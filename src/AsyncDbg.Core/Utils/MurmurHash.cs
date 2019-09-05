using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Codex.Utilities
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Murmur3
    {
        // 128 bit output, 64 bit platform version

        public const int READ_SIZE = 16;
        private static ulong C1 = 0x87c37b91114253d5L;
        private static ulong C2 = 0x4cf5ad432745937fL;

        private ulong processedCount;
        private uint seed; // if want to start with a seed, create a constructor
        ulong high;
        ulong low;

        private int stateOffset;
        private State state;

        public MurmurHash ComputeHash(byte[] bb, int start = 0, int length = -1)
        {
            Reset();
            ProcessBytes(bb, start, length < 0 ? bb.Length : length);
            ProcessFinal();
            return Hash;
        }

        public MurmurHash ComputeHash(IEnumerable<ArraySegment<byte>> byteSegments)
        {
            Reset();
            foreach (var byteSegment in byteSegments)
            {
                ProcessBytes(byteSegment.Array, byteSegment.Offset, byteSegment.Count);
            }

            ProcessFinal();
            return Hash;
        }

        private void ProcessBytes(byte[] bb, int start, int length)
        {
            int pos = start;
            ulong remaining = (ulong)length;

            int read = state.ConsumeInitial(ref this, bb, start, length);
            pos += read;
            remaining -= (ulong)read;

            // read 128 bits, 16 bytes, 2 longs in each cycle
            while (remaining >= READ_SIZE)
            {
                ulong k1 = bb.GetUInt64(pos);
                pos += 8;

                ulong k2 = bb.GetUInt64(pos);
                pos += 8;

                remaining -= READ_SIZE;

                MixBody(k1, k2);
            }

            if (remaining >= 0)
            {
                read = state.ConsumeRemaining(ref this, bb, pos, (int)remaining);
            }

            processedCount += (ulong)length;
        }

        private void Reset()
        {
            high = seed;
            low = 0;
            this.processedCount = 0L;
        }

        private void ProcessFinal()
        {
            if (stateOffset > 0)
            {
                MixRemaining(state.K1, state.K2);
            }

            high ^= processedCount;
            low ^= processedCount;

            high += low;
            low += high;

            high = MixFinal(high);
            low = MixFinal(low);

            high += low;
            low += high;
        }

        private void ProcessBytesRemaining(byte[] bb, ulong remaining, int pos)
        {
            ulong k1 = 0;
            ulong k2 = 0;

            // little endian (x86) processing
            switch (remaining)
            {
                case 15:
                    k2 ^= (ulong)bb[pos + 14] << 48; // fall through
                    goto case 14;
                case 14:
                    k2 ^= (ulong)bb[pos + 13] << 40; // fall through
                    goto case 13;
                case 13:
                    k2 ^= (ulong)bb[pos + 12] << 32; // fall through
                    goto case 12;
                case 12:
                    k2 ^= (ulong)bb[pos + 11] << 24; // fall through
                    goto case 11;
                case 11:
                    k2 ^= (ulong)bb[pos + 10] << 16; // fall through
                    goto case 10;
                case 10:
                    k2 ^= (ulong)bb[pos + 9] << 8; // fall through
                    goto case 9;
                case 9:
                    k2 ^= (ulong)bb[pos + 8]; // fall through
                    goto case 8;
                case 8:
                    k1 ^= bb.GetUInt64(pos);
                    break;
                case 7:
                    k1 ^= (ulong)bb[pos + 6] << 48; // fall through
                    goto case 6;
                case 6:
                    k1 ^= (ulong)bb[pos + 5] << 40; // fall through
                    goto case 5;
                case 5:
                    k1 ^= (ulong)bb[pos + 4] << 32; // fall through
                    goto case 4;
                case 4:
                    k1 ^= (ulong)bb[pos + 3] << 24; // fall through
                    goto case 3;
                case 3:
                    k1 ^= (ulong)bb[pos + 2] << 16; // fall through
                    goto case 2;
                case 2:
                    k1 ^= (ulong)bb[pos + 1] << 8; // fall through
                    goto case 1;
                case 1:
                    k1 ^= (ulong)bb[pos]; // fall through
                    break;
                default:
                    throw new Exception("Something went wrong with remaining bytes calculation.");
            }

            MixRemaining(k1, k2);
        }

        private void MixRemaining(ulong k1, ulong k2)
        {
            high ^= MixKey1(k1);
            low ^= MixKey2(k2);
        }

        #region Mix Methods

        private void MixBody(ulong k1, ulong k2)
        {
            high ^= MixKey1(k1);

            high = high.RotateLeft(27);
            high += low;
            high = high * 5 + 0x52dce729;

            low ^= MixKey2(k2);

            low = low.RotateLeft(31);
            low += high;
            low = low * 5 + 0x38495ab5;
        }

        private static ulong MixKey1(ulong k1)
        {
            k1 *= C1;
            k1 = k1.RotateLeft(31);
            k1 *= C2;
            return k1;
        }

        private static ulong MixKey2(ulong k2)
        {
            k2 *= C2;
            k2 = k2.RotateLeft(33);
            k2 *= C1;
            return k2;
        }

        private static ulong MixFinal(ulong k)
        {
            // avalanche bits

            k ^= k >> 33;
            k *= 0xff51afd7ed558ccdL;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53L;
            k ^= k >> 33;
            return k;
        }

        #endregion

        public MurmurHash Hash
        {
            get
            {
                return new MurmurHash() { High = high, Low = low };
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private unsafe struct State
        {
            public const int BYTE_LENGTH = 16;

            [FieldOffset(0)]
            public ulong K1;

            [FieldOffset(8)]
            public ulong K2;

            [FieldOffset(0)]
            private fixed byte bytes[BYTE_LENGTH];

            public byte this[int i]
            {
                get
                {
                    fixed (byte* bytes = this.bytes)
                    {
                        return bytes[i];
                    }
                }

                set
                {
                    fixed (byte* bytes = this.bytes)
                    {
                        bytes[i] = value;
                    }
                }
            }

            public int ConsumeInitial(ref Murmur3 murmur, byte[] bb, int offset, int length)
            {
                if (murmur.stateOffset != 0)
                {
                    return ConsumeRemaining(ref murmur, bb, offset, length);
                }

                return 0;
            }

            public int ConsumeRemaining(ref Murmur3 murmur, byte[] bb, int offset, int length)
            {
                fixed (byte* bytes = this.bytes)
                {
                    int i = 0;
                    for (i = 0; i < length && murmur.stateOffset < BYTE_LENGTH; i++, murmur.stateOffset++)
                    {
                        bytes[murmur.stateOffset] = bb[i + offset];
                    }

                    if (murmur.stateOffset == READ_SIZE)
                    {
                        murmur.MixBody(K1, K2);
                        murmur.stateOffset = 0;
                        K1 = 0;
                        K2 = 0;
                    }

                    return i;
                }
            }
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct MurmurHash
    {
        public const int BYTE_LENGTH = 16;

        private static readonly char[] s_paddingChars = new[] { '=' };

        [FieldOffset(0)]
        public ulong High;

        [FieldOffset(8)]
        public ulong Low;

        [FieldOffset(0)]
        private uint int_0;

        [FieldOffset(4)]
        private uint int_1;

        [FieldOffset(8)]
        private uint int_2;

        [FieldOffset(12)]
        private uint int_3;

        [FieldOffset(0)]
        private Guid guid;

        [FieldOffset(0)]
        private fixed byte bytes[16];

        public MurmurHash(uint int0, uint int1, uint int2, uint int3)
            : this()
        {
            int_0 = int0;
            int_1 = int1;
            int_2 = int2;
            int_3 = int3;
        }

        public Guid AsGuid()
        {
            return guid;
        }

        public override string ToString()
        {
            return guid.ToString();
        }

        public string ToBase64String()
        {
            return ToBase64String(maxCharLength: null, format: Base64.Format.UrlSafe);
        }

        internal unsafe string ToBase64String(int? maxCharLength = null, Base64.Format format = Base64.Format.UrlSafe)
        {
            fixed (byte* b = bytes)
            {
                return Base64.ToBase64String(b, 0, BYTE_LENGTH, maxCharLength, format);
            }
        }

        public uint GetInt(int i)
        {
            if (unchecked((uint)i >= (uint)4))
            {
                throw new ArgumentOutOfRangeException();
            }

            fixed (byte* bytes = this.bytes)
            {
                return ((uint*)bytes)[i];
            }
        }

        public byte GetByte(int i)
        {
            if (unchecked((uint)i >= (uint)BYTE_LENGTH))
            {
                throw new ArgumentOutOfRangeException();
            }

            fixed (byte* bytes = this.bytes)
            {
                return bytes[i];
            }
        }

        public static MurmurHash operator ^(MurmurHash hash1, MurmurHash hash2)
        {
            return new MurmurHash()
            {
                High = hash1.High ^ hash2.High,
                Low = hash1.Low ^ hash2.Low
            };
        }
    }
}
