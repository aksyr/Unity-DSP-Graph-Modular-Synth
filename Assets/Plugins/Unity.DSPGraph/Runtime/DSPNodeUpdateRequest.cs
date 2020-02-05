using System;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DSPNodeUpdateRequest<TAudioKernelUpdate, TParams, TProvs, TAudioKernel> : IDisposable, IHandle<DSPNodeUpdateRequest<TAudioKernelUpdate, TParams, TProvs, TAudioKernel>>
        where TParams         : unmanaged, Enum
        where TProvs          : unmanaged, Enum
        where TAudioKernel       : struct, IAudioKernel<TParams, TProvs>
        where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParams, TProvs, TAudioKernel>
    {
        private readonly Handle m_Handle;
        private readonly Handle m_Graph;
        internal readonly Handle OwningNode;

        public DSPNodeUpdateRequest(DSPNode node)
        {
            m_Graph = node.Graph;
            m_Handle = DSPGraphExtensions.Lookup(m_Graph).AllocateHandle();
            OwningNode = node.Handle;
        }

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

        public bool Done => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).JobStructData != null;

        public bool HasError => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).HasError;

        public DSPNode Node => DSPGraphExtensions.Lookup(m_Graph).LookupNode(OwningNode);

        public JobHandle Fence => DSPGraphExtensions.Lookup(m_Graph).LookupUpdateRequest(m_Handle).Fence;

        internal Handle Handle => m_Handle;

        public void Dispose()
        {
            DSPGraphExtensions.Lookup(m_Graph).ReleaseUpdateRequest(m_Handle);
        }

        public bool Valid => m_Handle.Valid && m_Graph.Valid;

        public bool Equals(DSPNodeUpdateRequest<TAudioKernelUpdate, TParams, TProvs, TAudioKernel> other)
        {
            return m_Handle.Equals(other.m_Handle) && m_Graph.Equals(other.m_Graph);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPNodeUpdateRequest<TAudioKernelUpdate, TParams, TProvs, TAudioKernel> other && Equals(other);
        }

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
