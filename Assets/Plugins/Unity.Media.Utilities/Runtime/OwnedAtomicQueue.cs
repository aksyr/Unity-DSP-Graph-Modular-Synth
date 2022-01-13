using System;
using System.Diagnostics;
using Unity.Burst;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A generic, multiple-reader, multiple-writer concurrent queue.
    /// The queue uses a sentinel node to track each end of the queue.
    /// When the queue is empty, the first and last nodes point to each other.
    /// Elements are added to the queue between the last and next-to-last nodes, whose pointers are updated accordingly.
    /// The element removed from the queue is always the next-to-first, and the pointers of the first and next-to-next-to-first elements are updated.
    /// </summary>
    /// <remarks>The difference between this and <see cref="AtomicQueue{T}"/> is that this manages the payload storage for you and <see cref="AtomicQueue{T}"/> does not.</remarks>
    /// <typeparam name="T">The element type</typeparam>
    public unsafe struct OwnedAtomicQueue<T> : IDisposable, IValidatable, IEquatable<OwnedAtomicQueue<T>>
        where T : unmanaged
    {
        private AtomicQueue<T> m_Queue;
        private AtomicFreeList<T> m_PayloadFreeList;

        /// <summary>
        /// Whether this queue is valid
        /// </summary>
        public bool Valid => m_Queue.Valid;

        /// <summary>
        /// Whether the queue is empty
        /// </summary>
        /// <remarks>Due to the concurrent nature of the queue, users should be careful about using this property for decision making</remarks>
        public bool IsEmpty => m_Queue.IsEmpty;

        /// <summary>
        /// Create an OwnedAtomicQueue&lt;T&gt;
        /// </summary>
        /// <remarks>This is the only valid way to create an OwnedAtomicQueue&lt;T&gt;</remarks>
        /// <param name="allocationMode">Use the specified allocation mode for the queue's internal free list</param>
        /// <returns></returns>
        public static OwnedAtomicQueue<T> Create(AllocationMode allocationMode = AllocationMode.Pooled)
        {
            return new OwnedAtomicQueue<T>
            {
                m_Queue = AtomicQueue<T>.Create(allocationMode),
                m_PayloadFreeList = new AtomicFreeList<T>(allocationMode),
            };
        }

        /// <summary>
        /// Add an element to the queue
        /// </summary>
        /// <param name="payload">The element to be added</param>
        public void Enqueue(ref T payload)
        {
            m_PayloadFreeList.Acquire(out T * payloadStorage);
            *payloadStorage = payload;
            m_Queue.Enqueue(payloadStorage);
        }

        /// <summary>
        /// Allocates storage for a payload
        /// </summary>
        /// <param name="payload">The allocated payload storage will be placed here</param>
        /// <returns>True if a previous storage allocation was reused, otherwise false</returns>
        public bool AcquirePayloadStorage(out T* payload) => m_PayloadFreeList.Acquire(out payload);

        /// <summary>
        /// Releases storage for a payload previously allocated via AcquirePayloadStorage
        /// </summary>
        /// <param name="payload">The payload to release</param>
        public void ReleasePayloadStorage(T* payload) => m_PayloadFreeList.Release(payload);

        /// <summary>
        /// Try to remove an element from the queue.
        /// </summary>
        /// <param name="result">
        /// If there's an element available, this parameter will be updated to that element.
        /// </param>
        /// <returns>Whether the dequeueing operation succeeded.</returns>
        public bool TryDequeue(out T result)
        {
            if (!m_Queue.TryDequeue(out T * payloadStorage))
            {
                result = default;
                return false;
            }

            result = *payloadStorage;
            m_PayloadFreeList.Release(payloadStorage);
            return true;
        }

        /// <summary>
        /// Remove an element from the queue
        /// </summary>
        /// <returns>A pointer to the next element from the queue</returns>
        /// <exception cref="InvalidOperationException">If the queue is empty</exception>
        public T Dequeue()
        {
            if (TryDequeue(out T result))
                return result;

            ThrowEmptyQueueException();
            return default;
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
        public ref T Peek()
        {
            return ref *m_Queue.Peek();
        }

        /// <summary>
        /// Dispose all resources belonging to the queue
        /// </summary>
        public void Dispose()
        {
            m_Queue.Dispose();
            m_PayloadFreeList.Dispose();
        }

        /// <summary>
        /// Whether this is the same queue as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(OwnedAtomicQueue<T> other)
        {
            return m_Queue.Equals(other.m_Queue);
        }

        /// <summary>
        /// Whether this is the same queue as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is OwnedAtomicQueue<T> other && Equals(other);
        }

        /// <summary>
        /// Return a unique hash for this queue
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return m_Queue.GetHashCode();
        }
    }
}
