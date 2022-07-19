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
    using System.Globalization;
using Microsoft.Azure.Cosmos;
    using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos.Core.Trace;
    using KestrelTcpDemo;

    internal partial class CosmosDuplexPipe : IDisposable
    {
        private static readonly string AuthorizationFormatPrefixUrlEncoded = HttpUtility.UrlEncode(string.Format(CultureInfo.InvariantCulture, Constants.Properties.AuthorizationFormat,
                Constants.Properties.MasterToken,
                Constants.Properties.TokenVersion,
                string.Empty));
        
        private readonly Stream stream;

        private int nextRequestId = 0;

        static readonly string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly IComputeHash authKeyHashFunction = new StringHMACSHA256Hash(authKey);

        private static readonly Lazy<ConcurrentPrng> rng =
            new Lazy<ConcurrentPrng>(LazyThreadSafetyMode.ExecutionAndPublication);

        public CosmosDuplexPipe(Stream ns)
        {
            PipeReader pipeReader = PipeReader.Create(ns, new StreamPipeReaderOptions(leaveOpen: true));
            this.Reader = new LengthPrefixPipeReader(pipeReader);
            this.Writer = new LimitedPipeWriter(PipeWriter.Create(ns, new StreamPipeWriterOptions(leaveOpen: true)));

            this.stream = ns;
        }

        public LengthPrefixPipeReader Reader { get; }

        public LimitedPipeWriter Writer { get;  }

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
            await this.SendReadRequestAsync(replicaPath, databaseName, contaienrName, itemName, partitionKey);

            ReadOnlySequence<byte> messageBytesSequence = await this.Reader.MoveNextAsync(isLengthCountedIn: true, CancellationToken.None);

            // TODO: Incoming context validation 
            // sizeof(UInt32) -> Length-prefix
            // sizeof(UInt32) -> Status code
            // 16 <- Activity id (hard coded)
            UInt32 connectionContextOffet = (UInt32)(sizeof(UInt32) + sizeof(UInt32) + BytesSerializer.GetSizeOfGuid());
            UInt32 statusCode = RntbdRequestTokensIterator.ToUInt32(messageBytesSequence.Slice(sizeof(UInt32)));
            if (statusCode > 399 && statusCode != 404 && statusCode != 429)
            {
                throw new Exception($"Non success status code: {statusCode}");
            }

            if (ResponeHasPayload(messageBytesSequence))
            {
                // Payload is present 
                ReadOnlySequence<byte> payloadBytesSequence = await this.Reader.MoveNextAsync(isLengthCountedIn: false, CancellationToken.None);
            }
        }

        public ValueTask SendReadRequestAsync(
            string replicaPath, 
            string databaseName, 
            string contaienrName,
            string itemName,
            string partitionKey)
        {
            uint requestId = unchecked((uint)Interlocked.Increment(ref this.nextRequestId));
            Guid activityId = Guid.NewGuid();

            string resourceId = $"dbs/{databaseName}/colls/{contaienrName}/docs/{itemName}";

            using (RntbdRequestPool.RequestOwner owner = RntbdRequestPool.Instance.Get())
            {
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

                return this.Writer.GetMemoryAndFlushAsync(allocationLength,
                    (bytes) => {
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
                    });
            }
        }

        public static async Task<CosmosDuplexPipe> ConnectAsClientAsync(Uri endpoint)
        {
            IPAddress resolvedAddress = await ResolveHostAsync(endpoint.DnsSafeHost);
            TcpClient tcpClient = new TcpClient(resolvedAddress.AddressFamily);
            SetCommonSocketOptions(tcpClient.Client);

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

            CosmosDuplexPipe duplexPipe = new CosmosDuplexPipe(sslStream);
            await duplexPipe.NegotiateRntbdContextAsClient();

            return duplexPipe;
        }

        public static bool ResponeHasPayload(ReadOnlySequence<byte> messagebytesSequence)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messagebytesSequence);
            return iterator.ResponeHasPayload();
        }

        public async Task NegotiateRntbdContextAsClient()
        {
            await this.SendRntbdContext();

            ReadOnlySequence<byte> messageBytesSequence = await this.Reader.MoveNextAsync(isLengthCountedIn: true, CancellationToken.None);

            // TODO: Incoming response validation 
            // sizeof(UInt32) -> Length-prefix
            // sizeof(UInt32) -> Status code
            // 16 <- Activity id (hard coded)
            UInt32 connectionContextOffet = (UInt32)(sizeof(UInt32) + sizeof(UInt32) + BytesSerializer.GetSizeOfGuid());
            UInt32 statusCode = RntbdRequestTokensIterator.ToUInt32(messageBytesSequence.Slice(sizeof(UInt32)));
            if(statusCode != 200)
            {
                throw new Exception($"Rntbd context negotiation failed with: {statusCode}");
            }
            //ConnectionContextResponse response = new ConnectionContextResponse();
            // Deserialize(messageBytes, connectionContextOffet, length - connectionContextOffet, response);
        }

        public async Task NegotiateRntbdContextAsServer()
        {
            ReadOnlySequence<byte> messageBytesSequence = await this.Reader.MoveNextAsync(isLengthCountedIn: true, CancellationToken.None);

            // TODO: Context validation 
            //UInt32 connectionContextOffet = (UInt32)(sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + BytesSerializer.GetSizeOfGuid());
            //RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            // Deserialize(messageBytes, connectionContextOffet, length - connectionContextOffet, request);

            // Send response 
            await RntbdConstants.ConnectionContextResponse.Serialize(200, Guid.NewGuid(), this.Writer.PipeWriter);
        }

        private ValueTask SendRntbdContext(
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

            return this.Writer.GetMemoryAndFlushAsync(length,
                    (bytes) =>
                    {
                        BytesSerializer writer = new BytesSerializer(bytes.Span);

                        // header
                        writer.Write(length);
                        writer.Write((ushort)RntbdConstants.RntbdResourceType.Connection);
                        writer.Write((ushort)RntbdConstants.RntbdOperationType.Connection);
                        writer.Write(activityIdBytes);

                        // metadata
                        request.SerializeToBinaryWriter(ref writer, out _);
                    });
        }
    }
}
