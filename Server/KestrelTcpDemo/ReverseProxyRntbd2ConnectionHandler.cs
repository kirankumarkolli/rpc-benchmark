using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using CosmosBenchmark;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using static Microsoft.Azure.Documents.RntbdConstants;

namespace KestrelTcpDemo
{
    internal class ReverseProxyRntbd2ConnectionHandler : BaseRntbd2ConnectionHandler
    {
        public ReverseProxyRntbd2ConnectionHandler()
        {
        }

        public override async Task ProcessRntbdFlowsAsyncCore(string connectionId, CosmosDuplexPipe cosmosDuplexPipe)
        {
            // Each incoming connection will have its own out-bound connections
            // I.e. for an account its possible that #connections = <#incoming-connections>*<#replica-endpoints>

            // TODO: Replace concurrent dict with AsyncCache
            ConcurrentDictionary<string, CosmosDuplexPipe> outboundConnections = new ConcurrentDictionary<string, CosmosDuplexPipe>();

            while (true)
            {
                (UInt32 messageLength, byte[] messagebytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true);

                try
                {
                    await this.ProcessRntbdMessageAsync(
                        connectionId, 
                        cosmosDuplexPipe, 
                        outboundConnections,
                        messageLength, 
                        messagebytes);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messagebytes);
                }
            }
        }

        private async Task ProcessRntbdMessageAsync(
            string connectionId,
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            ConcurrentDictionary<string, CosmosDuplexPipe> outboundConnections,
            UInt32 messageLength,
            byte[] messageBytes)
        {
            // Process incoming request
            CosmosDuplexPipe cosmosDuplexPipe = await this.ProcessRntbdMessageRewrite(
                connectionId,
                incomingCosmosDuplexPipe,
                messageLength,
                messageBytes);

            // Process response stream (Synchronous)
            {
                (UInt32 responseMetadataLength, byte[] responseMetadataBytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true);

                Memory<byte> memory = incomingCosmosDuplexPipe.Writer.GetMemory((int)responseMetadataLength);
                responseMetadataBytes.CopyTo(memory);
                incomingCosmosDuplexPipe.Writer.Advance((int)responseMetadataLength);
            }

            // TODO: Assume payload is expected 
            {
                (UInt32 responsePayloadLength, byte[] responsePayloadBytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true);

                int totalLength = (int)responsePayloadLength + sizeof(UInt32);
                Memory<byte> memory = incomingCosmosDuplexPipe.Writer.GetMemory(totalLength);
                responsePayloadBytes.AsSpan(0, totalLength).CopyTo(memory.Span);
                incomingCosmosDuplexPipe.Writer.Advance(totalLength);
            }

            await incomingCosmosDuplexPipe.Writer.FlushAsync();
        }

        private async Task<CosmosDuplexPipe> ProcessRntbdMessageRewrite(
            string connectionId,
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            UInt32 messageLength,
            byte[] messageBytes)
        {
            // roueTo(replicaPath)
            // 
            // To extract
            //  1. Replica path     - RequestIdentifiers.ReplicaPath
            //  2. isPayloadPresent - RequestIdentifiers.PayloadPresent

            (bool hasPaylad, string replicaPath, int replicaPathLengthPosition, int replicaPathLength) = ReverseProxyRntbd2ConnectionHandler.ExtractContext(messageBytes, messageLength);
            (string routingPathHint, string passThroughPath) = ReverseProxyRntbd2ConnectionHandler.SplitsParts(replicaPath);
            Uri routingTargetEndpoint = this.GetRouteToEndpoint(routingPathHint);

            ReadOnlyMemory<byte> updatedReplicaPathMemory = GetBytesForString(passThroughPath);

            // TODO: Write updated payload 
            CosmosDuplexPipe outBoundDuplexPipe = await CosmosDuplexPipe.ConnectAsClientAsync(routingTargetEndpoint);

            ReverseProxyRntbd2ConnectionHandler.ReWriteReqeustReplicaPath(messageBytes,
                (int)messageLength,
                replicaPathLengthPosition, 
                replicaPathLength, 
                updatedReplicaPathMemory, 
                outBoundDuplexPipe);

            if (hasPaylad) //TODO: Combine with metadata write?? 
            {
                // Copy next message as well 
                (UInt32 incomingPayloadLength, byte[] incomngPayloadBytes) = await incomingCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: false);

                Memory<byte> payloadMemory = outBoundDuplexPipe.Writer.GetMemory((int)incomingPayloadLength);
                incomngPayloadBytes.CopyTo(payloadMemory);

                outBoundDuplexPipe.Writer.Advance((int)incomingPayloadLength);
            }

            await outBoundDuplexPipe.Writer.FlushAsync();
            return outBoundDuplexPipe;
        }

        public static (bool hasPayload, string replicaPath, int replicaPathLengthPosition, int replicaPathUtf8Length) ExtractContext(
                byte[] messageBytes, 
                UInt32 messageLength)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messageBytes, 0, (int)messageLength);
            return iterator.ExtractContext();
        }

        private static void ReWriteReqeustReplicaPath(
                byte[] incomingMessageBytes, 
                int incomingMessageBytesLength,
                int incomingReplicaPathLengthPosition, 
                int incomingReplicaPathLength, 
                ReadOnlyMemory<byte> updatedReplicaPathMemory, 
                CosmosDuplexPipe outBoundDuplexPipe)
        {
            int reWriteMessageLength = incomingMessageBytesLength - (incomingReplicaPathLength - updatedReplicaPathMemory.Length);
            Memory<byte> rewriteMemory = outBoundDuplexPipe.Writer.GetMemory(reWriteMessageLength);

            BytesSerializer writer = new BytesSerializer(rewriteMemory.Span);

            writer.Write(reWriteMessageLength); // Length 

            Span<byte> preReplicaPathBytes = incomingMessageBytes.AsSpan(sizeof(UInt32), incomingReplicaPathLengthPosition - sizeof(UInt32));
            writer.Write(preReplicaPathBytes); // preReplicaPathBytes - Includes replicapath (identifier, type)

            writer.Write((UInt16)updatedReplicaPathMemory.Length);
            writer.Write(updatedReplicaPathMemory);

            int postReplicaPathPosition = incomingReplicaPathLengthPosition + sizeof(UInt16) + incomingReplicaPathLength;
            Span<byte> postReplicaPathBytes = incomingMessageBytes.AsSpan(postReplicaPathPosition, incomingMessageBytesLength - postReplicaPathPosition);
            writer.Write(postReplicaPathBytes); // postReplicaPathBytes 

            //Request request = new Request();
            //byte[] rreWriteByes = rewriteMemory.ToArray();
            //InMemoryRntbd2ConnectionHandler.DeserializeReqeust(
            //        rreWriteByes,
            //        sizeof(UInt32),
            //        (int)reWriteMessageLength - sizeof(UInt32),
            //        out RntbdConstants.RntbdResourceType resourceType,
            //        out RntbdConstants.RntbdOperationType operationType,
            //        out Guid operationId,
            //        request);

            outBoundDuplexPipe.Writer.Advance(reWriteMessageLength);
        }

        private static ReadOnlyMemory<byte> GetBytesForString(string toConvert)
        {
            byte[] stringBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(toConvert.Length));
            int length = Encoding.UTF8.GetBytes(toConvert, 0, toConvert.Length, stringBuffer, 0);
            return new ReadOnlyMemory<byte>(stringBuffer, 0, length);
        }

        private Uri GetRouteToEndpoint(string routingPath)
        {
            return new Uri("rntbd://" + routingPath);
        }

        private static (string routingPath, string passThroughPath) SplitsParts(string replicaPath)
        {
            int startIndex = 0;
            if (replicaPath[0] == '/')
            {
                startIndex = 1;
            }

            int index = startIndex + 1;
            while(replicaPath[index] != '/')
            {
                index ++;
            }

            return (
                    replicaPath.Substring(startIndex, index - startIndex),
                    replicaPath.Substring(index) // Include separator
                    );
        }
    }
}
