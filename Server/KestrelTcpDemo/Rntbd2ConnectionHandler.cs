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
using Kestrel.Clone;

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

        private SslDuplexPipe CreateSslDuplexPipe(IDuplexPipe duplexPipe, MemoryPool<byte> memoryPool)
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

            return new SslDuplexPipe(duplexPipe, inputPipeOptions, outputPipeOptions, _sslStreamFactory);
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
}
