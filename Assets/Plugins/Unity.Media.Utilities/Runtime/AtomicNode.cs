using Unity.Collections;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A node for use in an atomic collection
    /// </summary>
    /// <remarks>This must be external to AtomicFreeList&lt;T&gt; because it must be unmanaged</remarks>
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
}
