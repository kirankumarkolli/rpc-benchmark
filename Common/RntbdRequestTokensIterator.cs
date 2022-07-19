using System.Runtime.InteropServices;
using System;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using System.Diagnostics;
using static Microsoft.Azure.Documents.RntbdConstants;
using System.Buffers;

namespace KestrelTcpDemo
{
    internal ref struct RntbdRequestTokensIterator
    {
        static int ContextLength = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + BytesSerializer.GetSizeOfGuid();
        private ReadOnlySequence<byte> metadata;

        public RntbdRequestTokensIterator(
                ReadOnlySequence<byte> metadata)
        {
            this.metadata = metadata;
        }

        public (bool hasPayload, SequencePosition replicaPathLengthPosition, ushort replicaPathUtf8Length) ExtractContext(byte[] tmpBytes)
        {
            bool? hasPayload = null;
            ushort? replicaPathUtf8Length = null;
            SequencePosition? replicaPathLengthPosition = null;

            this.metadata = this.metadata.Slice(RntbdRequestTokensIterator.ContextLength); // Start after the context

            while (!this.metadata.IsEmpty
                && !(hasPayload.HasValue && replicaPathUtf8Length.HasValue))
            {
                ushort identifier = this.ReadUInt16();
                RntbdTokenTypes tokenType = (RntbdTokenTypes)this.ReadByte();

                switch (tokenType)
                {
                    case RntbdTokenTypes.Byte:
                        byte readByte = this.ReadByte();
                        if (identifier == (ushort)RequestIdentifiers.PayloadPresent)
                        {
                            hasPayload = (readByte != 0x00);
                        }
                        break;
                    case RntbdTokenTypes.UShort:
                        this.metadata = this.metadata.Slice(2);
                        break;
                    case RntbdTokenTypes.ULong:
                    case RntbdTokenTypes.Long:
                    case RntbdTokenTypes.Float:
                        this.metadata = this.metadata.Slice(4);
                        break;
                    case RntbdTokenTypes.ULongLong:
                    case RntbdTokenTypes.LongLong:
                    case RntbdTokenTypes.Double:
                        this.metadata = this.metadata.Slice(8);
                        break;
                    case RntbdTokenTypes.Guid:
                        this.metadata = this.metadata.Slice(16);
                        break;
                    case RntbdTokenTypes.SmallBytes:
                    case RntbdTokenTypes.SmallString:
                        {
                            byte bytesLength = this.ReadByte();
                            this.metadata = this.metadata.Slice(bytesLength);
                            break;
                        }
                    case RntbdTokenTypes.Bytes:
                    case RntbdTokenTypes.String:
                        {
                            if (identifier != (ushort)RequestIdentifiers.ReplicaPath)
                            {
                                ushort bytesLength = this.ReadUInt16();
                                this.metadata = this.metadata.Slice(bytesLength);
                            }
                            else
                            {
                                replicaPathLengthPosition = this.metadata.Start;
                                replicaPathUtf8Length = this.ReadShortBytes(tmpBytes.AsSpan());
                            }
                            break;
                        }
                    case RntbdTokenTypes.ULongBytes:
                    case RntbdTokenTypes.ULongString:
                        {
                            int bytesLength = (int)this.ReadUInt32();
                            this.metadata = this.metadata.Slice(bytesLength);
                            break;
                        }
                    default:
                        throw new Exception($"Unsupport RntbdToken type: {tokenType}");
                }
            }

            Debug.Assert(hasPayload.HasValue);
            Debug.Assert(replicaPathUtf8Length.HasValue);
            Debug.Assert(replicaPathLengthPosition.HasValue);

            return (hasPayload.Value, replicaPathLengthPosition.Value, replicaPathUtf8Length.Value);
        }

        public bool ResponeHasPayload()
        {
            bool? hasPayload = null;

            this.metadata = this.metadata.Slice(RntbdRequestTokensIterator.ContextLength); // Start after the context

            while (!this.metadata.IsEmpty
                        && !hasPayload.HasValue)
            {
                ushort identifier = this.ReadUInt16();
                RntbdTokenTypes tokenType = (RntbdTokenTypes)this.ReadByte();

                switch (tokenType)
                {
                    case RntbdTokenTypes.Byte:
                        byte readByte = this.ReadByte();
                        if (identifier == (ushort)ResponseIdentifiers.PayloadPresent)
                        {
                            hasPayload = (readByte != 0x00);
                        }
                        break;
                    case RntbdTokenTypes.UShort:
                        this.metadata = this.metadata.Slice(2);
                        break;
                    case RntbdTokenTypes.ULong:
                    case RntbdTokenTypes.Long:
                    case RntbdTokenTypes.Float:
                        this.metadata = this.metadata.Slice(4);
                        break;
                    case RntbdTokenTypes.ULongLong:
                    case RntbdTokenTypes.LongLong:
                    case RntbdTokenTypes.Double:
                        this.metadata = this.metadata.Slice(8);
                        break;
                    case RntbdTokenTypes.Guid:
                        this.metadata = this.metadata.Slice(16);
                        break;
                    case RntbdTokenTypes.SmallBytes:
                    case RntbdTokenTypes.SmallString:
                        {
                            byte bytesLength = this.ReadByte();
                            this.metadata = this.metadata.Slice(bytesLength);
                            break;
                        }
                    case RntbdTokenTypes.Bytes:
                    case RntbdTokenTypes.String:
                        {
                            ushort bytesLength = this.ReadUInt16();
                            this.metadata = this.metadata.Slice(bytesLength);
                            break;
                        }
                    case RntbdTokenTypes.ULongBytes:
                    case RntbdTokenTypes.ULongString:
                        {
                            int bytesLength = (int)this.ReadUInt32();
                            this.metadata = this.metadata.Slice(bytesLength);
                            break;
                        }
                    default:
                        throw new Exception($"Unsupport RntbdToken type: {tokenType}");
                }
            }

            Debug.Assert(hasPayload.HasValue);
            return hasPayload.Value;
        }

        public ushort ReadUInt16()
        {
            // Read next two bytes 
            uint value = RntbdRequestTokensIterator.ToUInt16(this.metadata);
            this.metadata = this.metadata.Slice(2);
            return (ushort)value;
        }

        public byte ReadByte()
        {
            byte value = this.metadata.FirstSpan[0];
            this.metadata = this.metadata.Slice(1);
            return value;
        }

        public uint ReadUInt32()
        {
            uint value = RntbdRequestTokensIterator.ToUInt32(this.metadata);
            this.metadata = this.metadata.Slice(4);
            return value;
        }

        public int ReadInt32()
        {
            return (int)this.ReadUInt32(); // TODO: Fix it 
        }

        public ushort ReadShortBytes(Span<byte> target)
        {
            ushort bytesLength = this.ReadUInt16();
            this.metadata.Slice(0, bytesLength).CopyTo(target);
            this.metadata = this.metadata.Slice(bytesLength);

            return bytesLength;
        }

        public void SkipLongBytes()
        {
            UInt32 bytesLength = this.ReadUInt32();
            this.metadata = this.metadata.Slice(bytesLength);
        }

        /// <summary>
        /// TODO: BELOW CODE DUPLICATION: Fined better alternatives 
        /// </summary>
        public static UInt32 ToUInt32(ReadOnlySequence<byte> lengthBytesSequence)
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

        public static UInt16 ToUInt16(ReadOnlySequence<byte> lengthBytesSequence)
        {
            if (lengthBytesSequence.Length < sizeof(UInt16))
            {
                throw new ArgumentOutOfRangeException($"{nameof(lengthBytesSequence)} had less than {sizeof(UInt16)}");
            }

            if (lengthBytesSequence.IsSingleSegment)
            {
                ReadOnlySpan<byte> lengthBytesSpan = lengthBytesSequence.FirstSpan;

                return BitConverter.ToUInt16(lengthBytesSpan);
            }
            else
            {
                Span<byte> stackCopyBytes = stackalloc byte[sizeof(uint)];
                lengthBytesSequence.Slice(0, sizeof(uint)).CopyTo(stackCopyBytes);

                return BitConverter.ToUInt16(stackCopyBytes);
            }
        }
    }
}
