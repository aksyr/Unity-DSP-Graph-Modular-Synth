using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Media.Utilities.Tests.Runtime
{
    public class GrowableBuffers
    {
        [Test]
        [TestCase(0)]
        [TestCase(5)]
        [TestCase(8)]
        [TestCase(9)]
        public void Insert(int insertionIndex)
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                var list = new List<int>();
                for (int i = 0; i < 9; ++i)
                {
                    growableBuffer.Add(i);
                    list.Add(i);
                }

                growableBuffer.Insert(insertionIndex, -1);
                list.Insert(insertionIndex, -1);
                CollectionAssert.AreEqual(list.ToArray(), growableBuffer.ToArray());
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(5)]
        [TestCase(8)]
        public void Remove(int removalIndex)
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                var list = new List<int>();
                for (int i = 0; i < 9; ++i)
                {
                    growableBuffer.Add(i);
                    list.Add(i);
                }

                growableBuffer.RemoveAt(removalIndex);
                list.RemoveAt(removalIndex);
                CollectionAssert.AreEqual(list.ToArray(), growableBuffer.ToArray());
            }
        }

        [Test]
        public void WhenInsertingPastCapacity_ListExpands()
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                var capacity = growableBuffer.Capacity;
                while (capacity == growableBuffer.Capacity)
                    growableBuffer.Add(0);
            }
        }

        [Test]
        public void CopyFromGrowableBuffer()
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                for (int i = 0; i < 9; ++i)
                    growableBuffer.Add(i);
                using (var copiedList = new GrowableBuffer<int>(Allocator.Temp, growableBuffer))
                {
                    // CollectionAssert is broken with GrowableBuffer<T> even though it only wants IEnumerable<T>...
                    CollectionAssert.AreEqual(growableBuffer.ToArray(), copiedList.ToArray());
                    Assert.AreEqual(growableBuffer.Count, copiedList.Capacity);
                }
            }
        }

        [Test]
        public void CopyFromArray()
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                for (int i = 0; i < 9; ++i)
                    growableBuffer.Add(i);
                using (var copiedList = new GrowableBuffer<int>(Allocator.Temp, growableBuffer.ToArray()))
                {
                    CollectionAssert.AreEqual(growableBuffer.ToArray(), copiedList.ToArray());
                    Assert.AreEqual(growableBuffer.Count, copiedList.Capacity);
                }
            }
        }

        [Test]
        public void CopyFromList()
        {
            var list = new List<int>();
            for (int i = 0; i < 9; ++i)
                list.Add(i);
            using (var copiedList = new GrowableBuffer<int>(Allocator.Temp, list))
            {
                CollectionAssert.AreEqual(list.ToArray(), copiedList.ToArray());
                Assert.AreEqual(list.Count, copiedList.Capacity);
            }
        }

        [Test]
        public void AddRange()
        {
            using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
            {
                for (int i = 0; i < 9; ++i)
                    growableBuffer.Add(i);
                using (var copiedList = new GrowableBuffer<int>(Allocator.Temp))
                {
                    copiedList.AddRange(growableBuffer);
                    CollectionAssert.AreEqual(growableBuffer.ToArray(), copiedList.ToArray());
                }
            }
        }

        [Test]
        [TestCase(-1, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(100, false)]
        public void CapacityIsValidated(int capacity, bool shouldThrow)
        {
            GrowableBuffer<float> list;
            if (shouldThrow)
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new GrowableBuffer<float>(Allocator.TempJob, capacity));
            else
            {
                list = new GrowableBuffer<float>(Allocator.TempJob, capacity);
                Assert.AreEqual(capacity, list.Capacity);
                list.Dispose();
            }
        }

        [Test]
        public void Iterate()
        {
            using (var buffer = new GrowableBuffer<int>(Allocator.Temp))
            {
                for (int i = 0; i < 10; ++i)
                    buffer.Add(i);

                foreach (int i in buffer)
                    Assert.AreEqual(buffer[i], i);
            }
        }

        [Test]
        public void CannotIndex_BeyondSize()
        {
            using (var buffer = new GrowableBuffer<int>(Allocator.Temp))
            {
                int dummy;
                Assert.Throws<IndexOutOfRangeException>(() => dummy = buffer[0]);
                Assert.Throws<IndexOutOfRangeException>(() => dummy = buffer[-1]);
                Assert.Throws<IndexOutOfRangeException>(() => dummy = buffer[1]);

                for (int i = 0; i < 10; ++i)
                {
                    Assert.Throws<IndexOutOfRangeException>(() => dummy = buffer[i]);

                    buffer.Add(i);
                    dummy = buffer[i];
                }
            }
        }

        [Test]
        public void CannotInsertAt_BeyondSize()
        {
            using (var buffer = new GrowableBuffer<int>(Allocator.Temp))
            {
                Assert.Throws<IndexOutOfRangeException>(() => buffer.Insert(1, 0));
                Assert.Throws<IndexOutOfRangeException>(() => buffer.Insert(-1, 0));

                for (int i = 1; i < 10; ++i)
                {
                    Assert.Throws<IndexOutOfRangeException>(() => buffer.Insert(i, 0));

                    buffer.Add(i);
                }
            }
        }

        [Test]
        public void CannotRemoveAt_BeyondSize()
        {
            using (var buffer = new GrowableBuffer<int>(Allocator.Temp))
            {
                Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(0));
                Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(1));
                Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(-1));

                for (int i = 0; i < 10; ++i)
                {
                    Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(i));

                    buffer.Add(i);
                }
            }
        }
    }
}
