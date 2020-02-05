using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;

public unsafe class BatchAllocatorTests
{
    [SetUp]
    public void Setup()
    {
        Utility.EnableLeakTracking = true;
    }

    [TearDown]
    public void Teardown()
    {
        Utility.VerifyLeaks();
        Utility.EnableLeakTracking = false;
    }

    [Test]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(100)]
    public void BasicAllocation(int blocksize)
    {
        int* first;
        int* second;
        int* third;

        using (var allocator = new BatchAllocator(Allocator.TempJob))
        {
            allocator.Allocate(blocksize, &first);
            allocator.Allocate(blocksize, &second);
            allocator.Allocate(blocksize, &third);

            Assert.That(first == null);
            Assert.That(second == null);
            Assert.That(third == null);
        }

        Assert.That(first != null);
        Assert.That(second != null);
        Assert.That(third != null);

        Assert.That(second == first + blocksize);
        Assert.That(third == second + blocksize);

        Utility.FreeUnsafe(first, Allocator.TempJob);
    }

    [Test]
    public void EmptyBatch_AvoidsAllocation()
    {
        // This will leak in teardown if we allocate
        using (new BatchAllocator(Allocator.TempJob)) {}
    }

    [Test]
    public void ZeroSizeAllocation_DoesNotPopulatePointer()
    {
        int* first;
        int* second;
        int* third;

        using (var allocator = new BatchAllocator(Allocator.TempJob))
        {
            allocator.Allocate(1, &first);
            allocator.Allocate(0, &second);
            allocator.Allocate(1, &third);
        }

        Assert.That(first != null);
        Assert.That(second == null);
        Assert.That(third == first + 1);

        Utility.FreeUnsafe(first, Allocator.TempJob);
    }

    [Test]
    public void TypeAlignment_BetweenAllocations_IsRespected()
    {
        byte* first;
        IntPtr* second;
        using (var allocator = new BatchAllocator(Allocator.TempJob))
        {
            allocator.Allocate(1, &first);
            allocator.Allocate(1, &second);
        }

        Assert.That(second == first + UnsafeUtility.AlignOf<IntPtr>());

        Utility.FreeUnsafe(first, Allocator.TempJob);
    }

    [Test]
    public void TypeAlignment_WithinAllocations_IsRespected()
    {
        byte* first;
        IntPtr* second;
        using (var allocator = new BatchAllocator(Allocator.TempJob))
        {
            allocator.Allocate(4, &first);
            allocator.Allocate(1, &second);
        }

        Assert.That(second > first + 4 * UnsafeUtility.SizeOf<byte>());

        Utility.FreeUnsafe(first, Allocator.TempJob);
    }

    [Test]
    public void AllocationRoot()
    {
        byte* first;
        IntPtr* second;

        var allocator = new BatchAllocator(Allocator.TempJob);
        allocator.Allocate(4, &first);
        allocator.Allocate(1, &second);
        Assert.AreEqual(IntPtr.Zero, (IntPtr)allocator.AllocationRoot);
        allocator.Dispose();

        Assert.AreEqual((IntPtr)first, (IntPtr)allocator.AllocationRoot);
        Utility.FreeUnsafe(allocator.AllocationRoot, Allocator.TempJob);
    }

    [Test]
    public void AllocationRoot_WithEmptyLeadingAllocations()
    {
        byte* first;
        IntPtr* second;

        var allocator = new BatchAllocator(Allocator.TempJob);
        allocator.Allocate(0, &first);
        allocator.Allocate(1, &second);
        Assert.AreEqual(IntPtr.Zero, (IntPtr)allocator.AllocationRoot);
        allocator.Dispose();

        Assert.AreEqual((IntPtr)second, (IntPtr)allocator.AllocationRoot);
        Utility.FreeUnsafe(allocator.AllocationRoot, Allocator.TempJob);
    }

    [Test]
    public void AllocationRoot_WithNoAllocations_IsNull()
    {
        var allocator = new BatchAllocator(Allocator.TempJob);
        allocator.Dispose();
        Assert.AreEqual(IntPtr.Zero, (IntPtr)allocator.AllocationRoot);

        byte* first;
        IntPtr* second;
        allocator = new BatchAllocator(Allocator.TempJob);
        allocator.Allocate(0, &first);
        allocator.Allocate(0, &second);
        allocator.Dispose();
        Assert.AreEqual(IntPtr.Zero, (IntPtr)allocator.AllocationRoot);
    }

    [Test]
    public void AllocationStorage_IsValidated()
    {
        using (var allocator = new BatchAllocator(Allocator.TempJob))
        {
            Assert.Throws<ArgumentNullException>(() => allocator.Allocate<int>(0, null));
            Assert.Throws<ArgumentNullException>(() => allocator.Allocate<int>(1, null));
        }
    }
}
