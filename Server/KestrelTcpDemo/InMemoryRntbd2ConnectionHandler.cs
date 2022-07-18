using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using static Microsoft.Azure.Documents.RntbdConstants;
using Kestrel.Clone;
using System.Collections.Concurrent;
using CosmosBenchmark;

namespace KestrelTcpDemo
{
    // This is the connection handler the framework uses to handle new incoming connections
    internal class InMemoryRntbd2ConnectionHandler : BaseRntbd2ConnectionHandler
    {
        private readonly IComputeHash computeHash;
        internal static readonly string AuthKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly byte[] testPayload;

        public InMemoryRntbd2ConnectionHandler()
        {
            computeHash = new StringHMACSHA256Hash(InMemoryRntbd2ConnectionHandler.AuthKey);
            testPayload = Encoding.UTF8.GetBytes(File.ReadAllText("TestData.json"));
        }


        public override async Task ProcessRntbdFlowsAsyncCore(string connectionId, CosmosDuplexPipe cosmosDuplexPipe)
        {
            // Code to parse length prefixed encoding
            while (true)
            {
                (UInt32 messageLength, byte[] messagebytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true);

                try
                {
                    int responseLength = await ProcessRntbdMessageAsync(connectionId, cosmosDuplexPipe, messageLength, messagebytes);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messagebytes);
                }
            }
        }

        internal static void DeserializeReqeust<T>(
            byte[] deserializePayload,
            int startPoistion, 
            int length,
            out RntbdConstants.RntbdResourceType resourceType,
            out RntbdConstants.RntbdOperationType operationType,
            out Guid operationId,
            RntbdTokenStream<T> request) where T : Enum
        {
            BytesDeserializer reader = new BytesDeserializer(deserializePayload, startPoistion, length);

            // Format: {ResourceType: 2}, {OperationType: 2}, {Guid: 16)
            resourceType = (RntbdConstants.RntbdResourceType)reader.ReadUInt16();
            operationType = (RntbdConstants.RntbdOperationType)reader.ReadUInt16();
            operationId = reader.ReadGuid();

            request.ParseFrom(ref reader);
        }

        private async Task<int> ProcessRntbdMessageAsync(
            string connectionId,
            CosmosDuplexPipe cosmosDuplexPipe,
            UInt32 messageLength,
            byte[] messagebytes)
        {
            Request request = new Request();
            DeserializeReqeust(
                    messagebytes,
                    sizeof(UInt32),
                    (int)messageLength - sizeof(UInt32),
                    out RntbdConstants.RntbdResourceType resourceType,
                    out RntbdConstants.RntbdOperationType operationType,
                    out Guid operationId,
                    request);

            if (request.payloadPresent.isPresent
                && request.payloadPresent.value.valueByte != 0x00)
            {
                (UInt32 payloadLength, byte[] payloadBytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: false);
                ArrayPool<byte>.Shared.Return(payloadBytes);
            }

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
                // TODO: Auth format parsing 
                //if (authorization != authHeaderValue)
                //{
                //    // TODO: Rntbd handling 
                //    throw new Exception("Unauthorized");
                //}
            }

            Response response = new Response();
            response.payloadPresent.value.valueByte = (byte)1;
            response.payloadPresent.isPresent = true;

            response.transportRequestID.value.valueULong = request.transportRequestID.value.valueULong;
            response.transportRequestID.isPresent = true;

            response.requestCharge.value.valueDouble = 1.0;
            response.requestCharge.isPresent = true;

            int totalResponselength = sizeof(UInt32) + sizeof(UInt32) + 16;
            totalResponselength += response.CalculateLength();

            Memory<byte> bytes = cosmosDuplexPipe.Writer.GetMemory(totalResponselength);
            int serializedLength = InMemoryRntbd2ConnectionHandler.Serialize(totalResponselength, 200, operationId, response, testPayload, bytes);

            cosmosDuplexPipe.Writer.Advance(serializedLength);
            await cosmosDuplexPipe.Writer.FlushAsync();

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
