using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Media.Utilities;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CompleteCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;

        public void Schedule()
        {
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
            m_Graph.DisposeHandle(m_Handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct CreateDSPNodeCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_NodeHandle;
        internal DSPGraph m_Graph;
        internal void* m_JobStructMemory;
        internal int m_KernelSize;
        internal int m_KernelAlignment;
        internal void* m_JobReflectionData;
        internal AudioKernelExtensions.ParameterDescriptionData m_ParameterDescriptionData;
        internal AudioKernelExtensions.SampleProviderDescriptionData m_SampleProviderDescriptionData;

        public void Schedule()
        {
            var persistentStructMemory = UnsafeUtility.Malloc(m_KernelSize, m_KernelAlignment, Allocator.Persistent);
            UnsafeUtility.MemCpy(persistentStructMemory, m_JobStructMemory, m_KernelSize);

            m_Graph.CreateDSPNode(m_NodeHandle, m_JobReflectionData, persistentStructMemory,
                m_ParameterDescriptionData.Descriptions, m_ParameterDescriptionData.ParameterCount,
                m_SampleProviderDescriptionData.Descriptions, m_SampleProviderDescriptionData.SampleProviderCount);
        }

        public void Cancel()
        {
            m_Graph.DisposeHandle(m_NodeHandle);
        }

        public void Dispose()
        {
            Utility.FreeUnsafe(m_JobStructMemory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SetFloatCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal uint m_Parameter;
        internal uint m_InterpolationLength;
        internal float m_Value;
        internal void* m_JobReflectionData;
        internal AudioKernelExtensions.ParameterDescriptionData m_ParameterDescriptionData;

        public void Schedule()
        {
            if (!m_ParameterDescriptionData.Validate(m_Parameter))
                return;

            m_Graph.SetFloat(m_Node, (int)m_Parameter, m_Value, m_InterpolationLength, m_JobReflectionData);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AddFloatKeyCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal ulong m_DSPClock;
        internal uint m_Parameter;
        internal float m_Value;
        internal void* m_JobReflectionData;
        internal AudioKernelExtensions.ParameterDescriptionData m_ParameterDescriptionData;

        public void Schedule()
        {
            if (!m_ParameterDescriptionData.Validate(m_Parameter))
                return;

            m_Graph.AddFloatKey(m_Node, m_JobReflectionData, (int)m_Parameter, (long)m_DSPClock, math.float4(m_Value), DSPGraph.DSPParameterKeyType.Value);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SustainFloatCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal ulong m_DSPClock;
        internal uint m_Parameter;
        internal void* m_JobReflectionData;
        internal AudioKernelExtensions.ParameterDescriptionData m_ParameterDescriptionData;

        public void Schedule()
        {
            if (!m_ParameterDescriptionData.Validate(m_Parameter))
                return;

            m_Graph.AddFloatKey(m_Node, m_JobReflectionData, (int)m_Parameter, (long)m_DSPClock, 0.0f, DSPGraph.DSPParameterKeyType.Sustain);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct UpdateAudioKernelCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal void* m_JobReflectionData;
        internal void* m_UpdateReflectionData;
        internal void* m_JobStructMemory;

        public void Schedule()
        {
            // Don't need to copy the job struct memory back to persistent here because it gets destroyed as soon as the job has executed
            var graph = m_Graph;
            graph.ExecuteUpdateJob(m_Node, m_JobStructMemory, m_UpdateReflectionData, m_JobReflectionData);
        }

        public void Cancel()
        {
            Utility.FreeUnsafe(m_JobStructMemory);
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct UpdateAudioKernelRequestCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal Handle m_UpdateRequestHandle;
        internal void* m_JobReflectionData;
        internal void* m_UpdateReflectionData;
        internal void* m_JobStructMemory;
        internal GCHandle m_Callback;

        public void Schedule()
        {
            // Don't need to copy the job struct memory back to persistent here because it gets destroyed as soon as the job has executed
            var graph = m_Graph;
            graph.ExecuteUpdateJob(m_Node, m_JobStructMemory, m_UpdateReflectionData, m_JobReflectionData, m_UpdateRequestHandle);
        }

        public void Cancel()
        {
            Utility.FreeUnsafe(m_JobStructMemory);
            m_Graph.DisposeHandle(m_UpdateRequestHandle);
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReleaseDSPNodeCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal Handle m_NodeHandle;

        public void Schedule()
        {
            m_Graph.ScheduleDSPNodeDisposal(m_NodeHandle);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    internal struct ClearDSPNodeCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal DSPNode m_Node;

        public void Schedule()
        {
            m_Node.Dispose(m_Graph);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ConnectCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal Handle m_Source;
        internal Handle m_Destination;
        internal Handle m_Connection;
        internal int m_OutputPort;
        internal int m_InputPort;

        public void Schedule()
        {
            m_Graph.Connect(m_Source, m_OutputPort, m_Destination, m_InputPort, m_Connection);
        }

        public void Cancel()
        {
            m_Graph.DisposeHandle(m_Connection);
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisconnectCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal Handle m_Source;
        internal Handle m_Destination;
        internal int m_OutputPort;
        internal int m_InputPort;

        public void Schedule()
        {
            m_Graph.Disconnect(m_Source, m_OutputPort, m_Destination, m_InputPort);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisconnectByHandleCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;

        public void Schedule()
        {
            m_Graph.Disconnect(m_Connection);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SetAttenuationCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;
        internal float m_Value0;
        internal float m_Value1;
        internal float m_Value2;
        internal float m_Value3;
        internal byte m_Dimension;
        internal uint m_InterpolationLength;

        public void Schedule()
        {
            unsafe
            {
                var buffer = stackalloc float[4];
                buffer[0] = m_Value0;
                buffer[1] = m_Value1;
                buffer[2] = m_Value2;
                buffer[3] = m_Value3;
                m_Graph.SetAttenuation(m_Connection, buffer, m_Dimension, m_InterpolationLength);
            }
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SetAttenuationBufferCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;
        internal float* m_Values;
        internal byte m_Dimension;
        internal uint m_InterpolationLength;

        public void Schedule()
        {
            m_Graph.SetAttenuation(m_Connection, m_Values, m_Dimension, m_InterpolationLength);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AddAttenuationKeyCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;
        internal ulong m_DSPClock;
        internal float m_Value0;
        internal float m_Value1;
        internal float m_Value2;
        internal float m_Value3;
        internal byte m_Dimension;

        public void Schedule()
        {
            unsafe
            {
                var buffer = stackalloc float[4];
                buffer[0] = m_Value0;
                buffer[1] = m_Value1;
                buffer[2] = m_Value2;
                buffer[3] = m_Value3;
                m_Graph.AddAttenuationKey(m_Connection, buffer, m_Dimension, (long)m_DSPClock);
            }
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AddAttenuationKeyBufferCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;
        internal ulong m_DSPClock;
        internal float* m_Values;
        internal byte m_Dimension;

        public void Schedule()
        {
            m_Graph.AddAttenuationKey(m_Connection, m_Values, m_Dimension, (long)m_DSPClock);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SustainAttenuationCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal DSPGraph m_Graph;
        internal Handle m_Connection;
        internal ulong m_DSPClock;

        public void Schedule()
        {
            m_Graph.SustainAttenuation(m_Connection, (long)m_DSPClock);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AddInletPortCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal int m_ChannelCount;

        public void Schedule()
        {
            m_Graph.AddPort(m_Node, m_ChannelCount, DSPGraph.PortType.Inlet);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AddOutletPortCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal DSPGraph m_Graph;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal int m_ChannelCount;

        public void Schedule()
        {
            m_Graph.AddPort(m_Node, m_ChannelCount, DSPGraph.PortType.Outlet);
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SetSampleProviderCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal AudioKernelExtensions.SampleProviderDescriptionData m_SampleProviderDescriptionData;
        internal int m_Item;
        internal int m_Index;
        internal uint m_ProviderId;
        internal bool m_DestroyOnRemove;

        public void Schedule()
        {
            if (!m_SampleProviderDescriptionData.Validate(m_Item))
                return;

            var providerIndex = DSPCommandBlock.GetProviderIndex(m_Item, m_SampleProviderDescriptionData);

            unsafe
            {
                ValidateSampleProviderForSet(m_SampleProviderDescriptionData.Descriptions[providerIndex], m_Index);
            }

            m_Graph.SetSampleProvider(m_Node, providerIndex, m_Index, m_ProviderId, m_DestroyOnRemove);
        }

        public void Cancel()
        {
        }

        // Index validation for fixed-size array items can be performed here. For variable-array,
        // it can only be performed in the job threads, where the array size is known and stable.
        private static void ValidateSampleProviderForSet(AudioKernelExtensions.DSPSampleProviderDescription provider, int index)
        {
            ValidateSampleProviderForSetWithMeaningfulMessages(provider, index);
            ValidateSampleProviderForSetBurst(provider, index);
        }

        [BurstDiscard]
        private static void ValidateSampleProviderForSetWithMeaningfulMessages(AudioKernelExtensions.DSPSampleProviderDescription provider, int index)
        {
            if (provider.m_IsArray && provider.m_Size >= 0 && (provider.m_Size < index || index < 0))
                throw new IndexOutOfRangeException($"Provider index {index} is out of range");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSampleProviderForSetBurst(AudioKernelExtensions.DSPSampleProviderDescription provider, int index)
        {
            if (provider.m_IsArray && provider.m_Size >= 0 && (provider.m_Size < index || index < 0))
                throw new IndexOutOfRangeException("Provider index is out of range");
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InsertSampleProviderCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal AudioKernelExtensions.SampleProviderDescriptionData m_SampleProviderDescriptionData;
        internal int m_Item;
        internal int m_Index;
        internal uint m_ProviderId;
        internal bool m_DestroyOnRemove;

        public void Schedule()
        {
            if (!m_SampleProviderDescriptionData.Validate(m_Item))
                return;

            var providerIndex = DSPCommandBlock.GetProviderIndex(m_Item, m_SampleProviderDescriptionData);

            unsafe
            {
                ValidateSampleProviderForInsert(m_SampleProviderDescriptionData.Descriptions[providerIndex]);
            }

            m_Graph.InsertSampleProvider(m_Node, providerIndex, m_Index, m_ProviderId, m_DestroyOnRemove);
        }

        public void Cancel()
        {
        }

        // Can only insert into variable-size arrays.
        private static void ValidateSampleProviderForInsert(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            ValidateSampleProviderForInsertMono(provider);
            ValidateSampleProviderForInsertBurst(provider);
        }

        [BurstDiscard]
        private static void ValidateSampleProviderForInsertMono(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            if (!provider.m_IsArray || provider.m_Size >= 0)
                throw new ArgumentException("Can only insert into variable-size array.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSampleProviderForInsertBurst(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            if (!provider.m_IsArray || provider.m_Size >= 0)
                throw new ArgumentException("Can only insert into variable-size array.");
        }

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RemoveSampleProviderCommand : IDSPCommand
    {
        internal DSPCommandType m_Type;
        internal Handle m_Handle;
        internal Handle m_Node;
        internal DSPGraph m_Graph;
        internal AudioKernelExtensions.SampleProviderDescriptionData m_SampleProviderDescriptionData;
        internal int m_Item;
        internal int m_Index;

        public void Schedule()
        {
            if (!m_SampleProviderDescriptionData.Validate(m_Item))
                return;

            unsafe
            {
                ValidateSampleProviderForRemove(m_SampleProviderDescriptionData.Descriptions[m_Item]);
            }

            m_Graph.RemoveSampleProvider(m_Node, m_Item, m_Index);
        }

        public void Cancel()
        {
        }

        // Can only remove from variable-size arrays.
        private static void ValidateSampleProviderForRemove(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            ValidateSampleProviderForRemoveMono(provider);
            ValidateSampleProviderForRemoveBurst(provider);
        }

        [BurstDiscard]
        private static void ValidateSampleProviderForRemoveMono(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            if (!provider.m_IsArray || provider.m_Size >= 0)
                throw new ArgumentException("Can only remove sample providers from variable-size array");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSampleProviderForRemoveBurst(AudioKernelExtensions.DSPSampleProviderDescription provider)
        {
            if (!provider.m_IsArray || provider.m_Size >= 0)
                throw new ArgumentException("Can only remove sample providers from variable-size array");
        }

        public void Dispose()
        {
        }
    }

    internal enum DSPCommandType
    {
        Invalid,
        Complete,
        CreateDSPNode,
        SetFloat,
        AddFloatKey,
        SustainFloat,
        UpdateAudioKernel,
        UpdateAudioKernelRequest,
        ReleaseDSPNode,
        ClearDSPNode,
        Connect,
        Disconnect,
        DisconnectByHandle,
        SetAttenuation,
        SetAttenuationBuffer,
        AddAttenuationKey,
        AddAttenuationKeyBuffer,
        SustainAttenuation,
        AddInletPort,
        AddOutletPort,
        SetSampleProvider,
        InsertSampleProvider,
        RemoveSampleProvider,
    }
}
