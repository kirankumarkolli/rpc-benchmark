//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using Microsoft.Azure.Cosmos.Rntbd;
    using Microsoft.Azure.Documents;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO.Pipelines;
    using System.IO;
    using System.Linq;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Net;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static Microsoft.Azure.Documents.RntbdConstants;
    using System.Buffers;
    using Microsoft.Azure.Documents.Rntbd;
    using System.Globalization;
    using Microsoft.Azure.Cosmos;
    using System.Runtime.InteropServices;

    internal class CosmosDuplexPipe : IDuplexPipe, IDisposable
    {
        private static readonly string AuthorizationFormatPrefixUrlEncoded = HttpUtility.UrlEncode(string.Format(CultureInfo.InvariantCulture, Constants.Properties.AuthorizationFormat,
                Constants.Properties.MasterToken,
                Constants.Properties.TokenVersion,
                string.Empty));
        
        private readonly NetworkStream stream;
        private readonly PipeReader pipeReader;
        private readonly PipeWriter pipeWriter;

        private int nextRequestId = 0;

        static readonly string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly IComputeHash authKeyHashFunction = new StringHMACSHA256Hash(authKey);

        private static readonly Lazy<ConcurrentPrng> rng =
            new Lazy<ConcurrentPrng>(LazyThreadSafetyMode.ExecutionAndPublication);

        private CosmosDuplexPipe(NetworkStream ns, PipeReader reader, PipeWriter writer)
        {
            this.stream = ns;

            this.pipeReader = reader;
            this.pipeWriter = writer;
        }

        public PipeReader Input => this.pipeReader;

        public PipeWriter Output => this.pipeWriter;

        public void Dispose()
        {
            if (this.stream != null)
            {
                this.stream.Dispose();
            }
        }

        private static async Task<IPAddress> ResolveHostAsync(string hostName)
        {
            IPAddress[] serverAddresses = await Dns.GetHostAddressesAsync(hostName);
            int addressIndex = 0;
            if (serverAddresses.Length > 1)
            {
                addressIndex = rng.Value.Next(serverAddresses.Length);
            }
            return serverAddresses[addressIndex];
        }

        public async Task ReadDocumentAsync(
            string replicaPath,
            string databaseName,
            string contaienrName,
            string itemName,
            string partitionKey)
        {
            FlushResult flushResult = await this.SendReadRequestAsync(replicaPath, databaseName, contaienrName, itemName, partitionKey);

            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFullToConsume(this.pipeReader);

            // TODO: Incoming context validation 
            // sizeof(UInt32) -> Length-prefix
            // sizeof(UInt32) -> Status code
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt32) + BytesSerializer.GetSizeOfGuid();
            uint statusCode = MemoryMarshal.Read<uint>(readResult.Buffer.FirstSpan.Slice(sizeof(UInt32)));
            if (statusCode > 399)
            {
                throw new Exception($"Non success status code: {statusCode}");
            }

            byte[] deserializePayload = readResult.Buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray();
            RntbdConstants.Response response = new();
            Deserialize(deserializePayload, response);

            // ACK: Consumed (Broken abstraction :-( )
            this.pipeReader.AdvanceTo(readResult.Buffer.GetPosition(length), readResult.Buffer.End);

            if (response.payloadPresent.isPresent && response.payloadPresent.value.valueByte != 0x00)
            {
                // Payload is present 
                (length, readResult) = await ReadLengthPrefixedMessageFullToConsume(this.pipeReader);

                // TODO: Response buffer 

                // ACK: Consumed (Broken abstraction :-( )
                this.pipeReader.AdvanceTo(readResult.Buffer.GetPosition(length), readResult.Buffer.End);
            }
        }

        public ValueTask<FlushResult> SendReadRequestAsync(
            string replicaPath, 
            string databaseName, 
            string contaienrName,
            string itemName,
            string partitionKey)
        {
            uint requestId = unchecked((uint)Interlocked.Increment(ref this.nextRequestId));
            Guid activityId = Guid.NewGuid();

            string resourceId = $"dbs/{databaseName}/colls/{contaienrName}/docs/{itemName}";

            using TransportSerialization.RntbdRequestPool.RequestOwner owner = TransportSerialization.RntbdRequestPool.Instance.Get();
            RntbdConstants.Request rntbdRequest = owner.Request;

            rntbdRequest.partitionKey.value.valueBytes = BytesSerializer.GetBytesForString(partitionKey, rntbdRequest);
            rntbdRequest.partitionKey.isPresent = true;

            //rntbdRequest.resourceId.value.valueBytes = ResourceId.Parse("Qpd0AJ7VlTw=").Value;
            //rntbdRequest.resourceId.isPresent = true;

            //rntbdRequest.collectionRid.value.valueBytes = BytesSerializer.GetBytesForString("Qpd0AJ7VlTw=", rntbdRequest);
            //rntbdRequest.collectionRid.isPresent = true;

            //rntbdRequest.clientRetryAttemptCount.value.valueULong = 0;
            //rntbdRequest.clientRetryAttemptCount.isPresent = true;

            //rntbdRequest.remainingTimeInMsOnClientRequest.value.valueULong = 30000;
            //rntbdRequest.remainingTimeInMsOnClientRequest.isPresent = true;

            rntbdRequest.replicaPath.value.valueBytes = BytesSerializer.GetBytesForString(replicaPath, rntbdRequest);
            rntbdRequest.replicaPath.isPresent = true;

            rntbdRequest.databaseName.value.valueBytes = BytesSerializer.GetBytesForString(databaseName, rntbdRequest);
            rntbdRequest.databaseName.isPresent = true;

            rntbdRequest.collectionName.value.valueBytes = BytesSerializer.GetBytesForString(contaienrName, rntbdRequest);
            rntbdRequest.collectionName.isPresent = true;

            rntbdRequest.documentName.value.valueBytes = BytesSerializer.GetBytesForString(itemName, rntbdRequest);
            rntbdRequest.documentName.isPresent = true;

            string dateHeaderValue = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
            rntbdRequest.date.value.valueBytes = BytesSerializer.GetBytesForString(dateHeaderValue, rntbdRequest);
            rntbdRequest.date.isPresent = true;

            string authorizationToken = AuthorizationHelper.GenerateKeyAuthorizationCore(
                                                    verb: "GET", 
                                                    resourceId: resourceId,
                                                    resourceType: "docs",
                                                    date: dateHeaderValue,
                                                    computeHash: authKeyHashFunction);
            string authorization = CosmosDuplexPipe.AuthorizationFormatPrefixUrlEncoded + HttpUtility.UrlEncode(authorizationToken);
            rntbdRequest.authorizationToken.value.valueBytes = BytesSerializer.GetBytesForString(authorization, rntbdRequest);
            rntbdRequest.authorizationToken.isPresent = true;

            rntbdRequest.transportRequestID.value.valueULong = requestId;
            rntbdRequest.transportRequestID.isPresent = true;

            rntbdRequest.payloadPresent.value.valueByte = 0x00;
            rntbdRequest.payloadPresent.isPresent = true;

            // Once all metadata tokens are set, we can calculate the length.
            int metadataLength = (sizeof(uint) + sizeof(ushort) + sizeof(ushort) + BytesSerializer.GetSizeOfGuid());
            int headerAndMetadataLength = metadataLength + rntbdRequest.CalculateLength(); // metadata tokens
            int allocationLength = headerAndMetadataLength;

            Memory<byte> bytes = this.pipeWriter.GetMemory(allocationLength);
            BytesSerializer writer = new BytesSerializer(bytes.Span);

            // header
            writer.Write((uint)headerAndMetadataLength);
            writer.Write((ushort)RntbdConstants.RntbdResourceType.Document);
            writer.Write((ushort)RntbdConstants.RntbdOperationType.Read);
            writer.Write(activityId);
            int actualWritten = metadataLength;

            // metadata
            rntbdRequest.SerializeToBinaryWriter(ref writer, out int tokensLength);
            actualWritten += tokensLength;

            if (actualWritten != allocationLength)
            {
                throw new Exception($"Unexpected length mis-match actualWritten:{actualWritten} allocationLength:{allocationLength}");
            }

            this.pipeWriter.Advance(allocationLength);
            return this.pipeWriter.FlushAsync();
        }

        public static async Task<CosmosDuplexPipe> Connect(Uri endpoint)
        {
            IPAddress resolvedAddress = await ResolveHostAsync(endpoint.DnsSafeHost);
            TcpClient tcpClient = new TcpClient(resolvedAddress.AddressFamily);
            Connection.SetCommonSocketOptions(tcpClient.Client);

            await tcpClient.ConnectAsync(resolvedAddress, endpoint.Port);

            // Per MSDN, "The Blocking property has no effect on asynchronous methods"
            // (https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.blocking),
            // but we also try to get the health status of the socket with a 
            // non-blocking, zero-byte Send.
            // tcpClient.Client.Blocking = false;

            var ns = tcpClient.GetStream();
            SslStream sslStream = new SslStream(
                innerStream: ns,
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                        {
                            return true;
                        });

            await sslStream.AuthenticateAsClientAsync(
                targetHost: endpoint.DnsSafeHost,
                clientCertificates: null,
                enabledSslProtocols: SslProtocols.Tls12, 
                checkCertificateRevocation: false);

            var pipeWriter = PipeWriter.Create(sslStream, new StreamPipeWriterOptions(leaveOpen: true));
            var pipeReader = PipeReader.Create(sslStream, new StreamPipeReaderOptions(leaveOpen: true));

            CosmosDuplexPipe duplexPipe = new CosmosDuplexPipe(ns, pipeReader, pipeWriter);
            await CosmosDuplexPipe.NegotiateRntbdContext(duplexPipe);

            return duplexPipe;
        }

        private static void Deserialize<T>(
            byte[] deserializePayload,
            RntbdTokenStream<T> responseType) where T : Enum
        {
            BytesDeserializer bytesDeserializer = new BytesDeserializer(deserializePayload, deserializePayload.Length);
            responseType.ParseFrom(ref bytesDeserializer);
        }

        // TODO: Multi part length prefixed payload (ex: incoming payload like create etc...)
        // Caller needs to advance the reader for length
        //  TODO: Add message handler model to abstract out the consumption aspect's
        private static async ValueTask<(int, ReadResult)> ReadLengthPrefixedMessageFullToConsume(PipeReader pipeReader)
        {
            int length = -1;

            ReadResult readResult = await pipeReader.ReadAtLeastAsync(4);
            var buffer = readResult.Buffer;
            if (!readResult.IsCompleted)
            {
                Debug.Assert(buffer.FirstSpan.Length >= 4);

                length = (int)BitConverter.ToUInt32(readResult.Buffer.FirstSpan.Slice(0, sizeof(UInt32)));

                if (buffer.Length < length) // Read at-least length (included length 4-bytes as well)
                {
                    pipeReader.AdvanceTo(buffer.Start, readResult.Buffer.End); // Not yet consumed
                    readResult = await pipeReader.ReadAtLeastAsync(length);
                }

                Debug.Assert(readResult.Buffer.Length >= length);
            }

            if (length == -1 || readResult.Buffer.Length < length)
            {
                // TODO: clean-up (for POC its fine)
                throw new Exception($"{nameof(ReadLengthPrefixedMessageFullToConsume)} failed length: {length}, readResult.Buffer.Length: {readResult.Buffer.Length}");
            }

            return (length, readResult);
        }


        private static async Task NegotiateRntbdContext(CosmosDuplexPipe duplexPipe)
        {
            await duplexPipe.SendRntbdContext();

            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFullToConsume(duplexPipe.Input);

            // TODO: Incoming context validation 
            // sizeof(UInt32) -> Length-prefix
            // sizeof(UInt32) -> Status code
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt32) + BytesSerializer.GetSizeOfGuid();

            byte[] deserializePayload = readResult.Buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray();
            ConnectionContextResponse response = new ConnectionContextResponse();
            Deserialize(deserializePayload, response);

            // ACK: Consumed (Broken abstraction :-( )
            duplexPipe.Input.AdvanceTo(readResult.Buffer.GetPosition(length), readResult.Buffer.End);
        }

        private ValueTask<FlushResult> SendRntbdContext(
            bool enableChannelMultiplexing = true)
        {
            Guid activityId = Guid.NewGuid();
            byte[] activityIdBytes = activityId.ToByteArray();

            RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            request.protocolVersion.value.valueULong = RntbdConstants.CurrentProtocolVersion;
            request.protocolVersion.isPresent = true;

            request.clientVersion.value.valueBytes = HttpConstants.Versions.CurrentVersionUTF8;
            request.clientVersion.isPresent = true;

            request.userAgent.value.valueBytes = Encoding.UTF8.GetBytes("IoPipelines");
            request.userAgent.isPresent = true;

            request.callerId.isPresent = false;

            //request.enableChannelMultiplexing.isPresent = true;
            //request.enableChannelMultiplexing.value.valueByte = enableChannelMultiplexing ? (byte)1 : (byte)0;

            int length = (sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + activityIdBytes.Length); // header
            length += request.CalculateLength(); // tokens

            Memory<byte> bytes = this.pipeWriter.GetMemory(length);
            BytesSerializer writer = new BytesSerializer(bytes.Span);

            // header
            writer.Write(length);
            writer.Write((ushort)RntbdConstants.RntbdResourceType.Connection);
            writer.Write((ushort)RntbdConstants.RntbdOperationType.Connection);
            writer.Write(activityIdBytes);

            // metadata
            request.SerializeToBinaryWriter(ref writer, out _);
            this.pipeWriter.Advance(length);

            return this.pipeWriter.FlushAsync();
        }
    }
}
