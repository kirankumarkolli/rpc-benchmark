using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Text;
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
        private readonly IRntbdMessageParser _parser;
        private readonly IComputeHash computeHash;
        private static readonly string AuthKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly byte[] testPayload;

        public Rntbd2ConnectionHandler(IRntbdMessageParser parser)
        {
            _parser = parser;
            computeHash = new StringHMACSHA256Hash(Rntbd2ConnectionHandler.AuthKey);
            testPayload = Encoding.UTF8.GetBytes(File.ReadAllText("TestData.json"));
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
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

            BytesDeserializer reader = new BytesDeserializer(buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray(), length - connectionContextOffet);
            RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            request.ParseFrom(ref reader);

            // Mark the incoming message as consumed
            // TODO: Test exception scenarios (i.e. if processing alter fails its impact)
            connection.Transport.Input.AdvanceTo(buffer.GetPosition(length), readResult.Buffer.End);

            // Send response 
            await RntbdConstants.ConnectionContextResponse.Serialize(200, Guid.NewGuid(), connection.Transport.Output);
        }

        private async Task ProcessMessageAsync(
            ConnectionContext connection,
            ReadOnlySequence<byte> buffer)
        {
            BytesDeserializer reader = new BytesDeserializer(buffer.Slice(4, buffer.Length - 4).ToArray(), (int)buffer.Length - 4);

            // Format: {ResourceType: 2}, {OperationType: 2}, {Guid: 16)
            RntbdConstants.RntbdResourceType resourceType = (RntbdConstants.RntbdResourceType)reader.ReadUInt16();
            RntbdConstants.RntbdOperationType operationType = (RntbdConstants.RntbdOperationType)reader.ReadUInt16();
            Guid operationId = reader.ReadGuid();

            // Read reqeust 
            Request request = new Request();
            request.ParseFrom(ref reader);

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

            int totalResponselength = sizeof(UInt32) + sizeof(UInt32) + 16;
            totalResponselength += response.CalculateLength();

            Memory<byte> bytes = connection.Transport.Output.GetMemory(totalResponselength);
            int serializedLength = Rntbd2ConnectionHandler.Serialize(totalResponselength, 200, operationId, response, testPayload, bytes);
            connection.Transport.Output.Advance(serializedLength);

            await connection.Transport.Output.FlushAsync();
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
