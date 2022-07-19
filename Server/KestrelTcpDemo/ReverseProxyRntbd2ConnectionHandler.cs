using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CosmosBenchmark;
using Microsoft.Azure.Cosmos.Common;
using Microsoft.Azure.Cosmos.Rntbd;

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

        public override async Task ProcessRntbdFlowsAsyncCore(
            string connectionId, 
            CosmosDuplexPipe incominCosmosDuplexPipe)
        {
            // Each incoming connection will have its own out-bound connections
            // I.e. for an account its possible that #connections = <#incoming-connections>*<#replica-endpoints>
            AsyncCache<string, CosmosDuplexPipe> outboundConnections = new AsyncCache<string, CosmosDuplexPipe>();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await ProcessRntbdFlowsCoreInernalAsync(
                            connectionId, 
                            incominCosmosDuplexPipe, 
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
            CosmosDuplexPipe incominCosmosDuplexPipe,
            AsyncCache<string, CosmosDuplexPipe> outboundConnections,
            CancellationToken cancellationToken)
        {
            // TODO: Fix hard coding 
            byte[] tmpBytes = new byte[4 * 1024];

            while (true)
            {
                ReadOnlySequence<byte> messagebytesSequence = await incominCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true, cancellationToken);
                await this.ProcessRntbdMessageRewriteAsync(
                            connectionId,
                            incominCosmosDuplexPipe,
                            outboundConnections,
                            messagebytesSequence,
                            tmpBytes,
                            cancellationToken);
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
            ReadOnlySequence<byte> messagebytesSequence,
            byte[] tmpBytes,
            CancellationToken cancellationToken)
        {
            // roueTo(replicaPath)
            // 
            // To extract
            //  1. Replica path     - RequestIdentifiers.ReplicaPath
            //  2. isPayloadPresent - RequestIdentifiers.PayloadPresent

            (bool hasPaylad, SequencePosition replicaPathLengthPosition, int replicaPathLength) = ReverseProxyRntbd2ConnectionHandler.ExtractContext(messagebytesSequence, tmpBytes);
            (string routingPathHint, ReadOnlyMemory<byte> passThroughReplicaPath) = ReverseProxyRntbd2ConnectionHandler.SplitsParts(tmpBytes.AsMemory(0, replicaPathLength));
            Uri routingTargetEndpoint = this.GetRouteToEndpoint(routingPathHint);

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

            await ProcessRequestAndPayloadAsync(messagebytesSequence,
                        replicaPathLengthPosition,
                        replicaPathLength,
                        passThroughReplicaPath,
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
                ReadOnlySequence<byte> messagebytesSequence = await outboundCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: true, cancellationToken);

                bool hasPayload = ReverseProxyRntbd2ConnectionHandler.ResponeHasPayload(messagebytesSequence);
                await incomingCosmosDuplexPipe.Writer.GetMemoryAndFlushAsync((int)messagebytesSequence.Length,
                    (memory) =>
                    {
                        messagebytesSequence.CopyTo(memory.Span);
                    });

                if (hasPayload)
                {
                    ReadOnlySequence<byte> responsePayloadBytesSequence = await outboundCosmosDuplexPipe.Reader.MoveNextAsync(isLengthCountedIn: false, cancellationToken);
                    await incomingCosmosDuplexPipe.Writer.GetMemoryAndFlushAsync((int)responsePayloadBytesSequence.Length,
                        (memory) =>
                        {
                            responsePayloadBytesSequence.CopyTo(memory.Span);
                        });
                }
            }
        }

        private static async Task ProcessRequestAndPayloadAsync(
            ReadOnlySequence<byte> messagebytesSequence,
            SequencePosition incomingReplicaPathLengthPosition,
            int incomingReplicaPathLength,
            ReadOnlyMemory<byte> updatedReplicaPathMemory,
            CosmosDuplexPipe incomingCosmosDuplexPipe,
            CosmosDuplexPipe outBoundDuplexPipe,
            bool hasPayload,
            CancellationToken cancellationToken)
        {
            ReadOnlySequence<byte> incomingPayloadBytes = ReadOnlySequence<byte>.Empty;

            int requiredMemoryLength = (int)messagebytesSequence.Length - (incomingReplicaPathLength - updatedReplicaPathMemory.Length);
            if (hasPayload)
            {
                incomingPayloadBytes = await incomingCosmosDuplexPipe.Reader.MoveNextAsync(
                                                                        isLengthCountedIn: false,
                                                                        cancellationToken: cancellationToken);
                requiredMemoryLength += (int)incomingPayloadBytes.Length;
            }

            await outBoundDuplexPipe.Writer.GetMemoryAndFlushAsync(requiredMemoryLength,
                (memory) =>
                {
                    ReadOnlySequence<byte> preReplicaPathBytes = messagebytesSequence.Slice(sizeof(UInt32), incomingReplicaPathLengthPosition);
                    ReadOnlySequence<byte> postReplicaPathBytes = messagebytesSequence.Slice(incomingReplicaPathLengthPosition).Slice(sizeof(UInt16) + incomingReplicaPathLength);

                    BytesSerializer writer = new BytesSerializer(memory.Span);
                    writer.Write(requiredMemoryLength); // Length 
                    writer.Write(preReplicaPathBytes); // preReplicaPathBytes - Includes replicapath (identifier, type)
                    writer.Write((UInt16)updatedReplicaPathMemory.Length);
                    writer.Write(updatedReplicaPathMemory);
                    writer.Write(postReplicaPathBytes);

                    if (hasPayload)
                    {
                        writer.Write(incomingPayloadBytes);
                    }
                });
        }

        public static bool ResponeHasPayload(ReadOnlySequence<byte> messagebytesSequence)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messagebytesSequence);
            return iterator.ResponeHasPayload();
        }

        public static (bool, SequencePosition, int ) ExtractContext(
                ReadOnlySequence<byte> messagebytesSequence,
                byte[] tempBytes)
        {
            RntbdRequestTokensIterator iterator = new RntbdRequestTokensIterator(messagebytesSequence);
            return iterator.ExtractContext(tempBytes);
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

        private static (string routingPath, ReadOnlyMemory<byte> passThroughPathBytes) SplitsParts(ReadOnlyMemory<byte> replicaPathBytes)
        {
            ReadOnlySpan<byte> replicaPathBytesSpan = replicaPathBytes.Span;
            int startIndex = 0;
            if (replicaPathBytesSpan[0] == '/')
            {
                startIndex = 1;
            }

            int index = startIndex;
            for (; index < replicaPathBytesSpan.Length; index++)
            {
                if(replicaPathBytesSpan[index] == '/')
                {
                    break;
                }
            }

            if (index == replicaPathBytesSpan.Length)
            {
                throw new ArgumentOutOfRangeException($"Routing path separator not found");
            }

            string routingPath = GetStringFromBytes(replicaPathBytesSpan.Slice(startIndex, index - startIndex));
            return (
                    routingPath,
                    replicaPathBytes.Slice(index) // Include separator
                    );
        }

        private static unsafe string GetStringFromBytes(ReadOnlySpan<byte> memory)
        {
            if (memory.IsEmpty)
            {
                return string.Empty;
            }

            fixed (byte* bytes = &memory.GetPinnableReference())
            {
                return Encoding.UTF8.GetString(bytes, memory.Length);
            }
        }
    }
}
