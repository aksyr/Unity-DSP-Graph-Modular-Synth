using System;
using System.Diagnostics;
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
    /// <remarks>The difference between this and <see cref="OwnedAtomicQueue{T}"/> is that <see cref="OwnedAtomicQueue{T}"/> manages the payload storage for you and this does not.</remarks>
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    public unsafe struct AtomicQueue<T> : IDisposable, IValidatable, IEquatable<AtomicQueue<T>>
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private long* m_Root;
        private AtomicFreeList<AtomicNode> m_FreeList;

        /// <summary>
        /// Whether this queue is valid
        /// </summary>
        public bool Valid => m_Root != null;

        /// <summary>
        /// Whether the queue is empty
        /// </summary>
        /// <remarks>Due to the concurrent nature of the queue, users should be careful about using this property for decision making</remarks>
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

        /// <summary>
        /// Create an AtomicQueue&lt;T&gt;
        /// </summary>
        /// <remarks>This is the only valid way to create an AtomicQueue&lt;T&gt;</remarks>
        /// <param name="allocationMode">Use the specified allocation mode for the queue's internal free list</param>
        /// <returns></returns>
        public static AtomicQueue<T> Create(AllocationMode allocationMode = AllocationMode.Pooled)
        {
            var root = Utility.AllocateUnsafe<long>();
            *root = (long)AtomicNode.Create(0, 0, Allocator.Persistent);
            return new AtomicQueue<T>
            {
                m_Root = root,
                m_FreeList = new AtomicFreeList<AtomicNode>(allocationMode),
            };
        }

        /// <summary>
        /// Add an element to the queue
        /// </summary>
        /// <param name="payload">A pointer to the element to be added</param>
        public void Enqueue(T* payload)
        {
            var node = (long)Acquire((long)payload, 0);
            AtomicNode* root = AcquireRoot();
            var last = (AtomicNode*)root->Payload;
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
                var root = (AtomicNode*)Interlocked.Exchange(ref *m_Root, 0);
                if (root != null)
                    return root;
                Utility.YieldProcessor();
            }
        }

        private void ReleaseRoot(AtomicNode* root)
        {
            if (Interlocked.Exchange(ref *m_Root, (long)root) != 0)
                ThrowConcurrencyError();
        }

        private static void ThrowConcurrencyError()
        {
            ThrowConcurrencyErrorMono();
            ThrowConcurrencyErrorBurst();
        }

        [BurstDiscard]
        private static void ThrowConcurrencyErrorMono()
        {
            throw new InvalidOperationException("Concurrency error, releasing root that isn't held");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowConcurrencyErrorBurst()
        {
            throw new InvalidOperationException("Concurrency error, releasing root that isn't held");
        }

        /// <summary>
        /// Try to remove an element from the queue.
        /// </summary>
        /// <param name="result">
        /// If there's an element available, this parameter will be updated to that element.
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
                result = (T*)first->Payload;
                root->Next = first->Next;
                // if the first node was also the last node, we need to mark the last pointer as well
                if (root->Payload == (long)first)
                    root->Payload = 0;
            }

            ReleaseRoot(root);
            if (dequeued)
                m_FreeList.Release(first);
            return dequeued;
        }

        /// <summary>
        /// Remove an element from the queue
        /// </summary>
        /// <returns>A pointer to the next element from the queue</returns>
        /// <exception cref="InvalidOperationException">If the queue is empty</exception>
        public T* Dequeue()
        {
            if (TryDequeue(out T * result))
                return result;

            ThrowEmptyQueueException();
            return null;
        }

        private void ThrowEmptyQueueException()
        {
            ThrowEmptyQueueExceptionMono();
            ThrowEmptyQueueExceptionBurst();
        }

        [BurstDiscard]
        private void ThrowEmptyQueueExceptionMono()
        {
            throw new InvalidOperationException("Queue is empty");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ThrowEmptyQueueExceptionBurst()
        {
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
                ThrowEmptyQueueException();
            return payload;
        }

        /// <summary>
        /// Dispose all resources belonging to the queue
        /// </summary>
        public void Dispose()
        {
            while (TryDequeue(out T * _)) {}
            m_FreeList.Dispose();
            Utility.FreeUnsafe((AtomicNode*)*m_Root);
            Utility.FreeUnsafe(m_Root);
        }

        AtomicNode* Acquire(long payload, long next)
        {
            m_FreeList.Acquire(out AtomicNode * node);
            Interlocked.Exchange(ref node->Payload, payload);
            Interlocked.Exchange(ref node->Next, next);
            return node;
        }

        /// <summary>
        /// Whether this is the same queue as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(AtomicQueue<T> other)
        {
            return m_Root == other.m_Root;
        }

        /// <summary>
        /// Whether this is the same queue as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is AtomicQueue<T> other && Equals(other);
        }

        /// <summary>
        /// Return a unique hash for this queue
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return unchecked((int)(long)m_Root);
        }
    }
}
