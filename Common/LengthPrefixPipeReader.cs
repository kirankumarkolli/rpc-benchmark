using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CosmosBenchmark
{
    internal class LengthPrefixPipeReader
    {
        internal readonly PipeReader pipeReader;
        private readonly string diagnticsContext;

        private SequencePosition? consumedBytesPosition;
        private ReadOnlySequence<byte>? remainingBytesSequence = null;

        public LengthPrefixPipeReader(
            PipeReader pipeReader,
            string traceDiagnticsContext)
        {
            this.pipeReader = pipeReader;
            this.diagnticsContext = traceDiagnticsContext;
        }

        /// <summary>
        /// NOTE: Its a possibility that the length in message might be different from first return value 
        ///     isLengthCountedIn (true) : It matches 
        ///     isLengthCountedIn (false): Differs by sizeof(UINT32) more
        /// </summary>
        public async Task<ReadOnlySequence<byte>> MoveNextAsync(
                bool isLengthCountedIn,
                CancellationToken cancellationToken)
        {
            UInt32 nextMessageLength = 0;
            if (remainingBytesSequence.HasValue)
            {
                int remainingBytesLength = (int)this.remainingBytesSequence.Value.Length;
                if (remainingBytesLength >= sizeof(UInt32))
                {
                    // Is there space for at-leat length 
                    nextMessageLength = LengthPrefixPipeReader.ToUInt32(this.remainingBytesSequence.Value);
                    if (!isLengthCountedIn)
                    {
                        nextMessageLength += sizeof(UInt32);
                    }

                    if (remainingBytesLength >= nextMessageLength)
                    {
                        return ExtractMessage(nextMessageLength);
                    }
                }
            }

            // Previous message consumed advance the Reader
            // Also Advance only once per READAsync
            if (this.remainingBytesSequence.HasValue 
                && this.consumedBytesPosition.HasValue)
            {
                this.pipeReader.AdvanceTo(
                        this.consumedBytesPosition.Value,
                        this.remainingBytesSequence.Value.End);
            }

            this.remainingBytesSequence = null;
            this.consumedBytesPosition = null;

            (nextMessageLength, ReadResult ReadResult) = await ReadLengthPrefixedMessageFullToConsume(
                                                                this.pipeReader, 
                                                                isLengthCountedIn,
                                                                this.diagnticsContext,
                                                                cancellationToken);
            this.remainingBytesSequence = ReadResult.Buffer;

            return ExtractMessage(nextMessageLength);
        }

        private ReadOnlySequence<byte> ExtractMessage(uint nextMessageLength)
        {
            ReadOnlySequence<byte> fullMessageSequence = this.remainingBytesSequence.Value.Slice(this.remainingBytesSequence.Value.Start, nextMessageLength);
            this.remainingBytesSequence = this.remainingBytesSequence.Value.Slice(fullMessageSequence.End);

            // Update consumed (as message sequence is prepared)
            // Real reader.AdvanceTo will be done on next MoveNext()
            this.consumedBytesPosition = fullMessageSequence.End;
            return fullMessageSequence;
        }

        private static UInt32 ToUInt32(ReadOnlySequence<byte> lengthBytesSequence)
        {
            if (lengthBytesSequence.Length < sizeof(UInt32))
            {
                throw new ArgumentOutOfRangeException($"{nameof(lengthBytesSequence)} had less than {sizeof(UInt32)}");
            }

            if (lengthBytesSequence.IsSingleSegment)
            {
                ReadOnlySpan<byte> lengthBytesSpan = lengthBytesSequence.FirstSpan;

                return (UInt32)BitConverter.ToUInt32(lengthBytesSpan);
            }
            else
            {
                Span<byte> stackCopyBytes = stackalloc byte[sizeof(UInt32)];
                lengthBytesSequence.Slice(0, sizeof(UInt32)).CopyTo(stackCopyBytes);

                return (UInt32)BitConverter.ToUInt32(stackCopyBytes);
            }
        }

        private static async ValueTask<(UInt32, ReadResult)> ReadLengthPrefixedMessageFullToConsume(
            PipeReader pipeReader,
            bool isLengthCountedIn,
            string diagnticsContext,
            CancellationToken cancellationToken)
        {
            UInt32 length = 0;

            ReadResult readResult = await pipeReader.ReadAtLeastAsync(4, cancellationToken);

            var buffer = readResult.Buffer;
            if (!readResult.IsCompleted)
            {
                Debug.Assert(buffer.FirstSpan.Length >= 4);

                length = LengthPrefixPipeReader.ToUInt32(readResult.Buffer);
                if (!isLengthCountedIn)
                {
                    length += sizeof(UInt32);
                }

                if (buffer.Length < length) // Read at-least length (included length 4-bytes as well)
                {
                    pipeReader.AdvanceTo(buffer.Start, readResult.Buffer.End); // Not yet consumed
                    readResult = await pipeReader.ReadAtLeastAsync((int)length, cancellationToken);
                }

                Debug.Assert(readResult.Buffer.Length >= length);
            }

            if (readResult.IsCanceled || readResult.IsCompleted)
            {
                throw new Exception($"{nameof(ReadLengthPrefixedMessageFullToConsume)} failed, Context:{diagnticsContext} ReadResult IsCompleted:{readResult.IsCompleted} IsCancelled:{readResult.IsCanceled} cancellationToken: {cancellationToken.IsCancellationRequested}  ");
            }

            if (length == 0 || readResult.Buffer.Length < length)
            {
                // TODO: clean-up (for POC its fine)
                throw new Exception($"{nameof(ReadLengthPrefixedMessageFullToConsume)} failed, Context:{diagnticsContext} length: {length}, readResult.Buffer.Length: {readResult.Buffer.Length}");
            }

            return (length, readResult);
        }
    }
}
