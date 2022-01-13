using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Random = UnityEngine.Random;

namespace Unity.Media.Utilities.Tests.Runtime
{
    public unsafe class PagedFreeLists
    {
        private const int kPageCapacityForConcurrencyTests = 16;
        private const int kItemCountForConcurrencyTests = kPageCapacityForConcurrencyTests * 500;

        [Test]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(100)]
        [TestCase(1024)]
        public void PageIsSetup(int pageSize)
        {
            using (var list = new PagedFreeList<int>(pageSize))
            {
                Assert.IsTrue(list.Valid);

                Assert.AreEqual(pageSize, list.PageCapacity);
                Assert.AreEqual(pageSize, list.Capacity);
                Assert.IsFalse(list.Root == null);
                Assert.IsFalse(list.Root->Elements == null);
                Assert.IsFalse(list.Root->Used == null);

                Assert.AreEqual(0, list.Root->Next);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(-5)]
        public void ThrowsOnPageSizeError(int pageSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { new PagedFreeList<int>(pageSize); });
        }

        [Test]
        public void ThrowsOnWrongIndex()
        {
            using (var list = new PagedFreeList<int>(100))
            {
                int dummy = 0;
                Assert.IsFalse(list.IndexIsValid(-1));
                Assert.Throws<IndexOutOfRangeException>(() => { dummy = list[-1]; });

                Assert.IsFalse(list.IndexIsValid(5));
                Assert.Throws<IndexOutOfRangeException>(() => { dummy = list[5]; });

                list.AllocateIndex();

                Assert.IsFalse(list.IndexIsValid(5));
                Assert.Throws<IndexOutOfRangeException>(() => { dummy = list[5]; });

                Assert.IsFalse(list.IndexIsValid(6000));
                Assert.Throws<IndexOutOfRangeException>(() => { dummy = list[6000]; });

                Assert.Throws<IndexOutOfRangeException>(() => { list[5] = dummy; });

                list.AllocateIndex();

                Assert.Throws<IndexOutOfRangeException>(() => { list[5] = dummy; });
                Assert.Throws<IndexOutOfRangeException>(() => { list[6000] = dummy; });
            }
        }

        [Test]
        public void DeallocatedThrows()
        {
            using (var list = new PagedFreeList<int>(100))
            {
                var index = list.AllocateIndex();
                var dummy = index;

                Assert.IsTrue(list.IndexIsValid(index));
                Assert.DoesNotThrow(() =>
                {
                    dummy = list[index];
                    list[index] = dummy;
                });

                list.FreeIndex(index);

                Assert.IsFalse(list.IndexIsValid(index));
                Assert.Throws<IndexOutOfRangeException>(() => { dummy = list[index]; });

                Assert.Throws<IndexOutOfRangeException>(() => { list[index] = dummy; });
            }
        }

        [Test]
        public void SpillAllocation()
        {
            using (var list = new PagedFreeList<int>(1))
            {
                list.AllocateIndex();
                var lastPage = list.Root;
                Assert.AreEqual(0, lastPage->Next);
                Assert.AreEqual(1, list.Capacity);

                list.AllocateIndex();
                Assert.AreNotEqual(0, lastPage->Next);
                lastPage = (FreeListPage*)lastPage->Next;
                Assert.AreEqual(0, lastPage->Next);
                Assert.AreEqual(2, list.Capacity);

                list.AllocateIndex();
                Assert.AreNotEqual(0, lastPage->Next);
                lastPage = (FreeListPage*)lastPage->Next;
                Assert.AreEqual(0, lastPage->Next);
                Assert.AreEqual(3, list.Capacity);
            }
        }

        [Test]
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(50)]
        public void AllocateFreeAllocateGivesSameIndexAndRef(int pageSize)
        {
            using (var list = new PagedFreeList<int>(pageSize))
            {
                list.AllocateIndex();
                list.AllocateIndex();
                list.AllocateIndex();
                list.AllocateIndex();
                list.AllocateIndex();
                list.AllocateIndex();

                var index = list.AllocateIndex();
                void* ptr;
                list[index] = index;
                fixed(int* p = &list[index])
                {
                    ptr = p;
                    Assert.AreEqual(index, *p);
                }

                list.FreeIndex(index);
                var newIndex = list.AllocateIndex();
                void* newPtr;
                list[index] = index;
                fixed(int* p = &list[newIndex])
                {
                    newPtr = p;
                    Assert.AreEqual(index, *p);
                }

                Assert.AreEqual(index, newIndex);
                Assert.IsTrue(ptr == newPtr);
            }
        }

        [Test]
        [TestCase(5, 3, 100, 100)]
        [TestCase(10, 1, 100, 5)]
        [TestCase(500, 340, 50, 1024)]
        [TestCase(5, 5, 1000, 100)]
        [TestCase(65, 48, 100, 89)]
        [TestCase(2, 1, 10000, 10)]
        [TestCase(94, 39, 64, 256)]
        public void AllocateFreeStress(int unitAlloc, int unitFree, int iterationCount, int pageSize)
        {
            var ptrDict = new Dictionary<int, IntPtr>();
            var allocatedIndices = new List<int>();

            using (var list = new PagedFreeList<int>(pageSize))
            {
                for (var i = 0; i < iterationCount; i++)
                {
                    for (var a = 0; a < unitAlloc; a++)
                    {
                        var index = list.AllocateIndex();
                        allocatedIndices.Add(index);

                        list[index] = a;
                        void* ptr;
                        fixed(int* p = &list[index])
                        {
                            ptr = p;
                            Assert.AreEqual(a, *p);
                        }

                        try
                        {
                            var existingPtr = ptrDict[index];
                            Assert.AreEqual(existingPtr, new IntPtr(ptr));
                        }
                        catch (KeyNotFoundException)
                        {
                            ptrDict.Add(index, new IntPtr(ptr));
                        }
                    }

                    for (var d = 0; d < unitFree; d++)
                    {
                        var slot = Random.Range(0, allocatedIndices.Count);
                        var indexToFree = allocatedIndices[slot];

                        void* ptr;
                        fixed(int* p = &list[indexToFree])
                        ptr = p;

                        var existingPtr = ptrDict[indexToFree];
                        Assert.AreEqual(existingPtr, new IntPtr(ptr));

                        list.FreeIndex(indexToFree);
                        allocatedIndices.Remove(indexToFree);
                    }
                }
            }
        }

        void PopulateListAndRunFunctionsInWorkerThreads(int pageCapacity, int itemCount,
            params Action<PagedFreeList<int>, int>[] functions)
        {
            using (var list = new PagedFreeList<int>(pageCapacity))
            {
                var threads = new List<Thread>();

                // Populate list
                for (int i = 0; i < itemCount; ++i)
                {
                    int index = list.AllocateIndex();
                    list[index] = index;
                }

                // Perform actions
                for (int i = 0; i < itemCount; ++i)
                {
                    var capturedIndex = i; // Need to copy i here, otherwise the closures capture it by reference
                    foreach (var action in functions)
                        threads.Add(new Thread(() => action(list, capturedIndex)));
                }

                foreach (var thread in threads)
                    thread.Start();

                foreach (var thread in threads)
                    thread.Join();
            }
        }

        [Test]
        public void Lookup_FromWorkerThreads()
        {
            PopulateListAndRunFunctionsInWorkerThreads(kPageCapacityForConcurrencyTests, kItemCountForConcurrencyTests,
                (list, index) => Assert.AreEqual(index, list[index]),
                (list, index) => Assert.Throws<IndexOutOfRangeException>(() =>
                {
                    var _ = list[index + kItemCountForConcurrencyTests];
                })
            );
        }

        [Test]
        public void Free_FromWorkerThreads()
        {
            PopulateListAndRunFunctionsInWorkerThreads(kPageCapacityForConcurrencyTests, kItemCountForConcurrencyTests,
                (list, index) => list.FreeIndex(index),
                (list, index) =>
                    Assert.Throws<IndexOutOfRangeException>(() => list.FreeIndex(index + kItemCountForConcurrencyTests))
            );
        }

        [Test]
        public void ReadAndFree_WhileAllocating()
        {
            using (var list = new PagedFreeList<int>(kPageCapacityForConcurrencyTests))
            {
                var threads = new List<Thread>();

                for (int i = 0; i < kItemCountForConcurrencyTests; ++i)
                {
                    var index = list.AllocateIndex();
                    list[index] = index;

                    var thread = new Thread(() =>
                    {
                        try
                        {
                            var _ = list[index];
                        }
                        catch (IndexOutOfRangeException)
                        {
                            // This is expected, we just want to verify that Allocate/Free don't fail in this context
                        }
                    });
                    threads.Add(thread);
                    thread.Start();

                    thread = new Thread(() => list.FreeIndex(index));
                    threads.Add(thread);
                    thread.Start();
                }

                foreach (var thread in threads)
                    thread.Join();
            }
        }
    }
}
