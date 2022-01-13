using System;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Media.Utilities.Tests.Runtime
{
    unsafe class AtomicFreeLists
    {
        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void CanAllocateFromEmptyList(AllocationMode allocationMode)
        {
            using (var list = new AtomicFreeList<AtomicNode>(allocationMode))
            {
                list.Acquire(out var node);
                Assert.AreNotEqual(IntPtr.Zero, (IntPtr)node);
                list.Release(node);
            }
        }

        [Test]
        public void PooledList_ReusesAllocations()
        {
            using (var list = new AtomicFreeList<AtomicNode>(AllocationMode.Pooled))
            {
                list.Acquire(out var node);
                list.Release(node);

                // Prevent the underlying allocator from just handing us back the same pointer
                var dummy = Utility.AllocateUnsafe<AtomicNode>(1, Allocator.TempJob);

                Assert.True(list.Acquire(out var secondNode));
                Assert.AreEqual((IntPtr)node, (IntPtr)secondNode);
                list.Release(secondNode);
                Utility.FreeUnsafe(dummy, Allocator.TempJob);
            }
        }

        [Test]
        public void PooledList_ReusesAllocations_WithSmallTypes()
        {
            using var list = new AtomicFreeList<int>(AllocationMode.Pooled);
            list.Acquire(out var item);
            list.Release(item);

            // Prevent the underlying allocator from just handing us back the same pointer
            var dummy = Utility.AllocateUnsafe<int>(1, Allocator.TempJob);

            Assert.True(list.Acquire(out var secondNode));
            Assert.AreEqual((IntPtr)item, (IntPtr)secondNode);
            list.Release(secondNode);
            Utility.FreeUnsafe(dummy, Allocator.TempJob);
        }

        [Test]
        public void EphemeralList_DoesNotReuseAllocations()
        {
            using (var list = new AtomicFreeList<AtomicNode>(AllocationMode.Ephemeral))
            {
                list.Acquire(out var node);
                list.Release(node);

                // Prevent the underlying allocator from just handing us back the same pointer
                var dummy = Utility.AllocateUnsafe<AtomicNode>(1, Allocator.TempJob);

                Assert.False(list.Acquire(out var secondNode));
                Assert.AreNotEqual((IntPtr)node, (IntPtr)secondNode);
                list.Release(secondNode);
                Utility.FreeUnsafe(dummy, Allocator.TempJob);
            }
        }
    }
}
