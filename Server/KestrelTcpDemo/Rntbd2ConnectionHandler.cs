using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.Documents.RntbdConstants;

namespace KestrelTcpDemo
{
    // This is the connection handler the framework uses to handle new incoming connections
    internal class Rntbd2ConnectionHandler : ConnectionHandler
    {
        private readonly Func<Stream, SslStream> _sslStreamFactory;

        private readonly IComputeHash computeHash;
        internal static readonly string AuthKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly byte[] testPayload;

        public Rntbd2ConnectionHandler()
        {
            computeHash = new StringHMACSHA256Hash(Rntbd2ConnectionHandler.AuthKey);
            testPayload = Encoding.UTF8.GetBytes(File.ReadAllText("TestData.json"));

            _sslStreamFactory = s => new SslStream(s, leaveInnerStreamOpen: true, userCertificateValidationCallback: null);
        }

        private SslDuplexPipe CreateSslDuplexPipe(IDuplexPipe transport, MemoryPool<byte> memoryPool)
        {
            StreamPipeReaderOptions inputPipeOptions = new StreamPipeReaderOptions
            (
                pool: memoryPool,
                //bufferSize: memoryPool.GetMinimumSegmentSize(),
                //minimumReadSize: memoryPool.GetMinimumAllocSize(),
                leaveOpen: true,
                useZeroByteReads: true
            );

            var outputPipeOptions = new StreamPipeWriterOptions
            (
                pool: memoryPool,
                leaveOpen: true
            );

            return new SslDuplexPipe(transport, inputPipeOptions, outputPipeOptions, _sslStreamFactory);
        }

        internal static X509Certificate2 GetServerCertificate(string serverName)
        {
            X509Store store = new X509Store("MY", StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);


            foreach (X509Certificate2 x509 in store.Certificates)
            {
                if (x509.HasPrivateKey)
                {
                    // TODO: Covering for "CN="
                    if (x509.SubjectName.Name.EndsWith(serverName))
                    {
                        return x509;
                    }
                }
            }

            throw new Exception("GetServerCertificate didn't find any");
        }

        private async Task DoOptionsBasedHandshakeAsync(ConnectionContext context, SslStream sslStream, CancellationToken cancellationToken)
        {
            var serverCert = GetServerCertificate("localhost");
            var sslOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = GetServerCertificate("localhost"),
                //ServerCertificateSelectionCallback = (object sender, string? hostName) => GetServerCertificate(hostName),
                ServerCertificateContext = SslStreamCertificateContext.Create(serverCert, additionalCertificates: null),
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
        }

        public override async Task OnConnectedAsync(ConnectionContext context)
        {
            await using (context)
            {
                var sslDuplexPipe = CreateSslDuplexPipe(
                    context.Transport,
                    context.Features.Get<IMemoryPoolFeature>()?.MemoryPool ?? MemoryPool<byte>.Shared);
                var sslStream = sslDuplexPipe.Stream;

                // Server SSL auth
                await DoOptionsBasedHandshakeAsync(context, sslStream, CancellationToken.None);

                // Process RntbdMessages
                await OnRntbdConnectionAsync(context.ConnectionId, sslDuplexPipe);
            }
        }

        public async Task OnRntbdConnectionAsync(string connectionId, IDuplexPipe sslDuplexPipe)
        {
            try
            { 
                await NegotiateRntbdContext(sslDuplexPipe);

                // Code to parse length prefixed encoding
                while (true)
                {
                    (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(sslDuplexPipe);
                    var buffer = readResult.Buffer;

                    if (length != -1) // request already completed
                    {
                        int responseLength = await ProcessMessageAsync(connectionId, sslDuplexPipe, buffer.Slice(0, length));

                        sslDuplexPipe.Input.AdvanceTo(readResult.Buffer.GetPosition(length), readResult.Buffer.End);
                    }
                }
            }
            catch (ConnectionResetException ex)
            {
                Trace.TraceInformation(ex.ToString());
            }
            catch (InvalidOperationException ex) // Connection reset dring Read/Write
            {
                Trace.TraceError(ex.ToString());
            }
            finally
            {
                Trace.TraceWarning($"Connection {connectionId} completed");

                // ConnectionContext.DisposeAsync() should take care of below
                //await connection.Transport.Input.CompleteAsync();
                //await connection.Transport.Output.CompleteAsync();
            }
        }

        // TODO: Multi part length prefixed payload (ex: incoming payload like create etc...)
        private static async ValueTask<(int, ReadResult)> ReadLengthPrefixedMessageFull(IDuplexPipe duplexPipe)
        {
            var input = duplexPipe.Input;
            int length = -1;

            ReadResult readResult = await input.ReadAtLeastAsync(4);
            var buffer = readResult.Buffer;
            if (!readResult.IsCompleted)
            {
                Debug.Assert(buffer.FirstSpan.Length >= 4);

                length = (int)BitConverter.ToUInt32(readResult.Buffer.FirstSpan.Slice(0, sizeof(UInt32)));

                if (buffer.Length < length) // Read at-least length
                {
                    input.AdvanceTo(buffer.Start, readResult.Buffer.End);
                    readResult = await input.ReadAtLeastAsync(length);
                }
            }

            return (length, readResult);
        }

        private static async Task NegotiateRntbdContext(IDuplexPipe duplexPipe)
        {
            // RntbdContext negotiation
            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(duplexPipe);
            var buffer = readResult.Buffer;

            // TODO: Incoming context validation 
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + 16;

            byte[] deserializePayload = buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray();
            RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            Deserialize(deserializePayload, request);

            // Mark the incoming message as consumed
            // TODO: Test exception scenarios (i.e. if processing alter fails its impact)
            duplexPipe.Input.AdvanceTo(buffer.GetPosition(length), readResult.Buffer.End);

            // Send response 
            await RntbdConstants.ConnectionContextResponse.Serialize(200, Guid.NewGuid(), duplexPipe.Output);
        }

        private static void Deserialize<T>(
            byte[] deserializePayload,
            RntbdTokenStream<T> request) where T : Enum
        {
            BytesDeserializer reader = new BytesDeserializer(deserializePayload, deserializePayload.Length);
            request.ParseFrom(ref reader);
        }

        private static void DeserializeReqeust<T>(
            byte[] deserializePayload,
            out RntbdConstants.RntbdResourceType resourceType,
            out RntbdConstants.RntbdOperationType operationType,
            out Guid operationId,
            RntbdTokenStream<T> request) where T : Enum
        {
            BytesDeserializer reader = new BytesDeserializer(deserializePayload, deserializePayload.Length);

            // Format: {ResourceType: 2}, {OperationType: 2}, {Guid: 16)
            resourceType = (RntbdConstants.RntbdResourceType)reader.ReadUInt16();
            operationType = (RntbdConstants.RntbdOperationType)reader.ReadUInt16();
            operationId = reader.ReadGuid();

            request.ParseFrom(ref reader);
        }

        private async Task<int> ProcessMessageAsync(
            string connectionId,
            IDuplexPipe sslDuplexPipe,
            ReadOnlySequence<byte> buffer)
        {
            // TODO: Avoid array materialization
            byte[] deserializePayload = buffer.Slice(4, buffer.Length - 4).ToArray();

            Request request = new Request();
            DeserializeReqeust(deserializePayload,
                out RntbdConstants.RntbdResourceType resourceType,
                out RntbdConstants.RntbdOperationType operationType,
                out Guid operationId,
                request);

            //if (request)
            string dbName = BytesSerializer.GetStringFromBytes(request.databaseName.value.valueBytes);
            string collectionName = BytesSerializer.GetStringFromBytes(request.collectionName.value.valueBytes);
            string itemName = BytesSerializer.GetStringFromBytes(request.documentName.value.valueBytes);

            string dateHeader = BytesSerializer.GetStringFromBytes(request.date.value.valueBytes);
            string authHeaderValue = BytesSerializer.GetStringFromBytes(request.authorizationToken.value.valueBytes);

            if (resourceType == RntbdResourceType.Document && operationType == RntbdOperationType.Read)
            {
                string authorization = AuthorizationHelper.GenerateKeyAuthorizationCore("GET",
                    dateHeader,
                    "docs",
                    String.Format($"dbs/{dbName}/colls/{collectionName}/docs/{itemName}"),
                    this.computeHash);
                if (authorization != authHeaderValue)
                {
                    // TODO: Rntbd handling 
                    throw new Exception("Unauthorized");
                }
            }

            Response response = new Response();
            response.payloadPresent.value.valueByte = (byte)1;
            response.payloadPresent.isPresent = true;

            response.transportRequestID.value.valueULong = request.transportRequestID.value.valueULong;
            response.transportRequestID.isPresent = true;

            response.requestCharge.value.valueDouble = 1.0;
            response.requestCharge.isPresent = true;

            Trace.TraceError($"Processing {connectionId} -> {request.transportRequestID.value.valueULong}");

            int totalResponselength = sizeof(UInt32) + sizeof(UInt32) + 16;
            totalResponselength += response.CalculateLength();

            Memory<byte> bytes = sslDuplexPipe.Output.GetMemory(totalResponselength);
            int serializedLength = Rntbd2ConnectionHandler.Serialize(totalResponselength, 200, operationId, response, testPayload, bytes);

            sslDuplexPipe.Output.Advance(serializedLength);
            await sslDuplexPipe.Output.FlushAsync();

            return serializedLength;
        }

        internal static int Serialize<T>(
            int totalResponselength,
            uint statusCode,
            Guid activityId,
            RntbdTokenStream<T> contextResponse,
            byte[] payload,
            Memory<byte> bytes) where T : Enum
        {
            BytesSerializer writer = new BytesSerializer(bytes.Span);
            writer.Write(totalResponselength);
            writer.Write((UInt32)statusCode);
            writer.Write(activityId.ToByteArray());

            contextResponse.SerializeToBinaryWriter(ref writer, out _);
            if (payload == null)
            {
                return totalResponselength;
            }

            writer.Write(payload.Length); // Interesting: **body lenth deviated from other length prefixing (doesn't includes length size)
            writer.Write(payload);

            return totalResponselength + sizeof(UInt32) + payload.Length;
        }
    }

    internal sealed class SslDuplexPipe : DuplexPipeStreamAdapter<SslStream>
    {
        public SslDuplexPipe(IDuplexPipe transport, StreamPipeReaderOptions readerOptions, StreamPipeWriterOptions writerOptions)
            : this(transport, readerOptions, writerOptions, s => new SslStream(s))
        {
        }

        public SslDuplexPipe(IDuplexPipe transport, StreamPipeReaderOptions readerOptions, StreamPipeWriterOptions writerOptions, Func<Stream, SslStream> factory) :
            base(transport, readerOptions, writerOptions, factory)
        {
        }
    }

    internal class DuplexPipeStreamAdapter<TStream> : DuplexPipeStream, IDuplexPipe where TStream : Stream
    {
        private bool _disposed;
        private readonly object _disposeLock = new object();

        public DuplexPipeStreamAdapter(IDuplexPipe duplexPipe, Func<Stream, TStream> createStream) :
            this(duplexPipe, new StreamPipeReaderOptions(leaveOpen: true), new StreamPipeWriterOptions(leaveOpen: true), createStream)
        {
        }

        public DuplexPipeStreamAdapter(IDuplexPipe duplexPipe, StreamPipeReaderOptions readerOptions, StreamPipeWriterOptions writerOptions, Func<Stream, TStream> createStream) :
            base(duplexPipe.Input, duplexPipe.Output)
        {
            var stream = createStream(this);
            Stream = stream;
            Input = PipeReader.Create(stream, readerOptions);
            Output = PipeWriter.Create(stream, writerOptions);
        }

        public TStream Stream { get; }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public override async ValueTask DisposeAsync()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }

            await Input.CompleteAsync();
            await Output.CompleteAsync();
        }

        protected override void Dispose(bool disposing)
        {
            throw new NotSupportedException();
        }
    }

    internal class DuplexPipeStream : Stream
    {
        private readonly PipeReader _input;
        private readonly PipeWriter _output;
        private readonly bool _throwOnCancelled;
        private volatile bool _cancelCalled;

        public DuplexPipeStream(PipeReader input, PipeWriter output, bool throwOnCancelled = false)
        {
            _input = input;
            _output = output;
            _throwOnCancelled = throwOnCancelled;
        }

        public void CancelPendingRead()
        {
            _cancelCalled = true;
            _input.CancelPendingRead();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValueTask<int> vt = ReadAsyncInternal(new Memory<byte>(buffer, offset, count), default);
            return vt.IsCompleted ?
                vt.Result :
                vt.AsTask().GetAwaiter().GetResult();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            return ReadAsyncInternal(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            return ReadAsyncInternal(destination, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        public override Task WriteAsync(byte[]? buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _output.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).GetAsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            return _output.WriteAsync(source, cancellationToken).GetAsValueTask();
        }

        public override void Flush()
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _output.FlushAsync(cancellationToken).GetAsTask();
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        private async ValueTask<int> ReadAsyncInternal(Memory<byte> destination, CancellationToken cancellationToken)
        {
            while (true)
            {
                var result = await _input.ReadAsync(cancellationToken);
                var readableBuffer = result.Buffer;
                try
                {
                    if (_throwOnCancelled && result.IsCanceled && _cancelCalled)
                    {
                        // Reset the bool
                        _cancelCalled = false;
                        throw new OperationCanceledException();
                    }

                    if (!readableBuffer.IsEmpty)
                    {
                        // buffer.Count is int
                        var count = (int)Math.Min(readableBuffer.Length, destination.Length);
                        readableBuffer = readableBuffer.Slice(0, count);
                        readableBuffer.CopyTo(destination.Span);
                        return count;
                    }

                    if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    _input.AdvanceTo(readableBuffer.End, readableBuffer.End);
                }
            }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            return TaskToApm.Begin(ReadAsync(buffer, offset, count), callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return TaskToApm.End<int>(asyncResult);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            return TaskToApm.Begin(WriteAsync(buffer, offset, count), callback, state);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            TaskToApm.End(asyncResult);
        }
    }

    internal static class TaskToApm
    {
        /// <summary>
        /// Marshals the Task as an IAsyncResult, using the supplied callback and state
        /// to implement the APM pattern.
        /// </summary>
        /// <param name="task">The Task to be marshaled.</param>
        /// <param name="callback">The callback to be invoked upon completion.</param>
        /// <param name="state">The state to be stored in the IAsyncResult.</param>
        /// <returns>An IAsyncResult to represent the task's asynchronous operation.</returns>
        public static IAsyncResult Begin(Task task, AsyncCallback? callback, object? state) =>
            new TaskAsyncResult(task, state, callback);

        /// <summary>Processes an IAsyncResult returned by Begin.</summary>
        /// <param name="asyncResult">The IAsyncResult to unwrap.</param>
        public static void End(IAsyncResult asyncResult)
        {
            if (asyncResult is TaskAsyncResult twar)
            {
                twar._task.GetAwaiter().GetResult();
                return;
            }

            throw new ArgumentNullException(nameof(asyncResult));
        }

        /// <summary>Processes an IAsyncResult returned by Begin.</summary>
        /// <param name="asyncResult">The IAsyncResult to unwrap.</param>
        public static TResult End<TResult>(IAsyncResult asyncResult)
        {
            if (asyncResult is TaskAsyncResult twar && twar._task is Task<TResult> task)
            {
                return task.GetAwaiter().GetResult();
            }

            throw new ArgumentNullException(nameof(asyncResult));
        }

        /// <summary>Provides a simple IAsyncResult that wraps a Task.</summary>
        /// <remarks>
        /// We could use the Task as the IAsyncResult if the Task's AsyncState is the same as the object state,
        /// but that's very rare, in particular in a situation where someone cares about allocation, and always
        /// using TaskAsyncResult simplifies things and enables additional optimizations.
        /// </remarks>
        internal sealed class TaskAsyncResult : IAsyncResult
        {
            /// <summary>The wrapped Task.</summary>
            internal readonly Task _task;
            /// <summary>Callback to invoke when the wrapped task completes.</summary>
            private readonly AsyncCallback? _callback;

            /// <summary>Initializes the IAsyncResult with the Task to wrap and the associated object state.</summary>
            /// <param name="task">The Task to wrap.</param>
            /// <param name="state">The new AsyncState value.</param>
            /// <param name="callback">Callback to invoke when the wrapped task completes.</param>
            internal TaskAsyncResult(Task task, object? state, AsyncCallback? callback)
            {
                Debug.Assert(task != null);
                _task = task;
                AsyncState = state;

                if (task.IsCompleted)
                {
                    // Synchronous completion.  Invoke the callback.  No need to store it.
                    CompletedSynchronously = true;
                    callback?.Invoke(this);
                }
                else if (callback != null)
                {
                    // Asynchronous completion, and we have a callback; schedule it. We use OnCompleted rather than ContinueWith in
                    // order to avoid running synchronously if the task has already completed by the time we get here but still run
                    // synchronously as part of the task's completion if the task completes after (the more common case).
                    _callback = callback;
                    _task.ConfigureAwait(continueOnCapturedContext: false)
                         .GetAwaiter()
                         .OnCompleted(InvokeCallback); // allocates a delegate, but avoids a closure
                }
            }

            /// <summary>Invokes the callback.</summary>
            private void InvokeCallback()
            {
                Debug.Assert(!CompletedSynchronously);
                Debug.Assert(_callback != null);
                _callback.Invoke(this);
            }

            /// <summary>Gets a user-defined object that qualifies or contains information about an asynchronous operation.</summary>
            public object? AsyncState { get; }
            /// <summary>Gets a value that indicates whether the asynchronous operation completed synchronously.</summary>
            /// <remarks>This is set lazily based on whether the <see cref="_task"/> has completed by the time this object is created.</remarks>
            public bool CompletedSynchronously { get; }
            /// <summary>Gets a value that indicates whether the asynchronous operation has completed.</summary>
            public bool IsCompleted => _task.IsCompleted;
            /// <summary>Gets a <see cref="WaitHandle"/> that is used to wait for an asynchronous operation to complete.</summary>
            public WaitHandle AsyncWaitHandle => ((IAsyncResult)_task).AsyncWaitHandle;
        }
    }

    internal static class ValueTaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task GetAsTask(this in ValueTask<FlushResult> valueTask)
        {
            // Try to avoid the allocation from AsTask
            if (valueTask.IsCompletedSuccessfully)
            {
                // Signal consumption to the IValueTaskSource
                valueTask.GetAwaiter().GetResult();
                return Task.CompletedTask;
            }
            else
            {
                return valueTask.AsTask();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueTask GetAsValueTask(this in ValueTask<FlushResult> valueTask)
        {
            // Try to avoid the allocation from AsTask
            if (valueTask.IsCompletedSuccessfully)
            {
                // Signal consumption to the IValueTaskSource
                valueTask.GetAwaiter().GetResult();
                return default;
            }
            else
            {
                return new ValueTask(valueTask.AsTask());
            }
        }
    }

}
