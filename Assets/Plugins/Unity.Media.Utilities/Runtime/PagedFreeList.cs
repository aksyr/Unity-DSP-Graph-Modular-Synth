using System;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    internal unsafe struct FreeListPage
    {
        [NativeDisableUnsafePtrRestriction]
        public long Next;
        [NativeDisableUnsafePtrRestriction]
        public void* Elements;
        [NativeDisableUnsafePtrRestriction]
        public int* Used; // No interlocked operations with bool unless you box
    }

    /// <summary>
    /// A growable, sparse list whose internal memory is never relocated once allocated
    /// </summary>
    /// <remarks>
    /// The intended usage is that allocation and writing will only happen from a single thread,
    /// while indexing and releasing may happen from any thread
    /// </remarks>
    /// <typeparam name="T">The element type of the list</typeparam>
    public unsafe struct PagedFreeList<T> : IDisposable, IEquatable<PagedFreeList<T>>, IValidatable
        where T : unmanaged
    {
        internal int PageCapacity;
        [NativeDisableUnsafePtrRestriction]
        internal FreeListPage* Root;
        private Allocator m_Allocator;

        /// <summary>
        /// The currently allocated capacity of the list
        /// </summary>
        /// <remarks>
        /// Can be called from any thread
        /// </remarks>
        public int Capacity
        {
            get
            {
                this.Validate();
                var capacity = 0;
                for (FreeListPage* page = Root; page != null; page = (FreeListPage*)Utility.InterlockedReadLong(ref page->Next))
                    capacity += PageCapacity;
                return capacity;
            }
        }

        /// <summary>
        /// Create a new PagedFreeList
        /// </summary>
        /// <param name="pageCapacity">The number of elements per allocated page</param>
        /// <param name="allocator">The allocator to be used</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if pageCapacity is less than or equal to zero</exception>
        public PagedFreeList(int pageCapacity, Allocator allocator = Allocator.Persistent)
        {
            ValidateCapacity(pageCapacity);
            PageCapacity = pageCapacity;
            m_Allocator = allocator;
            Root = AllocatePage(pageCapacity, allocator);
        }

        private static void ValidateCapacity(int pageCapacity)
        {
            ValidateCapacityMono(pageCapacity);
            ValidateCapacityBurst(pageCapacity);
        }

        [BurstDiscard]
        private static void ValidateCapacityMono(int pageCapacity)
        {
            if (pageCapacity <= 0)
                throw new ArgumentOutOfRangeException("pageCapacity");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateCapacityBurst(int pageCapacity)
        {
            if (pageCapacity <= 0)
                throw new ArgumentOutOfRangeException("pageCapacity");
        }

        /// <summary>
        /// Whether the list is valid
        /// </summary>
        public bool Valid => PageCapacity != 0;

        /// <summary>
        /// Query whether a given index is currently allocated for use
        /// </summary>
        /// <param name="index">The index to query</param>
        /// <remarks>
        /// Can be called from any thread (although the value of calling this in a concurrent context is limited)
        /// </remarks>
        /// <returns>True if the index is allocated, otherwise false</returns>
        public bool IndexIsValid(int index)
        {
            return Lookup(index, out _, out _);
        }

        bool Lookup(int index, out FreeListPage* page, out int pageRelativeIndex)
        {
            page = Root;
            pageRelativeIndex = index % PageCapacity;

            if (index < 0)
                return false;

            var pageCount = index / PageCapacity;
            for (int pageIndex = 0; pageIndex < pageCount; ++pageIndex)
            {
                var nextPage = Utility.InterlockedReadLong(ref page->Next);
                if (nextPage == 0)
                    return false;
                page = (FreeListPage*)nextPage;
            }

            return Utility.InterlockedReadInt(ref page->Used[pageRelativeIndex]) != 0;
        }

        /// <summary>
        /// Returns a reference to the item at the specified index
        /// </summary>
        /// <param name="index">The index of the desired item</param>
        /// <exception cref="IndexOutOfRangeException">Thrown if index is invalid or not in use</exception>
        public ref T this[int index]
        {
            get
            {
                this.Validate();
                if (Lookup(index, out FreeListPage * page, out var pageRelativeIndex))
                    return ref ((T*)page->Elements)[pageRelativeIndex];
                throw new IndexOutOfRangeException($"Index {index} is out of range of {Capacity} or not allocated");
            }
        }

        private void ThrowIndexOutOfRangeException(int index)
        {
            ThrowIndexOutOfRangeExceptionMono(index);
            ThrowIndexOutOfRangeExceptionBurst(index);
        }

        [BurstDiscard]
        private void ThrowIndexOutOfRangeExceptionMono(int index)
        {
            throw new IndexOutOfRangeException($"Index {index} is out of range of {Capacity} or not allocated");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ThrowIndexOutOfRangeExceptionBurst(int index)
        {
            throw new IndexOutOfRangeException($"Index {index} is out of range of {Capacity} or not allocated");
        }

        /// <summary>
        /// Allocate the next index for use
        /// </summary>
        /// <remarks>
        /// Can only be used from a single thread
        /// </remarks>
        /// <returns>The allocated index</returns>
        public int AllocateIndex()
        {
            this.Validate();
            FreeListPage* current;
            FreeListPage* lastValidPage = null;
            var totalIndex = 0;

            for (current = Root; current != null; lastValidPage = current, current = (FreeListPage*)current->Next)
            {
                for (var i = 0; i < PageCapacity; i++)
                {
                    if (Interlocked.CompareExchange(ref current->Used[i], 1, 0) == 0)
                    {
                        ((T*)current->Elements)[i] = default;
                        return totalIndex + i;
                    }
                }

                totalIndex += PageCapacity;
            }

            // Need a new page yo.
            current = AllocatePage(PageCapacity, m_Allocator);
            current->Used[0] = 1;
            lastValidPage->Next = (long)current;
            Interlocked.MemoryBarrier();

            return totalIndex;
        }

        /// <summary>
        /// Release the specified index
        /// </summary>
        /// <remarks>
        /// Can be called from any thread
        /// </remarks>
        /// <param name="index">The index to release</param>
        /// <exception cref="IndexOutOfRangeException">Thrown if index is invalid or not in use</exception>
        public void FreeIndex(int index)
        {
            this.Validate();
            if (!Lookup(index, out FreeListPage * page, out int pageRelativeIndex))
                ThrowIndexOutOfRangeException(index);

            ((T*)page->Elements)[pageRelativeIndex] = default;
            Interlocked.Exchange(ref page->Used[pageRelativeIndex], 0);
        }

        static FreeListPage* AllocatePage(int pageSize, Allocator allocator)
        {
            FreeListPage* newPage;
            T* elementBuffer;
            int* usageBuffer;

            var batchAllocator = new BatchAllocator(allocator);
            batchAllocator.Allocate(1, &newPage);
            batchAllocator.Allocate(pageSize, &elementBuffer);
            batchAllocator.Allocate(pageSize, &usageBuffer);
            batchAllocator.Dispose();
            UnsafeUtility.MemClear(batchAllocator.AllocationRoot, batchAllocator.TotalSize);

            *newPage = new FreeListPage
            {
                Next = 0,
                Elements = elementBuffer,
                Used = usageBuffer,
            };

            return newPage;
        }

        /// <summary>
        /// Dispose the list's allocated storage
        /// </summary>
        public void Dispose()
        {
            this.Validate();

            for (var current = Root; current != null;)
            {
                var next = current->Next;
                Utility.FreeUnsafe(current, m_Allocator);
                current = (FreeListPage*)next;
            }
        }

        /// <summary>
        /// Whether this is the same list as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(PagedFreeList<T> other)
        {
            return Root == other.Root;
        }
    }
}
