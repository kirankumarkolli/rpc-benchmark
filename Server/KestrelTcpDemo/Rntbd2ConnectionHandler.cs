using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
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
        private readonly IComputeHash computeHash;
        internal static readonly string AuthKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly byte[] testPayload;
        private readonly Channel<RntbdReqeustContext> requestQueue = Channel.CreateBounded<RntbdReqeustContext>(
                                new BoundedChannelOptions(1000)
                                {
                                    AllowSynchronousContinuations = true,
                                    FullMode = BoundedChannelFullMode.DropOldest
                                }); // TODO: Drop handler to fail message gracefully 
        private readonly Task[] dequeueTasks;
        private CancellationTokenSource tokenSource = new CancellationTokenSource();

        public Rntbd2ConnectionHandler()
        {
            computeHash = new StringHMACSHA256Hash(Rntbd2ConnectionHandler.AuthKey);
            testPayload = Encoding.UTF8.GetBytes(File.ReadAllText("TestData.json"));

            dequeueTasks = new Task[Environment.ProcessorCount];
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                dequeueTasks[i] = DeqeueueAndProcess(tokenSource.Token); // TODO: When to dispose them ??
            }
        }

        private async Task DeqeueueAndProcess(CancellationToken token)
        {
            while (! token.IsCancellationRequested)
            {
                RntbdReqeustContext requestContext = await requestQueue.Reader.ReadAsync(token);
                await ProcessMessageAsync(requestContext);
            }
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            await using (connection)
            {
                try
                {
                    await NegotiateRntbdContext(connection);

                    // Code to parse length prefixed encoding
                    while (true)
                    {
                        (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(connection);
                        var buffer = readResult.Buffer;

                        if (length != -1) // request already completed
                        {
                            await ProcessMessageAsync(connection, buffer.Slice(0, length));

                            connection.Transport.Input.AdvanceTo(readResult.Buffer.GetPosition(length), readResult.Buffer.End);
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
                    Trace.TraceWarning($"Connection {connection.ConnectionId} completed");

                    // ConnectionContext.DisposeAsync() should take care of below
                    //await connection.Transport.Input.CompleteAsync();
                    //await connection.Transport.Output.CompleteAsync();
                }
            }
        }

        // TODO: Multi part length prefixed payload (ex: incoming payload like create etc...)
        private static async ValueTask<(int, ReadResult)> ReadLengthPrefixedMessageFull(ConnectionContext connection)
        {
            var input = connection.Transport.Input;
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

        private static async Task NegotiateRntbdContext(ConnectionContext connection)
        {
            // RntbdContext negotiation
            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(connection);
            var buffer = readResult.Buffer;

            // TODO: Incoming context validation 
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + 16;

            byte[] deserializePayload = buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray();
            RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            Deserialize(deserializePayload, request);

            // Mark the incoming message as consumed
            // TODO: Test exception scenarios (i.e. if processing alter fails its impact)
            connection.Transport.Input.AdvanceTo(buffer.GetPosition(length), readResult.Buffer.End);

            // Send response 
            await RntbdConstants.ConnectionContextResponse.Serialize(200, Guid.NewGuid(), connection.Transport.Output);
        }

        private static void Deserialize<T>(
            byte[] deserializePayload, 
            RntbdTokenStream<T> request) where T: Enum
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

        private Task ProcessMessageAsync(
            ConnectionContext connection,
            ReadOnlySequence<byte> buffer)
        {
            // TODO: Avoid array materialization
            byte[] deserializePayload = buffer.Slice(4, buffer.Length - 4).ToArray();
            return ProcessMessageAsync(connection, deserializePayload);
        }

        private async Task ProcessMessageAsync(
            ConnectionContext connection,
            byte[] deserializePayload)
        {
            RntbdReqeustContext context = new();
            context.OutputWriter = connection.Transport.Output;

            DeserializeReqeust(deserializePayload,
                out RntbdConstants.RntbdResourceType resourceType,
                out RntbdConstants.RntbdOperationType operationType,
                out Guid operationId,
                context.Request);

            context.ResourceType = resourceType;
            context.OperationType = operationType;
            context.ConnectionId = context.ConnectionId;

            await requestQueue.Writer.WriteAsync(context);
        }

        private async Task ProcessMessageAsync(RntbdReqeustContext context)
        {
            //if (request)
            string dbName = BytesSerializer.GetStringFromBytes(context.Request.databaseName.value.valueBytes);
            string collectionName = BytesSerializer.GetStringFromBytes(context.Request.collectionName.value.valueBytes);
            string itemName = BytesSerializer.GetStringFromBytes(context.Request.documentName.value.valueBytes);

            string dateHeader = BytesSerializer.GetStringFromBytes(context.Request.date.value.valueBytes);
            string authHeaderValue = BytesSerializer.GetStringFromBytes(context.Request.authorizationToken.value.valueBytes);

            if (context.ResourceType == RntbdResourceType.Document && context.OperationType == RntbdOperationType.Read)
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

            response.transportRequestID.value.valueULong = context.Request.transportRequestID.value.valueULong;
            response.transportRequestID.isPresent = true;

            response.requestCharge.value.valueDouble = 1.0;
            response.requestCharge.isPresent = true;

            Trace.TraceError($"Processing {context.ConnectionId} -> {context.Request.transportRequestID.value.valueULong}");

            int totalResponselength = sizeof(UInt32) + sizeof(UInt32) + 16;
            totalResponselength += response.CalculateLength();

            Memory<byte> bytes = context.OutputWriter.GetMemory(totalResponselength);
            int serializedLength = Rntbd2ConnectionHandler.Serialize(totalResponselength, 200, context.OperationId, response, testPayload, bytes);

            context.OutputWriter.Advance(serializedLength);
            await context.OutputWriter.FlushAsync();
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

        public class RntbdReqeustContext
        {
            internal RntbdConstants.RntbdResourceType ResourceType { get; set; }
            internal RntbdConstants.RntbdOperationType OperationType { get; set; }
            internal Guid OperationId { get; set; }
            internal Request Request { get; set; } = new();
            internal string ConnectionId { get; set; }

            // TODO: Can pipewriter be cached???
            internal PipeWriter OutputWriter { get; set; }
        }
    }
}
