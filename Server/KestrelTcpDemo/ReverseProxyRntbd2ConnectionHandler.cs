using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CosmosBenchmark;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Common;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using static Microsoft.Azure.Documents.RntbdConstants;

namespace KestrelTcpDemo
{
    /// <summary>
    /// TODO: 
    /// 1. Reads are serialized
    /// 2. Avoid full reqeuest bytes materialization
    /// 3. Sharing outbound connections across incoming connections
    ///     a. Dynamic #connection managment (based on bounds)
    ///     b. MUX handling (TransportRequestId)
    /// </summary>
    internal class ReverseProxyRntbd2ConnectionHandler : BaseRntbd2ConnectionHandler
    {
        public ReverseProxyRntbd2ConnectionHandler()
        {
        }

        public override async Task ProcessRntbdFlowsAsyncCore(string connectionId, CosmosDuplexPipe cosmosDuplexPipe)
        {
            // Each incoming connection will have its own out-bound connections
            // I.e. for an account its possible that #connections = <#incoming-connections>*<#replica-endpoints>
            AsyncCache<string, CosmosDuplexPipe> outboundConnections = new AsyncCache<string, CosmosDuplexPipe>();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await ProcessRntbdFlowsCoreInernalAsync(
                            connectionId, 
                            cosmosDuplexPipe, 
                            outboundConnections,
                            cancellationTokenSource.Token);
            }
            finally
            {
                // Clean-up out bound connections 
                cancellationTokenSource.Cancel();
                await DisposeAllOutboundConnections(outboundConnections);
            }
        }

        public async Task ProcessRntbdFlowsCoreInernalAsync(
            string connectionId, 
            CosmosDuplexPipe cosmosDuplexPipe,
            AsyncCache<string, CosmosDuplexPipe> outboundConnections,
            CancellationToken cancellationToken)
        {

            while (true)
            {
                (UInt32 messageLength, byte[] messagebytes) = await cosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true, cancellationToken);
                try
                {
                    await this.ProcessRntbdMessageRewriteAsync(
                                connectionId,
                                cosmosDuplexPipe,
                                outboundConnections,
                                messageLength,
                                messagebytes,
                                cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messagebytes);
                }
            }
        }

        private async Task DisposeAllOutboundConnections(AsyncCache<string, CosmosDuplexPipe> outboundConnections)
        {

            try
            {
                foreach (string entryKey in outboundConnections.Keys)
                {
                    CosmosDuplexPipe outboundPipe = await outboundConnections.GetAsync(entryKey, 
                            null, 
                            () => Task.FromResult<CosmosDuplexPipe>(null), 
                            CancellationToken.None);
                    outboundPipe.Dispose();
                }

                outboundConnections.Clear();
                outboundConnections = null;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($" {nameof(DisposeAllOutboundConnections)} failed with {ex.ToString()}");
            }
        }

        /// <summary>
        /// Reads the complete message from <paramref name="incomingCosmosDuplexPipe"/> and opens a connection and starts a receive loop.
        /// </summary>
        private async Task ProcessRntbdMessageRewriteAsync(
            string connectionId,
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            AsyncCache<string, CosmosDuplexPipe> outboundConnections,
            UInt32 messageLength,
            byte[] messageBytes,
            CancellationToken cancellationToken)
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

            // Get the outbound cosmos duplex pipe
            CosmosDuplexPipe outboundDuplexPipe = await outboundConnections.GetAsync(routingTargetEndpoint.AbsoluteUri,
                null,
                async () =>
                {
                    var outboundCosmosDuplexPipe = await CosmosDuplexPipe.ConnectAsClientAsync(routingTargetEndpoint);

                    // Start the receiveloop (async task)
                    Task backgroundTask = ProcessResponseAndPayloadAsync(
                                                incomingCosmosDuplexPipe, 
                                                outboundCosmosDuplexPipe,
                                                cancellationToken)
                                            .ContinueWith((task) =>
                                             {
                                                 Trace.TraceError(task.Exception.ToString());
                                             }, TaskContinuationOptions.OnlyOnFaulted);

                    return outboundCosmosDuplexPipe;
                },
                cancellationToken: default);

            await ProcessRequestAndPayloadAsync(messageBytes,
                        (int)messageLength,
                        replicaPathLengthPosition,
                        replicaPathLength,
                        updatedReplicaPathMemory,
                        incomingCosmosDuplexPipe,
                        outboundDuplexPipe,
                        hasPaylad,
                        cancellationToken);
        }

        /// <summary>
        /// Read a response from the real service replica <paramref name="outboundCosmosDuplexPipe"/> and write it to the client through <paramref name="incomingCosmosDuplexPipe"/>.
        /// </summary>
        private static async Task ProcessResponseAndPayloadAsync(
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            CosmosDuplexPipe outboundCosmosDuplexPipe,
            CancellationToken cancellationToken)
        {
            while (! cancellationToken.IsCancellationRequested)
            {
                bool hasPayload = false;
                (UInt32 responseMetadataLength, byte[] responseMetadataBytes) = await outboundCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true, cancellationToken);
                try
                {
                    hasPayload = ReverseProxyRntbd2ConnectionHandler.HasPayload(responseMetadataBytes, responseMetadataLength);

                    await incomingCosmosDuplexPipe.Writer.GetMemoryAndFlushAsync((int)responseMetadataLength,
                        (memory) =>
                        {
                            responseMetadataBytes.CopyTo(memory);
                        });
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(responseMetadataBytes);
                }

                if (hasPayload)
                {
                    (UInt32 responsePayloadLength, byte[] responsePayloadBytes) = await outboundCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: false, cancellationToken);
                    try
                    {
                        await incomingCosmosDuplexPipe.Writer.GetMemoryAndFlushAsync((int)responsePayloadLength,
                            (memory) =>
                            {
                                responsePayloadBytes.CopyTo(memory);
                            });
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(responsePayloadBytes);
                    }
                }
            }
        }

        private static async Task ProcessRequestAndPayloadAsync(byte[] incomingMessageBytes,
            int incomingMessageBytesLength,
            int incomingReplicaPathLengthPosition,
            int incomingReplicaPathLength,
            ReadOnlyMemory<byte> updatedReplicaPathMemory,
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            CosmosDuplexPipe outBoundDuplexPipe,
            bool hasPayload,
            CancellationToken cancellationToken)
        {
            byte[] incomingPayloadBytes = null;
            UInt32 incomingPayloadLength;
            
            try
            {
                int requiredMemoryLength = incomingMessageBytesLength - (incomingReplicaPathLength - updatedReplicaPathMemory.Length);
                if (hasPayload)
                {
                    (incomingPayloadLength, incomingPayloadBytes) = await incomingCosmosDuplexPipe.Reader.MoveNextAsync(
                                                                            isLengthCountedIn: false, 
                                                                            cancellationToken: cancellationToken);
                    requiredMemoryLength += (int)incomingPayloadLength;
                }

                await outBoundDuplexPipe.Writer.GetMemoryAndFlushAsync(requiredMemoryLength,
                    (memory) =>
                    {
                        BytesSerializer writer = new BytesSerializer(memory.Span);
                        writer.Write(requiredMemoryLength); // Length 

                        Span<byte> preReplicaPathBytes = incomingMessageBytes.AsSpan(sizeof(UInt32), incomingReplicaPathLengthPosition - sizeof(UInt32));
                        writer.Write(preReplicaPathBytes); // preReplicaPathBytes - Includes replicapath (identifier, type)

                        writer.Write((UInt16)updatedReplicaPathMemory.Length);
                        writer.Write(updatedReplicaPathMemory);

                        int postReplicaPathPosition = incomingReplicaPathLengthPosition + sizeof(UInt16) + incomingReplicaPathLength;
                        Span<byte> postReplicaPathBytes = incomingMessageBytes.AsSpan(postReplicaPathPosition, incomingMessageBytesLength - postReplicaPathPosition);
                        writer.Write(postReplicaPathBytes); // postReplicaPathBytes
                        if (hasPayload)
                        {
                            incomingPayloadBytes.CopyTo(memory);
                        }
                    });
            }
            finally
            {
                if (hasPayload
                    && incomingPayloadBytes != null)
                {
                    ArrayPool<byte>.Shared.Return(incomingPayloadBytes);
                }
            }
        }

        public static bool HasPayload(
                byte[] messageBytes,
                UInt32 messageLength)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messageBytes,
                0,
                (int)messageLength);
            return iterator.HasPayload();
        }

        public static (bool hasPayload, string replicaPath, int replicaPathLengthPosition, int replicaPathUtf8Length) ExtractContext(
                byte[] messageBytes, 
                UInt32 messageLength)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messageBytes, 0, (int)messageLength);
            return iterator.ExtractContext();
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
