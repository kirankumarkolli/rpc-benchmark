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

    internal class CosmosDuplexPipe : IDuplexPipe, IDisposable
    {
        private readonly NetworkStream stream;
        private readonly PipeReader pipeReader;
        private readonly PipeWriter pipeWriter;

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
            var pipeWriter = PipeWriter.Create(ns, new StreamPipeWriterOptions(leaveOpen: true));

            SslStream readerStream = new SslStream(
                innerStream: ns,
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true);

            await readerStream.AuthenticateAsClientAsync(
                targetHost: endpoint.DnsSafeHost,
                clientCertificates: null,
                    enabledSslProtocols: SslProtocols.Tls12, checkCertificateRevocation: false);

            var pipeReader = PipeReader.Create(readerStream, new StreamPipeReaderOptions(leaveOpen: true));

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
        private static async ValueTask<(int, ReadResult)> ReadLengthPrefixedMessageFull(PipeReader pipeReader)
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
                    readResult = await pipeReader.ReadAtLeastAsync(length);
                }

                Debug.Assert(readResult.Buffer.Length >= length);
                pipeReader.AdvanceTo(buffer.Start, readResult.Buffer.GetPosition(length));
            }

            if (length == -1 || readResult.Buffer.Length < length)
            {
                // TODO: clean-up (for POC its fine)
                throw new Exception($"{nameof(ReadLengthPrefixedMessageFull)} failed length: {length}, readResult.Buffer.Length: {readResult.Buffer.Length}");
            }

            return (length, readResult);
        }

        private static async Task NegotiateRntbdContext(CosmosDuplexPipe duplexPipe)
        {
            await duplexPipe.SendRntbdContext();

            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(duplexPipe.Input);

            // TODO: Incoming context validation 
            // sizeof(UInt32) -> Length-prefix
            // sizeof(UInt16) -> Status code
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt16) + 16;

            byte[] deserializePayload = readResult.Buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray();
            ConnectionContextResponse response = new ConnectionContextResponse();
            Deserialize(deserializePayload, response);
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
