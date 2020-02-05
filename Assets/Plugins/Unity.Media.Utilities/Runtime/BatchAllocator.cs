using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// Non-concurrent batching allocator, backed by native allocations.
    /// The user will pass pointers to storage pointers in Allocate,
    /// and then actual allocation and storate population will occur in Dispose.
    /// </summary>
    public unsafe struct BatchAllocator : IDisposable
    {
        private readonly Allocator m_Allocator;
        private GrowableBuffer<Allocation> m_Allocations;
        private int m_TotalSize;
        private int m_CurrentOffset;
        private int m_LargestAlignment;

        /// <summary>
        /// Returns a pointer to the base allocation, once allocation has occurred
        /// </summary>
        public void* AllocationRoot { get; private set; }

        /// <summary>
        /// Create a new BatchAllocator
        /// </summary>
        /// <param name="allocator">The backing native allocator to be used</param>
        public BatchAllocator(Allocator allocator)
        {
            m_Allocator = allocator;
            m_Allocations = new GrowableBuffer<Allocation>(Allocator.TempJob);
            m_TotalSize = 0;
            m_CurrentOffset = 0;
            m_LargestAlignment = 0;
            AllocationRoot = null;
        }

        /// <summary>
        /// Register an allocation
        /// </summary>
        /// <param name="count">The number of elements to be allocated</param>
        /// <param name="result">
        /// A pointer to the storage where the beginning of the allocation will be stored.
        /// The address pointed to by result will be nulled when Allocate is called, and populated when Dispose is called.
        /// </param>
        /// <typeparam name="T">The element type</typeparam>
        /// <remarks>The alignment of T will be respected within the allocation.</remarks>
        public void Allocate<T>(int count, T** result)
            where T : unmanaged
        {
            if (result == null)
                throw new ArgumentNullException("result");

            *result = null;
            if (count == 0)
                return;

            var alignment = UnsafeUtility.AlignOf<T>();
            var elementSize = AlignOffset(UnsafeUtility.SizeOf<T>(), alignment);

            var alignedOffset = AlignOffset(m_CurrentOffset, alignment);
            var size = (count * elementSize) + (alignedOffset - m_CurrentOffset);

            m_TotalSize += size;
            m_LargestAlignment = Math.Max(m_LargestAlignment, alignment);

            m_Allocations.Add(new Allocation
            {
                Pointer = (void**)result,
                Offset = alignedOffset,
            });
            m_CurrentOffset += size;
        }

        /// <summary>
        /// Perform the actual allocation and populate the registered suballocation pointers
        /// </summary>
        public void Dispose()
        {
            if (m_TotalSize > 0)
            {
                var block = (byte*)UnsafeUtility.Malloc(m_TotalSize, m_LargestAlignment, m_Allocator);
                Utility.RegisterAllocation(block, m_Allocator);
                AllocationRoot = block;

                for (int i = 0; i < m_Allocations.Count; ++i)
                {
                    var allocation = m_Allocations[i];
                    *allocation.Pointer = block + allocation.Offset;
                }
            }

            m_Allocations.Dispose();
        }

        static int AlignOffset(int offset, int alignment)
        {
            if (offset == 0)
                return offset;
            if (offset < alignment)
                return alignment;
            return offset + (alignment % (alignment - (offset % alignment)));
        }

        /// <summary>
        /// Data struct to correlate an allocation pointer and its offset
        /// </summary>
        struct Allocation
        {
            public void** Pointer;
            public int Offset;
        }
    }
}
