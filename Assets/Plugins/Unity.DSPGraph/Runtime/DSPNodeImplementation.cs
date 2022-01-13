using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Media.Utilities;
using UnityEngine.Experimental.Audio;
using Debug = UnityEngine.Debug;

namespace Unity.Audio
{
    public unsafe partial struct DSPNode
    {
        internal Handle Handle;
        internal Handle Graph;

        [NativeDisableUnsafePtrRestriction]
        private readonly int *m_OutputReferences;
        internal int OutputReferences
        {
            get => *m_OutputReferences;
            set => *m_OutputReferences = value;
        }

        internal GrowableBuffer<int> OutputPortReferences;
        internal GrowableBuffer<PortDescription> Inputs;
        internal GrowableBuffer<PortDescription> Outputs;

        [NativeDisableUnsafePtrRestriction]
        private readonly int* m_InputConnectionIndex;
        internal int InputConnectionIndex
        {
            get => *m_InputConnectionIndex;
            set => *m_InputConnectionIndex = value;
        }

        [NativeDisableUnsafePtrRestriction]
        private readonly int* m_OutputConnectionIndex;
        internal int OutputConnectionIndex
        {
            get => *m_OutputConnectionIndex;
            set => *m_OutputConnectionIndex = value;
        }

        private readonly AudioKernelExtensions.ParameterDescriptionData m_ParameterDescriptions;
        internal GrowableBuffer<Parameter> Parameters;
        private GrowableBuffer<GrowableBuffer<SampleProvider>> m_SampleProviders;
        private GrowableBuffer<uint> m_OwnedSampleProviders;

        [NativeDisableUnsafePtrRestriction]
        internal readonly void* JobReflectionData;

        [NativeDisableUnsafePtrRestriction]
        internal readonly void* JobStructData;

        [NativeDisableUnsafePtrRestriction]
        internal readonly ResourceContextNode* ResourceContextHead;

        [NativeDisableUnsafePtrRestriction]
        internal readonly JobData* JobDataBuffer;

        [NativeDisableUnsafePtrRestriction]
        internal readonly JobHandle* JobFence;

        [NativeDisableUnsafePtrRestriction]
        private long* m_JobDataAllocationRoot;

        internal bool IsTopologicalRoot => Valid &&
        OutputConnectionIndex == DSPConnection.InvalidIndex &&
        InputConnectionIndex != DSPConnection.InvalidIndex;

        internal DSPNode(DSPGraph dspGraph, Handle nodeHandle, DSPGraph* unsafeGraphBuffer, void* jobReflectionData, void* persistentStructMemory, AudioKernelExtensions.DSPParameterDescription* descriptions, int parameterCount, AudioKernelExtensions.DSPSampleProviderDescription* dspSampleProviderDescription, int sampleProviderCount)
        {
            Graph = dspGraph.Handle;
            Handle = nodeHandle;
            JobReflectionData = jobReflectionData;
            JobStructData = persistentStructMemory;
            m_ParameterDescriptions = new AudioKernelExtensions.ParameterDescriptionData
            {
                Descriptions = descriptions,
                ParameterCount = parameterCount,
            };


            var batch = new BatchAllocator(Allocator.Persistent);
            fixed(JobData** buffer = &JobDataBuffer) { batch.Allocate(1, buffer); }
            fixed(int** buffer = &m_OutputReferences) { batch.Allocate(1, buffer); }
            fixed(int** buffer = &m_InputConnectionIndex) { batch.Allocate(1, buffer); }
            fixed(int** buffer = &m_OutputConnectionIndex) { batch.Allocate(1, buffer); }
            fixed(long** buffer = &m_JobDataAllocationRoot) { batch.Allocate(1, buffer); }
            fixed(ResourceContextNode** buffer = &ResourceContextHead) { batch.Allocate(1, buffer); }
            fixed(JobHandle** buffer = &JobFence) { batch.Allocate(1, buffer); }
            batch.Dispose();
            *JobDataBuffer = default;
            *m_OutputReferences = 0;
            *m_InputConnectionIndex = DSPConnection.InvalidIndex;
            *m_OutputConnectionIndex = DSPConnection.InvalidIndex;
            *ResourceContextHead = default;
            *JobFence = default;
            *m_JobDataAllocationRoot = 0;

            JobDataBuffer->NativeJobData.GraphIndex = Graph.Id;
            JobDataBuffer->NativeJobData.NodeIndex = Handle.Id;
            JobDataBuffer->NativeJobData.GraphRegistry = unsafeGraphBuffer;
            OutputPortReferences = new GrowableBuffer<int>(Allocator.Persistent);
            Inputs = new GrowableBuffer<PortDescription>(Allocator.Persistent);
            Outputs = new GrowableBuffer<PortDescription>(Allocator.Persistent);
            Parameters = new GrowableBuffer<Parameter>(Allocator.Persistent, Math.Max(parameterCount, 1));
            m_SampleProviders = new GrowableBuffer<GrowableBuffer<SampleProvider>>(Allocator.Persistent, Math.Max(sampleProviderCount, 1));
            m_OwnedSampleProviders = new GrowableBuffer<uint>(Allocator.Persistent);

            for (var i = 0; i < parameterCount; ++i)
                Parameters.Add(new Parameter
                {
                    KeyIndex = DSPParameterKey.NullIndex,
                    Value = descriptions[i].DefaultValue,
                });

            for (var i = 0; i < sampleProviderCount; ++i)
            {
                var subproviderCount = dspSampleProviderDescription[i].m_Size;
                var subproviders = new GrowableBuffer<SampleProvider>(Allocator.Persistent, Math.Max(subproviderCount, 1));

                if (subproviderCount > 0)
                    // Fixed-length array
                    for (int subIndex = 0; subIndex < subproviderCount; ++subIndex)
                        subproviders.Add(default);
                else if (!dspSampleProviderDescription[i].m_IsArray)
                    // Single item
                    subproviders.Add(default);
                // else it's a variable array, and we'll leave it at length 0 until the user adds providers

                m_SampleProviders.Add(subproviders);
            }
        }

        internal void Dispose(DSPGraph graph)
        {
            DeallocateJobData();

            OutputPortReferences.Dispose();
            Inputs.Dispose();
            Outputs.Dispose();

            var parameters = Parameters;
            // Clear parameter keys from graph-global store before disposing parameters
            for (int i = 0; i < parameters.Count; ++i)
                graph.FreeParameterKeys(parameters[i].KeyIndex);
            parameters.Dispose();

            var sampleProviders = m_SampleProviders;
            for (int i = 0; i < sampleProviders.Count; ++i)
                sampleProviders[i].Dispose();
            sampleProviders.Dispose();

            // Release sample providers that we own
            var ownedSampleProviders = m_OwnedSampleProviders;
            for (int i = 0; i < ownedSampleProviders.Count; ++i)
                AudioSampleProvider.InternalRemove(ownedSampleProviders[i]);
            ownedSampleProviders.Dispose();

            // Release and track leaked AudioKernel allocations
            var leakedAllocationCount = 0;
            var node = ResourceContextHead->Next;
            while (node != null)
            {
                ++leakedAllocationCount;
                var next = node->Next;
                // Not going through the allocation tracker here, since these allocations happen on the native side
                // FIXME: The payload was allocated as AudioKernel, but freeing that from here will trigger the resource context validation...
                // Fortunately, Persistent and AudioKernel come from the same native allocator today
                UnsafeUtility.Free(node, Allocator.Persistent);
                node = next;
            }
            if (leakedAllocationCount > 0)
                LogLeakedAllocations(leakedAllocationCount);

            Utility.FreeUnsafe(JobDataBuffer);
        }

        [BurstDiscard]
        static void LogLeakedAllocations(int leakedAllocationCount)
        {
            Debug.LogWarning($"{leakedAllocationCount} leaked DSP node allocations");
        }

        /// <summary>
        /// Allocate job data for this node, including port buffers, parameters, and sample providers
        /// </summary>
        /// <remarks>If the needed size is the same as for the last execution, no reallocation is done</remarks>
        /// <param name="graph">The graph to which this node belongs</param>
        internal void AllocateJobData(DSPGraph graph)
        {
            var sampleFrameCount = graph.LastReadLength;

            CountPortBuffers(graph, sampleFrameCount, out int inputBufferCount, out int inputSampleCount, out int outputBufferCount, out int outputSampleCount);
            CountParameters(out int parameterCount, out int interpolatedParameterCount);
            CountSampleProviders(out int sampleProviderIndexCount, out int sampleProviderCount);

            int paramInterpolationFloatCount = interpolatedParameterCount * sampleFrameCount;
            int floatCount = paramInterpolationFloatCount + inputSampleCount + outputSampleCount;

            if (inputBufferCount == JobDataBuffer->NativeJobData.InputBufferCount &&
                outputBufferCount == JobDataBuffer->NativeJobData.OutputBufferCount &&
                parameterCount == JobDataBuffer->NativeJobData.ParameterCount &&
                sampleProviderIndexCount == JobDataBuffer->NativeJobData.SampleProviderIndexCount &&
                sampleProviderCount == JobDataBuffer->NativeJobData.SampleProviderCount &&
                floatCount == JobDataBuffer->Count)
                // No need to reallocate job data
                return;

            DeallocateJobData();

            // Burst doesn't support the using statement atm
            var allocator = new BatchAllocator(Allocator.Persistent);
            allocator.Allocate(floatCount, &JobDataBuffer->Buffer);
            allocator.Allocate(inputBufferCount, &JobDataBuffer->NativeJobData.InputBuffers);
            allocator.Allocate(outputBufferCount, &JobDataBuffer->NativeJobData.OutputBuffers);
            allocator.Allocate(parameterCount, &JobDataBuffer->NativeJobData.Parameters);
            allocator.Allocate(sampleProviderIndexCount, &JobDataBuffer->NativeJobData.SampleProviderIndices);
            allocator.Allocate(sampleProviderCount, &JobDataBuffer->NativeJobData.SampleProviders);
            allocator.Dispose();
            *m_JobDataAllocationRoot = (long)allocator.AllocationRoot;

            JobDataBuffer->NativeJobData.InputBufferCount = (uint)inputBufferCount;
            JobDataBuffer->NativeJobData.OutputBufferCount = (uint)outputBufferCount;
            JobDataBuffer->NativeJobData.ParameterCount = (uint)parameterCount;
            JobDataBuffer->NativeJobData.SampleProviderIndexCount = (uint)sampleProviderIndexCount;
            JobDataBuffer->NativeJobData.SampleProviderCount = (uint)sampleProviderCount;
            JobDataBuffer->Count = floatCount;
        }

        internal void DeallocateJobData()
        {
            if (*m_JobDataAllocationRoot == 0)
                return;

            Utility.FreeUnsafe((void*)*m_JobDataAllocationRoot);
            *m_JobDataAllocationRoot = 0;
        }

        /// <summary>
        /// Set up "native" job data for execution
        /// </summary>
        /// <param name="graph">The graph to which this node belongs</param>
        /// <param name="unsafeGraphBuffer">A pointer to the global graph array (burst!)</param>
        internal void PrepareJobData(DSPGraph graph, DSPGraph* unsafeGraphBuffer)
        {
            JobDataBuffer->NativeJobData.GraphRegistry = unsafeGraphBuffer;
            JobDataBuffer->NativeJobData.GraphIndex = graph.Handle.Id;
            JobDataBuffer->NativeJobData.NodeIndex = Handle.Id;
            JobDataBuffer->NativeJobData.DSPClock = (ulong)graph.DSPClock;
            JobDataBuffer->NativeJobData.DSPBufferSize = (uint)graph.DSPBufferSize;
            JobDataBuffer->NativeJobData.SampleRate = (uint)graph.SampleRate;
            JobDataBuffer->NativeJobData.SampleReadCount = (uint)graph.LastReadLength;
            JobDataBuffer->NativeJobData.ParameterKeys = *graph.ParameterKeys.UnsafeDataPointer;

            bool isRoot = Equals(graph.RootDSP);
            float* floatBufferStart = isRoot ? *graph.RootBuffer.UnsafeDataPointer : JobDataBuffer->Buffer;
            float* floatCursor = floatBufferStart;
            SetupPortBuffers(graph, graph.LastReadLength, &floatCursor);
            SetupParameters();
            SetupSampleProviders();
        }

        // Entry point for AudioKernel execution
        internal void ExecuteJob(DSPGraph graph)
        {
            graph.ProfilerMarkers.AudioKernelExecuteMarker.Begin();
            DSPGraphInternal.Internal_ExecuteJob(JobStructData, JobReflectionData, JobDataBuffer, ResourceContextHead);
            graph.ProfilerMarkers.AudioKernelExecuteMarker.End();
        }

        // Entry point for AudioKernelUpdate execution
        internal void ExecuteUpdateJob(void* jobStructMemory, void* updateReflectionData, void* jobReflectionData, Handle requestHandle, out JobHandle fence)
        {
            fence = default;
            DSPGraphInternal.Internal_ExecuteUpdateJob(jobStructMemory, updateReflectionData, JobStructData, jobReflectionData, ResourceContextHead, ref requestHandle, ref fence);
        }

        /// <summary>
        /// Clean up job data after execution
        /// </summary>
        /// <remarks>Mainly just reading back interpolated parameter data and freeing expired parameter keys</remarks>
        /// <param name="graph">The graph to which this node belongs</param>
        internal void CleanupJobData(DSPGraph graph)
        {
            var jobParameters = JobDataBuffer->NativeJobData.Parameters;

            for (var i = 0; i < Parameters.Count; ++i)
            {
                // Read back final interpolated parameter values
                Parameters[i] = new Parameter
                {
                    KeyIndex = graph.FreeParameterKeys(jobParameters[i].m_KeyIndex, graph.DSPClock),
                    Value = jobParameters[i].m_Value,
                };
            }
        }

        void CountSampleProviders(out int indexCount, out int providerCount)
        {
            indexCount = m_SampleProviders.Count;

            providerCount = 0;
            for (int i = 0; i < indexCount; ++i)
                providerCount += m_SampleProviders[i].Count;
        }

        void CountParameters(out int parameterCount, out int interpolatedParameterCount)
        {
            parameterCount = Parameters.Count;

            interpolatedParameterCount = 0;
            for (int i = 0; i < parameterCount; i++)
                if (Parameters[i].KeyIndex != DSPParameterKey.NullIndex)
                    ++interpolatedParameterCount;
        }

        void CountPortBuffers(DSPGraph graph, int sampleFrameCount, out int inputBufferCount, out int inputSampleCount, out int outputBufferCount, out int outputSampleCount)
        {
            inputBufferCount = Inputs.Count;
            inputSampleCount = 0;
            for (int i = 0; i < inputBufferCount; ++i)
                inputSampleCount += Inputs[i].Channels * sampleFrameCount;

            outputBufferCount = 0;
            outputSampleCount = 0;

            if (Equals(graph.RootDSP))
            {
                // Let's not allocate output PortBuffer and input/output samples for the root node as
                // they're not used anyways. We do want the input PortBuffer to be created however
                // so MixJobInputs can run on the root node like it does on any other node.
                inputSampleCount = 0;
                return;
            }

            for (int i = 0; i < Outputs.Count; ++i)
            {
                // We only create a buffer if there needs to be one.
                if (OutputPortReferences[i] >= 0)
                {
                    ++outputBufferCount;
                    outputSampleCount += Outputs[i].Channels * sampleFrameCount;
                }
            }
        }

        private void SetupPortBuffers(DSPGraph graph, int sampleFrameCount, float** floatCursor)
        {
            NativeSampleBuffer* inputBuffers = JobDataBuffer->NativeJobData.InputBuffers;
            var inputBuffersCount = JobDataBuffer->NativeJobData.InputBufferCount;

            for (int i = 0; i < inputBuffersCount; i++)
                inputBuffers[i] = new NativeSampleBuffer
                {
                    Channels = (uint)Inputs[i].Channels,
                    Buffer = null,
                };

            // Steal output buffers from the node connected our inputs if we're the only ones using it.
            // Root node doesn't steal output buffers from input nodes, since it mixes into the graph's root buffer.
            if (!Equals(graph.RootDSP))
            {
                for (int inputConnectionIndex = InputConnectionIndex; inputConnectionIndex != DSPConnection.InvalidIndex;)
                {
                    DSPConnection conn = graph.Connections[inputConnectionIndex];
                    var inputPort = conn.InputPort;
                    // See if we've already stolen an output buffer for this input.
                    if (!conn.HasAttenuation && inputBuffers[inputPort].Buffer == null)
                    {
                        DSPNode inputNode = graph.Nodes[conn.OutputNodeIndex];
                        JobData* inputData = inputNode.JobDataBuffer;
                        NativeSampleBuffer* peerOutputs = inputData->NativeJobData.OutputBuffers;
                        NativeSampleBuffer peerOutput = peerOutputs[conn.OutputPort];
                        if (inputNode.OutputPortReferences[conn.OutputPort] == 1)
                        {
                            var inputBuffer = inputBuffers[inputPort];
                            inputBuffer.Buffer = peerOutput.Buffer;
                            inputBuffer.Initialized = true;
                            inputBuffers[inputPort] = inputBuffer;
                            peerOutput.Buffer = null;
                            peerOutputs[conn.OutputPort] = peerOutput;
                        }
                    }

                    inputConnectionIndex = conn.NextInputConnectionIndex;
                }
            }

            for (int i = 0; i < inputBuffersCount; i++)
            {
                NativeSampleBuffer buff = inputBuffers[i];
                if (buff.Buffer != null)
                    continue;

                buff.Buffer = *floatCursor;
                inputBuffers[i] = buff;
                *floatCursor += sampleFrameCount * inputBuffers[i].Channels;
            }

            NativeSampleBuffer* outputBuffers = JobDataBuffer->NativeJobData.OutputBuffers;
            var outputBuffersCount = JobDataBuffer->NativeJobData.OutputBufferCount;
            float* outputStart = *floatCursor;

            for (int i = 0; i < outputBuffersCount; i++)
            {
                PortDescription output = Outputs[i];
                bool initializeBuffer = OutputPortReferences[i] >= 0;

                outputBuffers[i] = new NativeSampleBuffer
                {
                    Channels = (uint)output.Channels,
                    Initialized = initializeBuffer,
                };

                if (initializeBuffer)
                {
                    outputBuffers[i].Buffer = *floatCursor; // We only create a buffer if there needs to be one.
                    *floatCursor += sampleFrameCount * output.Channels;
                }
            }

            var floatsToClearCount = *floatCursor - outputStart;
            if (floatsToClearCount > 0)
                Utility.ClearBuffer(outputStart, 0, floatsToClearCount * UnsafeUtility.SizeOf<float>());
        }

        void SetupParameters()
        {
            NativeDSPParameter* jobParameters = JobDataBuffer->NativeJobData.Parameters;

            for (int i = 0; i < Parameters.Count; i++)
            {
                Parameter param = Parameters[i];
                AudioKernelExtensions.DSPParameterDescription description = m_ParameterDescriptions.Descriptions[i];
                jobParameters[i] = new NativeDSPParameter
                {
                    m_KeyIndex = param.KeyIndex,
                    m_Value = param.Value.x,
                    m_Max = description.Max,
                    m_Min = description.Min,
                };
            }
        }

        void SetupSampleProviders()
        {
            int globalProviderIdx = 0;
            for (var itemIdx = 0; itemIdx < m_SampleProviders.Count; ++itemIdx)
            {
                JobDataBuffer->NativeJobData.SampleProviderIndices[itemIdx] = (m_SampleProviders[itemIdx].Count == 0 ? -1 : globalProviderIdx);
                for (var providerIdx = 0; providerIdx < m_SampleProviders[itemIdx].Count; ++providerIdx, ++globalProviderIdx)
                    JobDataBuffer->NativeJobData.SampleProviders[globalProviderIdx] = m_SampleProviders[itemIdx][providerIdx].ProviderHandle;
            }
        }

        internal void ValidateReflectionData(void* jobReflectionData)
        {
            ValidateReflectionDataMono(jobReflectionData);
            ValidateReflectionDataBurst(jobReflectionData);
        }

        [BurstDiscard]
        internal void ValidateReflectionDataMono(void* jobReflectionData)
        {
            if (jobReflectionData != JobReflectionData)
                throw new InvalidOperationException("Mismatching DSPNode and IAudioKernel");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void ValidateReflectionDataBurst(void* jobReflectionData)
        {
            if (jobReflectionData != JobReflectionData)
                throw new InvalidOperationException("Mismatching DSPNode and IAudioKernel");
        }

        internal void ValidateParameter(int parameter)
        {
            Utility.ValidateIndex(parameter, m_ParameterDescriptions.ParameterCount - 1);
        }

        internal void SetSampleProvider(int providerIndex, int subIndex, uint providerId, bool destroyOnRemove)
        {
            var provider = m_SampleProviders[providerIndex];
            var ownedProviders = m_OwnedSampleProviders;

            if (providerId == SampleProvider.InvalidProviderHandle)
            {
                // Remove old handle if appropriate
                var oldHandle = provider[subIndex].ProviderHandle;
                if (ownedProviders.Remove(oldHandle))
                    AudioSampleProvider.InternalRemove(oldHandle);
            }

            provider[subIndex] = new SampleProvider { ProviderHandle = providerId };
            if (destroyOnRemove)
                ownedProviders.Add(providerId);
        }

        internal void InsertSampleProvider(int providerIndex, int subIndex, uint providerId, bool destroyOnRemove)
        {
            var provider = m_SampleProviders[providerIndex];
            var newProvider = new SampleProvider {ProviderHandle = providerId};

            if (subIndex < 0)
                provider.Add(newProvider);
            else
                provider.Insert(subIndex, newProvider);

            if (destroyOnRemove)
                m_OwnedSampleProviders.Add(providerId);
        }

        internal void RemoveSampleProvider(int providerIndex, int subIndex)
        {
            var provider = m_SampleProviders[providerIndex];
            var oldHandle = provider[subIndex].ProviderHandle;

            if (m_OwnedSampleProviders.Remove(oldHandle))
                AudioSampleProvider.InternalRemove(oldHandle);
            provider.RemoveAt(subIndex);
        }

        // TODO: Remove wrapper struct?
        [StructLayout(LayoutKind.Sequential)]
        internal struct PortDescription
        {
            public int Channels;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Parameter
        {
            public float4 Value;
            public int KeyIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobData
        {
            public AudioKernelExtensions.NativeAudioData NativeJobData;
            public int Count;
            public readonly float* Buffer;
        }

        /// <summary>
        /// This is the head of a linked list of AudioKernel allocations.
        /// We use this to track/release allocations for a DSPNode.
        /// Any allocations made with the AudioKernel allocator during the AudioKernel/AudioKernelUpdate lifecycle will get a node in this list.
        /// AudioKernel allocations made outside of those lifecycle callbacks will error.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ResourceContextNode
        {
            public readonly ResourceContextNode* Next;
            public readonly void* Payload;
        }
    }
}
