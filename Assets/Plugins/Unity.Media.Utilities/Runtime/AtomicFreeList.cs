using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A generic, multiple-reader, multiple-writer concurrent free list
    /// </summary>
    /// <typeparam name="T">The element type for the list. For technical reasons, the size of T must be at least the size of AtomicNode.</typeparam>
    public readonly unsafe struct AtomicFreeList<T> : IDisposable, IValidatable, IEquatable<AtomicFreeList<T>>
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private readonly long* m_FreeList;
        private readonly AllocationMode m_AllocationMode;
        private readonly Allocator m_Allocator;

        private static readonly int s_ElementSize = Math.Max(UnsafeUtility.SizeOf<T>(), UnsafeUtility.SizeOf<AtomicNode>());
        private static readonly int s_ElementAlignment = Math.Max(UnsafeUtility.AlignOf<T>(), UnsafeUtility.AlignOf<AtomicNode>());

        /// <summary>
        /// Create a new AtomicFreeList
        /// </summary>
        /// <param name="allocationMode">The allocation mode to be used</param>
        /// <param name="allocator">The native allocator to be used</param>
        public AtomicFreeList(AllocationMode allocationMode, Allocator allocator = Allocator.Persistent)
        {
            m_FreeList = Utility.AllocateUnsafe<long>(1,  allocator);
            *m_FreeList = 0;
            m_AllocationMode = allocationMode;
            m_Allocator = allocator;
        }

        //TODO: Extract atomic stack
        private void PushToFreeList(AtomicNode* node)
        {
            while (true)
            {
                var currentHead = Utility.InterlockedReadLong(ref *m_FreeList);
                Interlocked.Exchange(ref node->Next, currentHead);
                if (Interlocked.CompareExchange(ref *m_FreeList, (long)node, currentHead) == currentHead)
                    break;
                Utility.YieldProcessor();
            }
        }

        private AtomicNode* PopFromFreeList()
        {
            while (true)
            {
                var freeList = (AtomicNode*)Utility.InterlockedReadLong(ref *m_FreeList);
                if (freeList == null)
                    return null;
                if (Interlocked.CompareExchange(ref *m_FreeList, Utility.InterlockedReadLong(ref freeList->Next), (long)freeList) == (long)freeList)
                    return freeList;

                Utility.YieldProcessor();
            }
        }

        /// <summary>
        /// Acquires a new element from the free list.
        /// If no elements are available, or the allocation mode is ephemeral, a new element is allocated.
        /// </summary>
        /// <param name="element">A "new" element is stored here</param>
        /// <returns>True if an element was reused from the pool, otherwise false</returns>
        public bool Acquire(out T* element)
        {
            if (m_AllocationMode == AllocationMode.Ephemeral)
            {
                element = Utility.AllocateUnsafe<T>(1, Allocator.TempJob);
                return false;
            }

            AtomicNode* node = PopFromFreeList();
            bool acquired = (node != null);
            element = acquired ? (T*)node : AllocateElement();
            return acquired;
        }

        private T* AllocateElement()
        {
            return (T*)UnsafeUtility.Malloc(s_ElementSize, s_ElementAlignment, m_Allocator);
        }

        /// <summary>
        /// Releases an element into the free list
        /// </summary>
        /// <remarks>If the allocation mode is ephemeral, the element will be freed immediately</remarks>
        /// <param name="element">The element to be released</param>
        public void Release(T* element)
        {
            if (m_AllocationMode == AllocationMode.Ephemeral)
                Utility.FreeUnsafe(element, Allocator.TempJob);
            else
                // TODO: Upper bound on free list size?
                PushToFreeList((AtomicNode*)element);
        }

        /// <summary>
        /// Disposes the free list and releases all freed elements
        /// </summary>
        public void Dispose()
        {
            var allocator = (m_AllocationMode == AllocationMode.Ephemeral ? Allocator.TempJob : m_Allocator);
            for (AtomicNode* node = PopFromFreeList(); node != null; node = PopFromFreeList())
                Utility.FreeUnsafe(node, allocator);
            Utility.FreeUnsafe(m_FreeList, m_Allocator);
        }

        /// <summary>
        /// Whether the free list is valid
        /// </summary>
        public bool Valid => m_FreeList != null;

        /// <summary>
        /// Whether this is the same free list as another instance
        /// </summary>
        /// <param name="other">The free list to compare</param>
        /// <returns></returns>
        public bool Equals(AtomicFreeList<T> other)
        {
            return m_FreeList == other.m_FreeList;
        }

        /// <summary>
        /// Whether this is the same free list as another instance
        /// </summary>
        /// <param name="obj">The free list to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is AtomicFreeList<T> other && Equals(other);
        }

        /// <summary>
        /// Return a unique hash code for this free list
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return unchecked((int)(long)m_FreeList);
        }
    }

    /// <summary>
    /// The allocation mode to be used with an AtomicFreeList&lt;T&gt;
    /// </summary>
    public enum AllocationMode
    {
        /// <summary>
        /// All acquisition and releasing of elements will result in immediate allocation/deallocation
        /// </summary>
        Ephemeral,

        /// <summary>
        /// Freed elements will be pooled, and acquisition will reuse elements from the pool if possible
        /// </summary>
        Pooled,
    }
}
