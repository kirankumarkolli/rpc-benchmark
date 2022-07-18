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
        private readonly PipeReader pipeReader;

        private UInt32 consumedBytesLength = 0;
        private ReadResult? readResult = null;

        public LengthPrefixPipeReader(PipeReader pipeReader)
        {
            this.pipeReader = pipeReader;
        }

        /// <summary>
        /// NOTE: Its a possibility that the length in message might be different from first return value 
        ///     isLengthCountedIn (true) : It matches 
        ///     isLengthCountedIn (false): Differs by sizeof(UINT32) more
        /// </summary>
        public async Task<(UInt32, byte[])> MoveNextAsync(
                bool isLengthCountedIn,
                CancellationToken cancellationToken)
        {
            UInt32 nextMessageLength = 0;
            if (readResult.HasValue
                && this.consumedBytesLength + sizeof(UInt32) <= readResult.Value.Buffer.Length)
            {
                // Is there space for at-leat length 
                ReadOnlySequence<byte> lengthBytesSequence = readResult.Value.Buffer.Slice(this.consumedBytesLength);
                nextMessageLength = LengthPrefixPipeReader.ToUInt32(lengthBytesSequence);
                if (!isLengthCountedIn)
                {
                    nextMessageLength += sizeof(UInt32);
                }

                long pendingBytesToRead = this.consumedBytesLength + nextMessageLength - readResult.Value.Buffer.Length;

                if (pendingBytesToRead <= 0)
                {
                    return ExtractMessage(nextMessageLength);
                }
            }

            this.readResult = null;
            this.consumedBytesLength = 0;

            (nextMessageLength, this.readResult) = await ReadLengthPrefixedMessageFullToConsume(
                                                                this.pipeReader, 
                                                                isLengthCountedIn,
                                                                cancellationToken);
            return ExtractMessage(nextMessageLength);
        }

        private (uint, byte[]) ExtractMessage(uint nextMessageLength)
        {
            ReadOnlySequence<byte> fullMessageSequence = this.readResult.Value.Buffer.Slice(this.consumedBytesLength, nextMessageLength);

            byte[] currentMessageBytes = ArrayPool<byte>.Shared.Rent((int)nextMessageLength);
            fullMessageSequence.CopyTo(currentMessageBytes);

            this.consumedBytesLength += nextMessageLength;

            // Advance reader for current message
            ReadOnlySequence<byte> readOnlyBytes = this.readResult.Value.Buffer;
            this.pipeReader.AdvanceTo(
                    readOnlyBytes.GetPosition(this.consumedBytesLength),
                    readOnlyBytes.End); 

            return (nextMessageLength, currentMessageBytes);
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

            if (length == 0 || readResult.Buffer.Length < length)
            {
                // TODO: clean-up (for POC its fine)
                throw new Exception($"{nameof(ReadLengthPrefixedMessageFullToConsume)} failed length: {length}, readResult.Buffer.Length: {readResult.Buffer.Length}");
            }

            return (length, readResult);
        }
    }
}
