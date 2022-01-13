using System;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// Handle for managing an update request registered via <see cref="DSPCommandBlock.CreateUpdateRequest{TAudioKernelUpdate,TParameters,TProviders,TAudioKernel}"/>
    /// </summary>
    /// <typeparam name="TAudioKernelUpdate">The type of the <see cref="IAudioKernelUpdate{TParameters,TProviders,TKernel}"/></typeparam>
    /// <typeparam name="TParameters">The parameters for <typeparamref name="TAudioKernel"/></typeparam>
    /// <typeparam name="TProviders">The providers for <typeparamref name="TAudioKernel"/></typeparam>
    /// <typeparam name="TAudioKernel">The audio kernel type for <typeparamref name="TAudioKernelUpdate"/></typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public readonly unsafe struct DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> : IDisposable, IHandle<DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>>
        where TParameters         : unmanaged, Enum
        where TProviders          : unmanaged, Enum
        where TAudioKernel       : struct, IAudioKernel<TParameters, TProviders>
        where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParameters, TProviders, TAudioKernel>
    {
        private readonly Handle m_Handle;
        private readonly Handle m_Graph;
        internal readonly Handle OwningNode;

        internal DSPNodeUpdateRequest(DSPNode node)
        {
            m_Graph = node.Graph;
            m_Handle = DSPGraphExtensions.Lookup(m_Graph).AllocateHandle();
            OwningNode = node.Handle;
        }

        /// <summary>
        /// The <see cref="IAudioKernelUpdate{TParameters,TProviders,TKernel}"/> for this request
        /// </summary>
        public TAudioKernelUpdate UpdateJob
        {
            get
            {
                var description = DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle);
                if (description.JobStructData == null)
                    return default;
                UnsafeUtility.CopyPtrToStructure(description.JobStructData, out TAudioKernelUpdate updateJob);
                return updateJob;
            }
        }

        /// <summary>
        /// Whether the request has been completed
        /// </summary>
        public bool Done => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).JobStructData != null;

        /// <summary>
        /// Whether the request has encountered an error
        /// </summary>
        public bool HasError => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).HasError;

        /// <summary>
        /// The <see cref="DSPNode"/> for the request
        /// </summary>
        public DSPNode Node => DSPGraphExtensions.Lookup(m_Graph).LookupNode(OwningNode);

        /// <summary>
        /// The <see cref="JobHandle"/> associated with the request
        /// </summary>
        public JobHandle Fence => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).Fence;

        internal Handle Handle => m_Handle;

        /// <summary>
        /// Dispose resources associated with the request
        /// </summary>
        public void Dispose()
        {
            DSPGraphExtensions.Lookup(m_Graph).ReleaseUpdateRequest(m_Handle);
        }

        /// <summary>
        /// Whether this is a valid request for a valid graph
        /// </summary>
        public bool Valid => m_Handle.Valid && m_Graph.Valid;

        /// <summary>
        /// Whether this request is the same as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> other)
        {
            return m_Handle.Equals(other.m_Handle) && m_Graph.Equals(other.m_Graph);
        }

        /// <summary>
        /// Whether this request is the same as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> other && Equals(other);
        }

        /// <summary>
        /// Returns a unique hash for this request
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = m_Handle.GetHashCode();
                hashCode = (hashCode * 397) ^ m_Graph.GetHashCode();
                return hashCode;
            }
        }
    }

    internal unsafe struct DSPNodeUpdateRequestDescription
    {
        [NativeDisableUnsafePtrRestriction]
        public void* JobStructData;
        public Handle Node;
        public JobHandle Fence;
        public bool HasError;
        public GCHandle Callback;
        public Handle Handle;
    }
}
