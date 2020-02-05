using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Media.Utilities;

unsafe class AtomicFreeLists
{
    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void CanAllocateFromEmptyList(AllocationMode allocationMode)
    {
        using (var list = new AtomicFreeList<AtomicNode>(allocationMode))
        {
            var node = list.Acquire();
            Assert.AreNotEqual(IntPtr.Zero, (IntPtr)node);
            list.Release(node);
        }
    }

    [Test]
    public void PooledList_ReusesAllocations()
    {
        using (var list = new AtomicFreeList<AtomicNode>(AllocationMode.Pooled))
        {
            var node = list.Acquire();
            list.Release(node);

            // Prevent the underlying allocator from just handing us back the same pointer
            var dummy = Utility.AllocateUnsafe<AtomicNode>(1, Allocator.TempJob);

            var secondNode = list.Acquire();
            Assert.AreEqual((IntPtr)node, (IntPtr)secondNode);
            list.Release(secondNode);
            Utility.FreeUnsafe(dummy, Allocator.TempJob);
        }
    }

    [Test]
    public void EphemeralList_DoesNotReuseAllocations()
    {
        using (var list = new AtomicFreeList<AtomicNode>(AllocationMode.Ephemeral))
        {
            var node = list.Acquire();
            list.Release(node);

            // Prevent the underlying allocator from just handing us back the same pointer
            var dummy = Utility.AllocateUnsafe<AtomicNode>(1, Allocator.TempJob);

            var secondNode = list.Acquire();
            Assert.AreNotEqual((IntPtr)node, (IntPtr)secondNode);
            list.Release(secondNode);
            Utility.FreeUnsafe(dummy, Allocator.TempJob);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void DescriptionIsValidated(AllocationMode allocationMode)
    {
        Assert.Throws<ArgumentException>(() => AtomicFreeList<AtomicNode>.FromDescription(default));
        // FIXME burst
//        using (var list = new AtomicFreeList<AtomicNode>(allocationMode))
//            Assert.Throws<ArgumentException>(() => AtomicFreeList<AtomicFreeListDescription>.FromDescription(list.Description));
    }
}
