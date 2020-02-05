using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Media.Utilities;

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
    public void WhenInsertingPastCapacity_DescriptionIsStillValid()
    {
        using (var growableBuffer = new GrowableBuffer<int>(Allocator.TempJob))
        {
            var description = growableBuffer.Description;
            var capacity = growableBuffer.Capacity;
            while (capacity == growableBuffer.Capacity)
                growableBuffer.Add(0);
            var rehydratedList = GrowableBuffer<int>.FromDescription(description);
            Assert.AreEqual(growableBuffer, rehydratedList);
            Assert.AreEqual(growableBuffer.Count, rehydratedList.Count);
            Assert.AreEqual(growableBuffer.Capacity, rehydratedList.Capacity);
            CollectionAssert.AreEqual(growableBuffer.ToArray(), rehydratedList.ToArray());
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new GrowableBuffer<float>(Allocator.TempJob, capacity));
        else
        {
            list = new GrowableBuffer<float>(Allocator.TempJob, capacity);
            Assert.AreEqual(capacity, list.Capacity);
            list.Dispose();
        }
    }

    [Test]
    public void DescriptionIsValidated()
    {
        Assert.Throws<ArgumentException>(() => GrowableBuffer<float>.FromDescription(default));
        // FIXME burst
//        using (var list = new GrowableBuffer<int>(Allocator.Temp))
//            Assert.Throws<ArgumentException>(() => GrowableBuffer<GrowableBufferDescription>.FromDescription(list.Description));
    }

    [Test]
    public void Iterate()
    {
        var buffer = new GrowableBuffer<int>(Allocator.Temp);

        for (int i = 0; i < 10; ++i)
            buffer.Add(i);

        foreach (int i in buffer)
            Assert.AreEqual(buffer[i], i);
    }
}
