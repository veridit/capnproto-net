﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CapnProto
{
    abstract public partial class CapnProtoReader : IDisposable
    {
        protected const int ScratchLengthBytes = 64, ScratchLengthWords = ScratchLengthBytes / 8; // must be at least 8
        protected readonly byte[] Scratch = new byte[ScratchLengthBytes];
        public static CapnProtoReader Create(Stream source, object context = null, bool leaveOpen = false)
        {
            return new CapnProtoStreamReader(source, context, leaveOpen);
        }

        protected CapnProtoReader(object context)
        {
            this.context = context;

        }

        private static readonly bool isLittleEndian = BitConverter.IsLittleEndian;

        public virtual void Dispose() { context = null; }

        private object context;
        public object Context { get { return context; } }

        //private void Read(byte[] buffer, int offset, int count)
        //{
        //    int read;
        //    while(count > 0 && (read = ReadRaw(buffer, offset, count)) > 0)
        //    {
        //        count -= read;
        //        offset += read;
        //    }
        //    if (count != 0) throw new EndOfStreamException();
        //}



        public abstract void ReadWords(int segment, int wordOffset, byte[] buffer, int bufferOffset, int wordCount);
        public virtual ulong ReadWord(int segment, int wordOffset)
        {
            var tmp = Scratch;
            ReadWords(segment, wordOffset, tmp, 0, 1);
            return BitConverter.ToUInt64(tmp, 0);
        }

        public int ParseListHeader(ulong pointer, ref int origin, out ElementSize size)
        {
            //lsb                       list pointer                        msb
            //+-+-----------------------------+--+----------------------------+
            //|A|             B               |C |             D              |
            //+-+-----------------------------+--+----------------------------+

            uint first = unchecked((uint)pointer), second = unchecked((uint)(pointer >> 32));

            // A (2 bits) = 1, to indicate that this is a list pointer.
            if ((first & 3) != 1) throw new InvalidOperationException("List header expected");
            // B (30 bits) = Offset, in words, from the end of the pointer to the start of the first element of the list.  Signed.
            origin += (int)(first >> 2);
            // C (3 bits) = Size of each element:
            size = (ElementSize)(int)(second & 7);
            // D (29 bits) = Number of elements in the list, except when C is 7
            return (int)(second >> 3);
        }
        private int AssertListHeader(ulong pointer, ref int origin, ElementSize expectedSize)
        {
            ElementSize actualSize;
            int count = ParseListHeader(pointer, ref origin, out actualSize);
            if (expectedSize != actualSize) throw new InvalidOperationException("Invalid list pointer size; expected " + expectedSize.ToString() + ", found " + actualSize.ToString());
            return count;
        }

        public virtual unsafe List<string> ReadStringList(int segment, int origin, ulong pointer)
        {
            int count = AssertListHeader(pointer, ref origin, ElementSize.EightBytesPointer);
            ulong* raw = stackalloc ulong[count];
            ReadWords(segment, origin, count, count, raw);
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadStringFromPointer(segment, origin + i, raw[i]));
            }
            return list;
        }

        public virtual unsafe List<T> ReadStructList<T>(DeserializationContext context, int segment, int origin, ulong pointer) where T : IBlittable
        {
            int count = AssertListHeader(pointer, ref origin, ElementSize.EightBytesPointer);
            ulong* raw = stackalloc ulong[count];
            ReadWords(segment, origin, count, count, raw);
            var list = new List<T>(count);
            var model = context.Model;
            for (int i = 0; i < count; i++)
            {
                list.Add(model.Deserialize<T>(segment, origin + i, context, raw[i]));
            }
            return list;
        }
        public virtual unsafe List<long> ReadInt64List(int segment, int origin, ulong pointer)
        {
            int count = AssertListHeader(pointer, ref origin, ElementSize.EightBytesNonPointer);
            ulong* raw = stackalloc ulong[count];
            ReadWords(segment, origin, count, count, raw);
            var list = new List<long>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(unchecked((long)raw[i]));
            }
            return list;
        }

        protected abstract void OnChangeSegment(int segment, int offset, int length);

        private int segment;
        public int Segment { get { return segment; } }
        public void ChangeSegment(int segment)
        {
            if (segment != this.segment)
            {
                int offset, length;
                if (segment == 0)
                {
                    offset = firstSegment.Offset;
                    length = firstSegment.Length;
                }
                else
                {

                    if (segment < 0 || segment-- > otherSegments.Length)
                        throw new ArgumentOutOfRangeException("segment");

                    offset = otherSegments[segment].Offset;
                    length = otherSegments[segment].Length;
                }
                OnChangeSegment(segment, offset, length);
                this.segment = segment;
            }
        }
        public byte[] ReadBytesFromPointer(int segment, int wordOffset, ulong ptr)
        {
            var lptr = new ListPointer(ptr);
            if (lptr.ElementSize != ElementSize.OneByte) throw new InvalidOperationException("Expected pointer to single-byte data");

            int len = lptr.Size;
            if (len == 0) return NixBytes;
            return ReadBytes(segment, wordOffset + lptr.Offset, len);
        }
        static readonly byte[] NixBytes = new byte[0];
        public string ReadStringFromPointer(int segment, int wordOffset, ulong ptr)
        {
            var lptr = new ListPointer(ptr);
            if (lptr.ElementSize != ElementSize.OneByte) throw new InvalidOperationException("Expected pointer to single-byte data");

            int len = lptr.Size;
            if (len == 0) throw ExpectedNullTerminatedString();

            if (len <= ScratchLengthBytes)
            {
                var tmp = Scratch;
                ReadWords(segment, wordOffset + lptr.Offset, tmp, 0, ToWords(len));
                if (tmp[len - 1] != 0) throw ExpectedNullTerminatedString();

                return Encoding.UTF8.GetString(tmp, 0, len - 1);

            }
            return ReadString(segment, wordOffset + lptr.Offset, len);
        }
        private static int ToWords(int bytes)
        {
            return (bytes + 7) / 8;
        }
        protected Exception ExpectedNullTerminatedString()
        {
            return new InvalidOperationException("Expected nul-terminated string");
        }

        protected abstract string ReadString(int segment, int wordOffset, int count);
        protected abstract byte[] ReadBytes(int segment, int wordOffset, int count);

        private SegmentRange firstSegment;
        private SegmentRange[] otherSegments;
        public virtual void ReadPreamble()
        {
            int offset = 0;
            var word = ReadWord(0, offset++);
            int additionalSegments = (int)word;

            int segmentOffset = 1 + (additionalSegments / 2);
            if ((additionalSegments % 2) != 0) segmentOffset++;
            firstSegment = new SegmentRange(ref segmentOffset, (int)(word >> 32));
            if (additionalSegments != 0)
            {
                otherSegments = new SegmentRange[additionalSegments];
                int index = 0;
                for (int i = 0; i < additionalSegments / 2; i++)
                {
                    word = ReadWord(0, offset++);
                    otherSegments[index++] = new SegmentRange(ref segmentOffset, (int)word);
                    otherSegments[index++] = new SegmentRange(ref segmentOffset, (int)(word >> 32));
                }

                if ((additionalSegments % 2) != 0)
                {
                    word = ReadWord(0, offset++);
                    otherSegments[index] = new SegmentRange(ref segmentOffset, (int)word);
                }
            }
            OnChangeSegment(0, firstSegment.Offset, firstSegment.Length);
        }
        private struct SegmentRange
        {
            public readonly int Offset, Length;
            public SegmentRange(ref int offset, int length)
            {
                this.Offset = offset;
                this.Length = length;
                offset += length;
            }
        }


        public unsafe void ReadData(int segment, int origin, ulong pointer, ulong* raw, int max)
        {
            if ((pointer & 3) != 0) throw new InvalidOperationException("Expected struct pointer");
            var offset = unchecked(((int)pointer) >> 2); // note: int because signed
            var count = (int)((pointer >> 32) & 0xFFFF);

            ReadWords(segment, origin + offset, count, max, raw);
        }

        public unsafe int ReadPointers(int segment, int origin, ulong pointer, ulong* raw, int max)
        {
            if ((pointer & 3) != 0) throw new InvalidOperationException("Expected struct pointer");
            var offset = unchecked(((int)pointer) >> 2); // note: int because signed
            var dataCount = (int)((pointer >> 32) & 0xFFFF);
            var count = (int)((pointer >> 48) & 0xFFFF);

            int pointerBase = origin + offset + dataCount;
            ReadWords(segment, pointerBase, count, max, raw);
            return pointerBase;
        }

        protected virtual unsafe void ReadWords(int segment, int origin, int available, int expected, ulong* raw)
        {
            int wordsToRead = Math.Max(available, expected);

            var buffer = Scratch;
            int offset = 0;
            while (wordsToRead >= ScratchLengthWords)
            {
                ReadWords(segment, origin + offset, buffer, 0, ScratchLengthWords);
                Marshal.Copy(buffer, 0, new IntPtr(raw + offset), ScratchLengthBytes);
                offset += ScratchLengthWords;
                wordsToRead -= ScratchLengthWords;
            }
            if (wordsToRead > 0)
            {
                ReadWords(segment, origin + offset, buffer, 0, wordsToRead);
                Marshal.Copy(buffer, 0, new IntPtr(raw + offset), wordsToRead * 8);
            }
            // zero out any words that didn't receive data
            for (int i = available; i < expected; i++)
                raw[i] = 0;
        }
    }
}