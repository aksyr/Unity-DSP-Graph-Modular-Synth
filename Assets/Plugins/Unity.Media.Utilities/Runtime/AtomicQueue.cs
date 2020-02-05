using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A generic, multiple-reader, multiple-writer concurrent queue.
    /// The queue uses a sentinel node to track each end of the queue.
    /// When the queue is empty, the first and last nodes point to each other.
    /// Elements are added to the queue between the last and next-to-last nodes, whose pointers are updated accordingly.
    /// The element removed from the queue is always the next-to-first, and the pointers of the first and next-to-next-to-first elements are updated.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    public unsafe struct AtomicQueue<T> : IDisposable, IValidatable
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private AtomicNode** m_Root;
        private AtomicFreeListDescription m_FreeList;

        public bool Valid => m_Root != null;

        public bool IsEmpty
        {
            get
            {
                var root = AcquireRoot();
                var empty = (root->Next == 0);
                ReleaseRoot(root);
                return empty;
            }
        }

        private AtomicFreeList<AtomicNode> FreeList => AtomicFreeList<AtomicNode>.FromDescription(m_FreeList);

        /// <summary>
        /// Get the unmanaged description of the queue
        /// </summary>
        public AtomicQueueDescription Description
        {
            get
            {
                Validate();
                return new AtomicQueueDescription
                {
                    Root = m_Root,
                    FreeList = m_FreeList,
                    ElementTypeHash = UnsafeUtility.SizeOf<T>(),
                };
            }
        }

        /// <summary>
        /// Create an AtomicQueue<T>
        /// </summary>
        /// <remarks>This is the only valid way to create an AtomicQueue<T></remarks>
        /// <param name="allocationMode">Use the specified allocation mode for the queue's internal free list</param>
        /// <returns></returns>
        public static AtomicQueue<T> Create(AllocationMode allocationMode = AllocationMode.Pooled)
        {
            var root = (AtomicNode**)Utility.AllocateUnsafe<IntPtr>();
            *root = AtomicNode.Create(0, 0, Allocator.Persistent);
            return new AtomicQueue<T>
            {
                m_Root = root,
                m_FreeList = new AtomicFreeList<AtomicNode>(allocationMode).Description,
            };
        }

        /// <summary>
        /// Creates an AtomicQueue<T> wrapping the data contained in the description.
        /// </summary>
        /// <remarks>This does not copy any data from the description.</remarks>
        /// <param name="description">The description</param>
        /// <returns>An AtomicQueue<T> wrapping the data contained in the description</returns>
        public static AtomicQueue<T> FromDescription(AtomicQueueDescription description)
        {
            ValidateDescription(description);
            return new AtomicQueue<T>
            {
                m_Root = description.Root,
                m_FreeList = description.FreeList,
            };
        }

        private static void ValidateDescription(AtomicQueueDescription description)
        {
            ValidateDescriptionWithMeaningfulMessages(description);
            if (description.Root == null)
                throw new ArgumentException("Invalid description");
        }

        [BurstDiscard]
        private static void ValidateDescriptionWithMeaningfulMessages(AtomicQueueDescription description)
        {
            if (description.Root == null)
                throw new ArgumentException("Invalid description", nameof(description));
        }

        /// <summary>
        /// Add an element to the queue
        /// </summary>
        /// <param name="payload">A pointer to the element to be added</param>
        public void Enqueue(T* payload)
        {
            var node = (long) Acquire((long) payload, 0);
            AtomicNode* root = AcquireRoot();
            var last = (AtomicNode*) root->Payload;
            root->Payload = node;
            if (last != null)
                last->Next = node;
            if (root->Next == 0)
                // Queue was empty
                root->Next = node;
            ReleaseRoot(root);
        }

        private AtomicNode* AcquireRoot()
        {
            while (true)
            {
                var root = (AtomicNode*) Interlocked.Exchange(ref *(long*) m_Root, 0);
                if (root != null)
                    return root;
                Utility.YieldProcessor();
            }
        }

        private void ReleaseRoot(AtomicNode* root)
        {
            if (Interlocked.Exchange(ref *(long*) m_Root, (long) root) != 0)
                throw new InvalidOperationException("Concurrency error, releasing root that isn't held");
        }

        /// <summary>
        /// Try to remove an element from the queue.
        /// </summary>
        /// <param name="result">
        /// If there's a element available, this parameter will be updated to 
        /// that element.
        /// </param>
        /// <returns>Whether the dequeueing operation succeeded.</returns>
        public bool TryDequeue(out T* result)
        {
            result = null;
            AtomicNode* root = AcquireRoot();

            var first = (AtomicNode*)root->Next;
            var dequeued = (first != null);
            if (dequeued)
            {
                result = (T*) first->Payload;
                root->Next = first->Next;
                // if the first node was also the last node, we need to mark the last pointer as well
                if (root->Payload == (long) first)
                    root->Payload = 0;
            }

            ReleaseRoot(root);
            if (dequeued)
                FreeList.Release(first);
            return dequeued;
        }

        /// <summary>
        /// Remove an element from the queue
        /// </summary>
        /// <returns>A pointer to the next element from the queue</returns>
        /// <exception cref="InvalidOperationException">If the queue is empty</exception>
        public T* Dequeue()
        {
            if (TryDequeue(out T* result))
                return result;

            throw new InvalidOperationException("Queue is empty");
        }

        /// <summary>
        /// Returns the next element from the queue without removing it
        /// </summary>
        /// <returns>A pointer to the next element from the queue</returns>
        /// <exception cref="InvalidOperationException">If the queue is empty</exception>
        public T* Peek()
        {
            AtomicNode* root = AcquireRoot();
            bool empty = (root->Next == 0);
            var payload = (T*)(empty ? 0 : ((AtomicNode*)root->Next)->Payload);
            ReleaseRoot(root);
            if (empty)
                throw new InvalidOperationException("Queue is empty");
            return payload;
        }

        /// <summary>
        /// Dispose all resources belonging to the queue
        /// </summary>
        public void Dispose()
        {
            while (TryDequeue(out _)) { }
            FreeList.Dispose();
            Utility.FreeUnsafe(*m_Root);
            Utility.FreeUnsafe(m_Root);
        }

        AtomicNode* Acquire(long payload, long next)
        {
            AtomicNode* node = FreeList.Acquire();
            Interlocked.Exchange(ref node->Payload, payload);
            Interlocked.Exchange(ref node->Next, next);
            return node;
        }

        private void Validate()
        {
            if (!Valid)
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// An unmanaged data structure meant to describe an AtomicQueue<T>
    /// </summary>
    public unsafe struct AtomicQueueDescription
    {
        [NativeDisableUnsafePtrRestriction]
        public AtomicNode** Root;
        public long ElementTypeHash;
        public AtomicFreeListDescription FreeList;
    }
}
