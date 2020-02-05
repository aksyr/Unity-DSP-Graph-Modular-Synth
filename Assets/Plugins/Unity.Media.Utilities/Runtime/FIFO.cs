using Unity.Mathematics;

namespace Unity.Media.Utilities
{
    public struct FIFO
    {
        public struct Indices
        {
            public struct Range
            {
                public int Begin, End;
                public int Count => End - Begin;
            }

            const int StaticCount = 2;
            
            internal Range a, b;

            public Range this[int i] => i == 0 ? a : b;

            public int RangeCount => StaticCount;
            public int TotalCount => a.Count + b.Count;
        }
        
        int _Start, _End, _FuturedWriteLen;
        int _Size;
        bool _Full;
        
        public int FuturedWriteLength => _FuturedWriteLen;
        public int TotalLength => _Size;

        public int AvailableLength => _End >= _Start ? _Full ? _Size : (_End - _Start) : (_Size - (_Start - _End));
        public int FreeLength => _Size - AvailableLength;

        public int FuturedFreeLength => FreeLength - FuturedWriteLength;

        public FIFO(int size)
        {
            _Size = size;
            _Start = _End = _FuturedWriteLen = 0;
            _Full = false;
        }

        public Indices RequestWrite(int count, out int actual)
        {
            Indices.Range a, b;
            actual = RequestWrite(count, out a.Begin, out a.End, out b.Begin, out b.End);
            return new Indices { a = a, b = b };
        }

        int RequestWrite(int count, out int begin1, out int end1, out int begin2, out int end2)
        {
            var freeSpace = _End >= _Start ? _Full ? 0 : (_Size - (_End - _Start)) : (_Start - _End);
            count = math.min(count, freeSpace);

            if (count <= 0)
            {
                begin1 = 0;
                begin2 = 0;
                end1 = 0;
                end2 = 0;
            }
            else
            {
                begin1 = _End;
                begin2 = 0;
                var length = math.min(_Size - _End, count);
                end1 = begin1 + length;
                count -= length;
                end2 = count <= 0 ? 0 : math.min(count, _Start);
            }

            return (end1 - begin1) + (end2 - begin2);
        }

        public Indices RequestRead(int count, out int actual)
        {
            Indices.Range a, b;
            actual = RequestRead(count, out a.Begin, out a.End, out b.Begin, out b.End);
            return new Indices { a = a, b = b };
        }

        int RequestRead(int count, out int begin1, out int end1, out int begin2, out int end2)
        {
            var avail = _End >= _Start ? _Full ? _Size : (_End - _Start) : (_Size - (_Start - _End));
            count = math.min(count, avail);

            if (count <= 0)
            {
                begin1 = 0;
                begin2 = 0;
                end1 = 0;
                end2 = 0;
            }
            else
            {
                begin1 = _Start;
                begin2 = 0;
                var length = math.min(_Size - _Start, count);
                end1 = begin1 + length;
                count -= length;
                end2 = count <= 0 ? 0 : math.min(count, _End);
            }

            return (end1 - begin1) + (end2 - begin2);
        }

        public void CommitWrite(int count)
        {
            var newEnd = _End + count;

            if (newEnd > _Size)
                newEnd -= _Size;

            _End = newEnd;

            if (count > 0 && _End == _Size)
                _Full = true;

            _FuturedWriteLen -= count;
            if (_FuturedWriteLen < 0)
                _FuturedWriteLen = 0;
        }

        public void CommitRead(int count)
        {
            var newStart = _Start + count;

            if (newStart > _Size)
                newStart -= _Size;

            _Start = newStart;

            if (count > 0)
                _Full = false;
        }

        public int ReserveFuturedWrite(int length)
        {
            length = math.min(FuturedFreeLength, length);
            _FuturedWriteLen += length;
            return length;
        }
    }
}