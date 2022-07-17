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
    internal class InMemoryRntbd2ConnectionHandler : ConnectionHandler
    {
        private readonly Func<Stream, SslStream> _sslStreamFactory;
        private static ConcurrentDictionary<string, X509Certificate2> cachedCerts = new ConcurrentDictionary<string, X509Certificate2>();

        private readonly IComputeHash computeHash;
        internal static readonly string AuthKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly byte[] testPayload;

        public InMemoryRntbd2ConnectionHandler()
        {
            computeHash = new StringHMACSHA256Hash(InMemoryRntbd2ConnectionHandler.AuthKey);
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
            X509Store store = new X509Store("MY", StoreLocation.LocalMachine);
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
            //var serverCert = GetServerCertificate("backend-fake");
            var sslOptions = new SslServerAuthenticationOptions
            {
                //ServerCertificate = serverCert,
                ServerCertificateSelectionCallback = (object sender, string? hostName) =>
                {
                    if (cachedCerts.TryGetValue(hostName, out X509Certificate2 cachedCert))
                    {
                        return cachedCert;
                    }


                    X509Certificate2 newCert = GetServerCertificate(hostName);
                    cachedCerts.TryAdd(hostName, newCert);
                    return newCert;
                },
                //ServerCertificateContext = SslStreamCertificateContext.Create(serverCert, additionalCertificates: null),
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
                using (CosmosDuplexPipe cosmosDuplexPipe = new CosmosDuplexPipe(sslStream))
                {
                    // Process RntbdMessages
                    await OnRntbdConnectionAsync(context.ConnectionId, cosmosDuplexPipe);
                }
            }
        }

        public async Task OnRntbdConnectionAsync(string connectionId, CosmosDuplexPipe cosmosDuplexPipe)
        {
            try
            {
                await cosmosDuplexPipe.NegotiateRntbdContextAsServer();

                // Code to parse length prefixed encoding
                while (true)
                {
                    (UInt32 messageLength, byte[] messagebytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true);
                    int responseLength = await ProcessMessageAsync(connectionId, cosmosDuplexPipe, messageLength, messagebytes);
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

        private static void DeserializeReqeust<T>(
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

        private async Task<int> ProcessMessageAsync(
            string connectionId,
            CosmosDuplexPipe cosmosDuplexPipe,
            UInt32 messageLength,
            byte[] messageBytes)
        {
            try
            {
                Request request = new Request();
                DeserializeReqeust(
                        messageBytes,
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
            finally
            {
                ArrayPool<byte>.Shared.Return(messageBytes);
            }
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
