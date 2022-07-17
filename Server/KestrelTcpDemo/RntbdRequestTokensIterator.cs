using System.Runtime.InteropServices;
using System;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
using System.Diagnostics;
using static Microsoft.Azure.Documents.RntbdConstants;

namespace KestrelTcpDemo
{
    internal ref struct RntbdRequestTokensIterator
    {
        static int ContextLength = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + BytesSerializer.GetSizeOfGuid();
        private readonly ReadOnlyMemory<byte> metadata;

        public RntbdRequestTokensIterator(
                byte[] metadata, 
                int startPoistion, 
                int totalLength)
        {
            this.metadata = new Memory<byte>(metadata, 0, metadata.Length);
            this.Position = startPoistion;
            this.TotalLength = totalLength;

            // Skip the meta-data context 
            this.Position += RntbdRequestTokensIterator.ContextLength;
        }

        public int Position { get; private set; }

        public int TotalLength { get; }

        public bool HasPayload()
        {
            bool? hasPayload = null;

            while (this.Position < this.TotalLength
                && !hasPayload.HasValue)
            {
                ushort identifier = this.ReadUInt16();
                RntbdTokenTypes tokenType = (RntbdTokenTypes)this.ReadByte();


                switch (tokenType)
                {
                    case RntbdTokenTypes.Byte:
                        if (identifier == (ushort)ResponseIdentifiers.PayloadPresent)
                        {
                            byte readByte = this.ReadByte();
                            hasPayload = (readByte != 0x00);
                        }
                        else
                        {
                            this.Position++;
                        }
                        break;
                    case RntbdTokenTypes.UShort:
                        this.Position += 2;
                        break;
                    case RntbdTokenTypes.ULong:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.Long:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.ULongLong:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.LongLong:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.Float:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.Double:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.Guid:
                        this.Position += 16;
                        break;
                    case RntbdTokenTypes.SmallBytes:
                    case RntbdTokenTypes.SmallString:
                        {
                            byte bytesLength = this.ReadByte();
                            this.Position += bytesLength;
                            break;
                        }
                    case RntbdTokenTypes.Bytes:
                    case RntbdTokenTypes.String:
                        {
                            ushort bytesLength = this.ReadUInt16();
                            this.Position += bytesLength;
                            break;
                        }
                    case RntbdTokenTypes.ULongBytes:
                    case RntbdTokenTypes.ULongString:
                        {
                            UInt32 bytesLength = this.ReadUInt32();
                            this.Position += (int)bytesLength;
                            // token.value.valueBytes = reader.ReadBytes((int)length);
                            break;
                        }
                    default:
                        throw new Exception($"Unsupport RntbdToken type: {tokenType}");
                }
            }

            Debug.Assert(hasPayload.HasValue);
            return hasPayload.Value;
        }

        public (bool hasPayload, string replicaPath, int replicaPathLengthPosition, int replicaPathUtf8Length) ExtractContext()
        {
            string replicaPath = null;
            bool? hasPayload = null;
            int? replicaPathUtf8Length = null;
            int? replicaPathLengthPosition = null;

            while (this.Position < this.TotalLength 
                && ! (hasPayload.HasValue && replicaPath != null))
            {
                ushort identifier = this.ReadUInt16();
                RntbdTokenTypes tokenType = (RntbdTokenTypes)this.ReadByte();


                switch (tokenType)
                {
                    case RntbdTokenTypes.Byte:
                        if (identifier == (ushort)RequestIdentifiers.PayloadPresent)
                        {
                            byte readByte = this.ReadByte();
                            hasPayload = (readByte != 0x00);
                        }
                        else
                        {
                            this.Position++;
                        }
                        break;
                    case RntbdTokenTypes.UShort:
                        this.Position += 2;
                        break;
                    case RntbdTokenTypes.ULong:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.Long:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.ULongLong:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.LongLong:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.Float:
                        this.Position += 4;
                        break;
                    case RntbdTokenTypes.Double:
                        this.Position += 8;
                        break;
                    case RntbdTokenTypes.Guid:
                        this.Position += 16;
                        break;
                    case RntbdTokenTypes.SmallBytes:
                    case RntbdTokenTypes.SmallString:
                        {
                            byte bytesLength = this.ReadByte();
                            this.Position += bytesLength;
                            break;
                        }
                    case RntbdTokenTypes.Bytes:
                    case RntbdTokenTypes.String:
                        {
                            if (identifier != (ushort)RequestIdentifiers.ReplicaPath)
                            {
                                ushort bytesLength = this.ReadUInt16();
                                this.Position += bytesLength;
                            }
                            else
                            {
                                replicaPathLengthPosition = this.Position;

                                ushort bytesLength = this.ReadUInt16();
                                ReadOnlyMemory<byte> replicaPathMemory = this.ReadBytes(bytesLength);
                                replicaPathUtf8Length = replicaPathMemory.Length;

                                replicaPath = BytesSerializer.GetStringFromBytes(replicaPathMemory);
                            }
                            break;
                        }
                    case RntbdTokenTypes.ULongBytes:
                    case RntbdTokenTypes.ULongString:
                        {
                            UInt32 bytesLength = this.ReadUInt32();
                            this.Position += (int)bytesLength;
                            // token.value.valueBytes = reader.ReadBytes((int)length);
                            break;
                        }
                    default:
                        throw new Exception($"Unsupport RntbdToken type: {tokenType}");
                }
            }

            Debug.Assert(hasPayload.HasValue);
            Debug.Assert(replicaPathUtf8Length.HasValue);
            Debug.Assert(replicaPathLengthPosition.HasValue);
            Debug.Assert(!String.IsNullOrWhiteSpace(replicaPath));

            return (hasPayload.Value, replicaPath, replicaPathLengthPosition.Value, replicaPathUtf8Length.Value);
        }

        public ushort ReadUInt16()
        {
            ushort value = MemoryMarshal.Read<ushort>(this.metadata.Span.Slice(this.Position));
            this.Position += 2;
            return value;
        }

        public byte ReadByte()
        {
            byte value = this.metadata.Span[this.Position];
            this.Position++;
            return value;
        }

        public uint ReadUInt32()
        {
            uint value = MemoryMarshal.Read<uint>(this.metadata.Span.Slice(this.Position));
            this.Position += 4;
            return value;
        }

        public int ReadInt32()
        {
            int value = MemoryMarshal.Read<int>(this.metadata.Span.Slice(this.Position));
            this.Position += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            ulong value = MemoryMarshal.Read<ulong>(this.metadata.Span.Slice(this.Position));
            this.Position += 8;
            return value;
        }

        public long ReadInt64()
        {
            long value = MemoryMarshal.Read<long>(this.metadata.Span.Slice(this.Position));
            this.Position += 8;
            return value;
        }

        public float ReadSingle()
        {
            float value = MemoryMarshal.Read<float>(this.metadata.Span.Slice(this.Position));
            this.Position += 4;
            return value;
        }

        public double ReadDouble()
        {
            double value = MemoryMarshal.Read<double>(this.metadata.Span.Slice(this.Position));
            this.Position += 8;
            return value;
        }

        public Guid ReadGuid()
        {
            Guid value = MemoryMarshal.Read<Guid>(this.metadata.Span.Slice(this.Position));
            this.Position += 16;
            return value;
        }

        public ReadOnlyMemory<byte> ReadBytes(int length)
        {
            ReadOnlyMemory<byte> value = this.metadata.Slice(this.Position, length);
            this.Position += length;
            return value;
        }
    }
}
