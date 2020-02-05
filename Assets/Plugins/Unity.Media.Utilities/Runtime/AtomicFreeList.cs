using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A generic, multiple-reader, multiple-writer concurrent free list
    /// </summary>
    /// <typeparam name="T">The element type for the list. For technical reasons, the size of T must be at least the size of AtomicNode.</typeparam>
    public unsafe struct AtomicFreeList<T> : IDisposable, IValidatable
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private readonly long* m_FreeList;
        private readonly AllocationMode m_AllocationMode;
        private readonly Allocator m_Allocator;

        /// <summary>
        /// Get the unmanaged description of the free list
        /// </summary>
        public AtomicFreeListDescription Description
        {
            get
            {
                Validate();
                return new AtomicFreeListDescription
                {
                    FreeList = m_FreeList,
                    AllocationMode = (int)m_AllocationMode,
                    Allocator = m_Allocator,
                    // FIXME: Using element size here for now instead of BurstRuntime.GetHashCode64
                    // because it returns different results on mono and burst
                    ElementTypeHash = UnsafeUtility.SizeOf<T>(),
                };
            }
        }

        /// <summary>
        /// Create a new AtomicFreeList
        /// </summary>
        /// <param name="allocationMode">The allocation mode to be used</param>
        /// <param name="allocator">The native allocator to be used</param>
        public AtomicFreeList(AllocationMode allocationMode, Allocator allocator = Allocator.Persistent)
        {
            // This is required because of casting T* => AtomicNode* tomfoolery when releasing nodes back into the free list
            ValidateTypeSize();
            m_FreeList = Utility.AllocateUnsafe<long>(1,  allocator);
            *m_FreeList = 0;
            m_AllocationMode = allocationMode;
            m_Allocator = allocator;
        }

        private AtomicFreeList(long* freeList, AllocationMode allocationMode, Allocator allocator)
        {
            m_FreeList = freeList;
            m_AllocationMode = allocationMode;
            m_Allocator = allocator;
        }

        private static void ValidateTypeSize()
        {
            ValidateTypeSizeWithMeaningfulMessage();
            if (sizeof(T) < sizeof(AtomicNode))
                throw new ArgumentException("Type is too small");
        }

        [BurstDiscard]
        private static void ValidateTypeSizeWithMeaningfulMessage()
        {
            if (sizeof(T) < sizeof(AtomicNode))
                throw new ArgumentException($"Size of type must be at least {sizeof(AtomicNode)}", nameof(T));
        }

        /// <summary>
        /// Creates an AtomicFreeList wrapping the data contained in the description.
        /// </summary>
        /// <remarks>This does not copy any data from the description.</remarks>
        /// <param name="description">The description</param>
        /// <returns>An AtomicFreeList wrapping the data contained in the description</returns>
        public static AtomicFreeList<T> FromDescription(AtomicFreeListDescription description)
        {
            ValidateDescription(description);
            return new AtomicFreeList<T>(description.FreeList, (AllocationMode)description.AllocationMode, description.Allocator);
        }

        private static void ValidateDescription(AtomicFreeListDescription description)
        {
            ValidateDescriptionWithMeaningfulMessages(description);
            if (description.FreeList == null)
                throw new ArgumentException("Invalid description");
            // FIXME: Burst sometimes generates different sizes(!) than mono/il2cpp, so we won't validate those right now
        }

        [BurstDiscard]
        private static void ValidateDescriptionWithMeaningfulMessages(AtomicFreeListDescription description)
        {
            if (description.FreeList == null)
                throw new ArgumentException("Invalid description", nameof(description));
        }

        //TODO: Extract atomic stack
        void PushToFreeList(AtomicNode* node)
        {
            while (true)
            {
                Interlocked.Exchange(ref node->Next, *m_FreeList);
                if (Interlocked.CompareExchange(ref *m_FreeList, (long)node, node->Next) == node->Next)
                    break;
                Utility.YieldProcessor();
            }
        }

        AtomicNode* PopFromFreeList()
        {
            while (true)
            {
                var freeList = (AtomicNode*)Interlocked.CompareExchange(ref *m_FreeList, 0, 0);
                if (freeList == null)
                    return null;
                if (Interlocked.CompareExchange(ref *m_FreeList, freeList->Next, (long)freeList) == (long)freeList)
                    return (AtomicNode*)freeList;
                Utility.YieldProcessor();
            }
        }

        /// <summary>
        /// Acquires a new element from the free list.
        /// If no elements are available, or the allocation mode is ephemeral, a new element is allocated.
        /// </summary>
        /// <returns>A "new" element</returns>
        public T* Acquire()
        {
            if (m_AllocationMode == AllocationMode.Ephemeral)
                return Utility.AllocateUnsafe<T>(1,  Allocator.TempJob);

            var node = PopFromFreeList();
            if (node == null)
                return Utility.AllocateUnsafe<T>(1,  m_Allocator);

            return (T*)node;
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

        public bool Valid => m_FreeList != null;

        void Validate()
        {
            if (!Valid)
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// The allocation mode to be used with an AtomicFreeList<T>
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

    /// <summary>
    /// A node for use in an atomic collection
    /// </summary>
    /// <remarks>This must be external to AtomicFreeList<T> because it must be unmanaged</remarks>
    public struct AtomicNode
    {
        /// <summary>
        /// A pointer to the next node
        /// </summary>
        public long Next;

        /// <summary>
        /// A pointer to the actual data managed by this node
        /// </summary>
        public long Payload;

        /// <summary>
        /// Allocate a new AtomicNode with the provided payload and next pointers
        /// </summary>
        /// <param name="payload">The payload for the new node</param>
        /// <param name="next">The next pointer for the new node</param>
        /// <param name="allocator">The native allocator to use</param>
        /// <returns></returns>
        public static unsafe AtomicNode* Create(long payload = 0, long next = 0, Allocator allocator = Allocator.TempJob)
        {
            var node = Utility.AllocateUnsafe<AtomicNode>(1, allocator);
            node->Next = next;
            node->Payload = payload;
            return node;
        }
    }

    /// <summary>
    /// An unmanaged data structure meant to describe an AtomicFreeList<T>
    /// </summary>
    public unsafe struct AtomicFreeListDescription
    {
        [NativeDisableUnsafePtrRestriction]
        public long* FreeList;
        public long ElementTypeHash;
        public int AllocationMode;
        public Allocator Allocator;
    }
}
