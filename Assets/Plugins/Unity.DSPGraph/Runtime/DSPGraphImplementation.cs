using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Media.Utilities;
using Debug = UnityEngine.Debug;

namespace Unity.Audio
{
    // Internal implementation details have been separated to this partial for now
    public unsafe partial struct DSPGraph
    {
        internal readonly Handle Handle;
        private readonly DSPNode m_RootNode;

        private AtomicFreeList<Handle.Node> m_HandleAllocator;
        private AtomicFreeList<ClearDSPNodeCommand> m_CommandAllocator;
        private AtomicFreeList<DSPNode> m_TemporaryNodeAllocator;
        private AtomicQueue<GrowableBuffer<IntPtr>> m_Commands;
        private GrowableBuffer<DSPNodeUpdateRequestDescription> m_UpdateRequestHandles;
        private GrowableBuffer<int> m_OwnedSampleProviders;
        private GrowableBuffer<int> m_GraphTraversal;
        private GrowableBuffer<IntPtr> m_TraversalDependencies;
        private GrowableBuffer<DSPGraphExecutionNode> m_ExecutionBuffer;
        private GrowableBuffer<int> m_TopologicalRoots;
        private AtomicFreeList<GrowableBuffer<IntPtr>> m_CommandBufferAllocator;
        private AtomicQueue<DSPNode> m_NodesPendingDisposal;

        internal AtomicFreeList<EventHandlerDescription> EventHandlerAllocator;
        internal AtomicQueue<EventHandlerDescription> MainThreadCallbacks;
        internal GrowableBuffer<DSPNode> Nodes;
        internal GrowableBuffer<DSPConnection> Connections;
        internal GrowableBuffer<DSPParameterKey> ParameterKeys;
        internal GrowableBuffer<float> RootBuffer;
        internal GrowableBuffer<EventHandlerDescription> EventHandlers;

        private readonly int m_SampleRate;
        private readonly int m_DSPBufferSize;
        private readonly int m_OutputChannelCount;
        private readonly SoundFormat m_OutputFormat;

        internal delegate void Trampoline(ref DSPGraph graph);

        [NativeDisableUnsafePtrRestriction]
        private readonly IntPtr m_DisposeFunctionPointer;

        private bool IsDriven { get; }

        [NativeDisableUnsafePtrRestriction]
        private readonly DSPGraph** m_UnsafeGraphBuffer;

        [NativeDisableUnsafePtrRestriction]
        private readonly long* m_DSPClock;

        [NativeDisableUnsafePtrRestriction]
        private readonly int* m_LastReadLength;
        internal int LastReadLength => *m_LastReadLength;

        internal ProfilerMarkers ProfilerMarkers;

        DSPGraph(SoundFormat outputFormat, int outputChannels, int dspBufferSize, int sampleRate)
        {
            DSPGraphExtensions.Initialize();
            m_SampleRate = sampleRate;
            m_DSPBufferSize = dspBufferSize;
            m_OutputChannelCount = outputChannels;
            m_OutputFormat = outputFormat;
            m_DSPClock = Utility.AllocateUnsafe<long>();
            *m_DSPClock = 0;
            m_LastReadLength = Utility.AllocateUnsafe<int>();
            *m_LastReadLength = 0;
            IsDriven = false;
            m_RootNode = default;
            m_DisposeFunctionPointer = Marshal.GetFunctionPointerForDelegate(DSPGraphExtensions.DisposeMethod);
            ProfilerMarkers = ProfilerMarkers.Create();

            m_HandleAllocator = new AtomicFreeList<Handle.Node>(AllocationMode.Pooled);
            m_CommandAllocator = new AtomicFreeList<ClearDSPNodeCommand>(AllocationMode.Pooled);
            m_TemporaryNodeAllocator = new AtomicFreeList<DSPNode>(AllocationMode.Pooled);
            EventHandlerAllocator = new AtomicFreeList<EventHandlerDescription>(AllocationMode.Pooled);
            m_CommandBufferAllocator = new AtomicFreeList<GrowableBuffer<IntPtr>>(AllocationMode.Pooled);

            m_Commands = AtomicQueue<GrowableBuffer<IntPtr>>.Create();
            MainThreadCallbacks = AtomicQueue<EventHandlerDescription>.Create();
            m_NodesPendingDisposal = AtomicQueue<DSPNode>.Create();

            Nodes = new GrowableBuffer<DSPNode>(Allocator.Persistent);
            Connections = new GrowableBuffer<DSPConnection>(Allocator.Persistent);
            ParameterKeys = new GrowableBuffer<DSPParameterKey>(Allocator.Persistent);
            m_UpdateRequestHandles = new GrowableBuffer<DSPNodeUpdateRequestDescription>(Allocator.Persistent);
            m_OwnedSampleProviders = new GrowableBuffer<int>(Allocator.Persistent);
            RootBuffer = new GrowableBuffer<float>(Allocator.Persistent, dspBufferSize * outputChannels);
            m_GraphTraversal = new GrowableBuffer<int>(Allocator.Persistent);
            m_TraversalDependencies = new GrowableBuffer<IntPtr>(Allocator.Persistent);
            EventHandlers = new GrowableBuffer<EventHandlerDescription>(Allocator.Persistent);
            m_ExecutionBuffer = new GrowableBuffer<DSPGraphExecutionNode>(Allocator.Persistent);
            m_TopologicalRoots = new GrowableBuffer<int>(Allocator.Persistent);
            m_UnsafeGraphBuffer = DSPGraphExtensions.UnsafeGraphBuffer;

            //Handle.Node* node = m_HandleAllocator.Acquire();
            Handle.Node* node = null;
            m_HandleAllocator.Acquire(out node);
            node->Id = Handle.Node.InvalidId;
            Handle = new Handle(node);
            node->Id = DSPGraphExtensions.FindFreeGraphIndex();
            // All fields are now assigned

            // Create root node
            using (var block = CreateCommandBlock())
            {
                m_RootNode = block.CreateDSPNode<UpdateDSPClockKernel.NoParameters, UpdateDSPClockKernel.NoProviders, UpdateDSPClockKernel>();
                var updateKernel = new InitializeDSPClockKernel
                {
                    DSPClock  = m_DSPClock,
                };
                block.UpdateAudioKernel<InitializeDSPClockKernel, UpdateDSPClockKernel.NoParameters, UpdateDSPClockKernel.NoProviders, UpdateDSPClockKernel>(updateKernel, m_RootNode);
            }

            ApplyScheduledCommands();
            m_RootNode = Nodes[0];

            // Root node gets an input port by default
            AddPort(m_RootNode.Handle, outputChannels, PortType.Inlet);
        }

        [MonoPInvokeCallback(typeof(Trampoline))]
        internal static void DoDispose(ref DSPGraph graph)
        {
            try
            {
                graph.Dispose(DisposeBehavior.RunDisposeJobs);
            }
            catch (Exception exception)
            {
                // Don't throw exceptions back to burst
                Debug.LogException(exception);
            }
        }

        internal void Dispose(DisposeBehavior disposeBehavior)
        {
            if (disposeBehavior == DisposeBehavior.RunDisposeJobs)
            {
                // Execute pending command blocks
                ApplyScheduledCommands();

                // Execute callbacks from pending command blocks
                Update();

                // Callbacks may schedule one more round of commands
                ApplyScheduledCommands();
            }

            CleanupRemainingNodes(disposeBehavior);
            InternalDispose(disposeBehavior);
        }

        void CleanupRemainingNodes(DisposeBehavior disposeBehavior)
        {
            // Clean up dangling nodes
            DisposeDSPNode(m_RootNode.Handle, DisposeBehavior.DeallocateOnly);

            var leakedNodeCount = 0;
            // Skip root node
            for (int i = 1; i < Nodes.Count; ++i)
            {
                var node = Nodes[i];
                if (node.Valid)
                {
                    ++leakedNodeCount;
                    DisposeDSPNode(node.Handle, disposeBehavior);
                }
            }

            LogLeakedDSPNodes(leakedNodeCount);

            if (disposeBehavior == DisposeBehavior.RunDisposeJobs)
            {
                // Dispose jobs give us one more set of callbacks
                Update();
                ApplyScheduledCommands();
            }
        }

        // This is here to keep allocation/disposition of internals together
        void InternalDispose(DisposeBehavior disposeBehavior)
        {
            this.Unregister();
            Utility.FreeUnsafe(m_DSPClock);
            Utility.FreeUnsafe(m_LastReadLength);

            Nodes.Dispose();

            // Dangling connection handles need to be released
            for (int i = 0; i < Connections.Count; ++i)
                if (Connections[i].Valid)
                    DisposeHandle(Connections[i].Handle);
            Connections.Dispose();

            // Dangling update requests need to be released
            for (int i = 0; i < m_UpdateRequestHandles.Count; ++i)
                if (m_UpdateRequestHandles[i].Handle.Valid)
                    ReleaseUpdateRequest(m_UpdateRequestHandles[i].Handle);
            m_UpdateRequestHandles.Dispose();

            m_Commands.Dispose();
            MainThreadCallbacks.Dispose();
            m_NodesPendingDisposal.Dispose();
            ParameterKeys.Dispose();
            m_OwnedSampleProviders.Dispose();
            RootBuffer.Dispose();
            m_GraphTraversal.Dispose();
            m_TraversalDependencies.Dispose();
            EventHandlers.Dispose();
            m_ExecutionBuffer.Dispose();
            m_TopologicalRoots.Dispose();
            m_CommandAllocator.Dispose();
            m_TemporaryNodeAllocator.Dispose();
            EventHandlerAllocator.Dispose();
            m_CommandBufferAllocator.Dispose();
            DisposeHandle(Handle);
            m_HandleAllocator.Dispose();
        }

        [BurstDiscard]
        private static void LogLeakedDSPNodes(int leakedNodeCount)
        {
            if (leakedNodeCount > 0)
                Debug.LogWarning($"Destroyed {leakedNodeCount} DSPNodes");
        }

        /// <summary>
        /// Allocate a new handle from the graph's free list
        /// </summary>
        /// <returns>A new handle</returns>
        internal Handle AllocateHandle()
        {
            //Handle.Node* node = m_HandleAllocator.Acquire();
            Handle.Node* node = null;
            m_HandleAllocator.Acquire(out node);
            node->Id = Handle.Node.InvalidId;
            return new Handle(node);
        }

        internal T* AllocateCommand<T>()
            where T : unmanaged, IDSPCommand
        {
            ValidateCommandAllocation<T>();
            //return (T*)m_CommandAllocator.Acquire();
            ClearDSPNodeCommand* cmd = null;
            m_CommandAllocator.Acquire(out cmd);
            return (T*)cmd;
        }

        private static void ValidateCommandAllocation<T>()
            where T : unmanaged
        {
            ValidateCommandAllocationWithMeaningfulMessage<T>();
            ValidateCommandAllocationBurst<T>();
        }

        [BurstDiscard]
        private static void ValidateCommandAllocationWithMeaningfulMessage<T>()
            where T : unmanaged
        {
            if (UnsafeUtility.SizeOf<T>() > UnsafeUtility.SizeOf<ClearDSPNodeCommand>())
                throw new ArgumentException($"Size of {typeof(T).FullName} is larger than allowed maximum {UnsafeUtility.SizeOf<ClearDSPNodeCommand>()}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateCommandAllocationBurst<T>()
            where T : unmanaged
        {
            if (UnsafeUtility.SizeOf<T>() > UnsafeUtility.SizeOf<ClearDSPNodeCommand>())
                throw new ArgumentException("Command size is too large");
        }

        internal void ReleaseCommand(void* commandPointer)
        {
            DSPCommand.Dispose(commandPointer);
            ClearDSPNodeCommand* command = (ClearDSPNodeCommand*)commandPointer;
            *command = default;
            m_CommandAllocator.Release(command);
        }

        /// <summary>
        /// Release a handle to the graph's free list
        /// </summary>
        /// <param name="handle">The handle to release</param>
        internal void DisposeHandle(Handle handle)
        {
            handle.FlushNode();
            m_HandleAllocator.Release(handle.AtomicNode);
        }

        /// <summary>
        /// Add a set of commands to the graph's command queue (any thread)
        /// </summary>
        /// <param name="commands">The commands to add (IntPtrs that are really DSPCommand*)</param>
        internal void ScheduleCommandBuffer(GrowableBuffer<IntPtr> commands)
        {
            //var description = m_CommandBufferAllocator.Acquire();
            //*description = commands;
            //m_Commands.Enqueue(description);
            GrowableBuffer<IntPtr>* description = null;
            m_CommandBufferAllocator.Acquire(out description);
            *description = commands;
            m_Commands.Enqueue(description);
        }

        /// <summary>
        /// Execute the pending commands from the graph's command queue (mixer thread only)
        /// </summary>
        internal void ApplyScheduledCommands()
        {
            while (m_Commands.TryDequeue(out var commandBuffer))
            {
                for (var i = 0; i < commandBuffer->Count; ++i)
                {
                    var command = (DSPCommand*)(*commandBuffer)[i];
                    DSPCommand.Schedule(command);
                    ReleaseCommand(command);
                }
                commandBuffer->Dispose();
                m_CommandBufferAllocator.Release(commandBuffer);
            }
        }

        // These could all be generalized with loader and attenuator functions, but how will that affect performance?
        // e.g. Attenuate(float* source, float* destination, float4 attenuation, int frames, int channels, Func<float, IntPtr, int> LoadDestination, Func<float, float4, int> GetAttenuation)
        static void Attenuate(float* source, float* destination, float4 attenuation, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            int bufferIndex = 0;
            for (int channel = 0; channel < channels; ++channel)
                for (int frame = 0; frame < frames; ++frame, ++bufferIndex)
                    destination[bufferIndex] = destination[bufferIndex] + (source[bufferIndex] * attenuation[channel % 4]);
        }

        static void AttenuateAndClear(float* source, float* destination, float4 attenuation, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            int bufferIndex = 0;
            for (int channel = 0; channel < channels; ++channel)
                for (int frame = 0; frame < frames; ++frame, ++bufferIndex)
                    destination[bufferIndex] = source[bufferIndex] * attenuation[channel % 4];
        }

        private void ApplyInterpolatedAttenuation(DSPConnection connection, float* source, float* destination, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            DSPParameterKey* keys = *ParameterKeys.UnsafeDataPointer;
            var attenuation = connection.Attenuation;
            var attenuationValues = stackalloc float4[frames];

            for (int frame = 0; frame < frames; ++frame)
            {
                attenuationValues[frame] = DSPParameterInterpolator.Generate(frame, keys, attenuation.KeyIndex,
                    DSPClock, DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, attenuation.Value);
                destination[frame] = destination[frame] + (source[frame] * attenuationValues[frame][0]);
            }

            for (int channel = 1, baseFrame = frames; channel < channels; ++channel, baseFrame += frames)
            {
                for (int frame = 0; frame < frames; ++frame)
                {
                    var bufferIndex = baseFrame + frame;
                    destination[bufferIndex] = destination[bufferIndex] + (source[bufferIndex] * attenuationValues[frame][channel % 4]);
                }
            }

            // Update attenuation with last interpolated value
            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = FreeParameterKeys(attenuation.KeyIndex, DSPClock + frames),
                Value = attenuationValues[frames - 1],
            };
            Connections[connection.Handle.Id] = connection;
        }

        private void ApplyInterpolatedAttenuationAndClear(DSPConnection connection, float* source, float* destination, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            DSPParameterKey* keys = *ParameterKeys.UnsafeDataPointer;
            var attenuation = connection.Attenuation;
            var attenuationValues = stackalloc float4[frames];

            for (int frame = 0; frame < frames; ++frame)
            {
                attenuationValues[frame] = DSPParameterInterpolator.Generate(frame, keys, attenuation.KeyIndex,
                    DSPClock, DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, attenuation.Value);
                destination[frame] = source[frame] * attenuationValues[frame][0];
            }

            for (int channel = 1, baseFrame = frames; channel < channels; ++channel, baseFrame += frames)
            {
                for (int frame = 0; frame < frames; ++frame)
                {
                    var bufferIndex = baseFrame + frame;
                    destination[bufferIndex] = source[bufferIndex] * attenuationValues[frame][channel % 4];
                }
            }

            // Update attenuation with last interpolated value
            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = FreeParameterKeys(attenuation.KeyIndex, DSPClock + frames),
                Value = attenuationValues[frames - 1],
            };
            Connections[connection.Handle.Id] = connection;
        }

        bool MixAndCleanOutputIntoTargetBuffer(NativeSampleBuffer* peerOutputs, DSPConnection inputConnection, float* targetBuffer, int channels, int sampleFrameCount, bool clear)
        {
            if (peerOutputs == null)
                return false;

            float* sourceBuffer = peerOutputs[inputConnection.OutputPort].Buffer;

            // If source buffer is null, it means we've stolen it
            // so we can return true to indicate our target buffer is cleared.
            if (sourceBuffer == null)
                return true;

            var attenuation = inputConnection.Attenuation;
            if (attenuation.KeyIndex == DSPParameterKey.NullIndex)
                if (clear)
                    AttenuateAndClear(sourceBuffer, targetBuffer, attenuation.Value, sampleFrameCount, channels);
                else
                    Attenuate(sourceBuffer, targetBuffer, attenuation.Value, sampleFrameCount, channels);
            else if (clear)
                ApplyInterpolatedAttenuationAndClear(inputConnection, sourceBuffer, targetBuffer, sampleFrameCount, channels);
            else
                ApplyInterpolatedAttenuation(inputConnection, sourceBuffer, targetBuffer, sampleFrameCount, channels);
            return true;
        }

        void MixJobInputs(DSPNode node)
        {
            ProfilerMarkers.MixNodeInputsMarker.Begin();
            var inputBuffers = node.JobDataBuffer->NativeJobData.InputBuffers;
            var sampleFrameCount = node.JobDataBuffer->NativeJobData.SampleReadCount;

            for (var inputConnectionIndex = node.InputConnectionIndex; inputConnectionIndex != DSPConnection.InvalidIndex;)
            {
                var connection = Connections[inputConnectionIndex];
                var inputNode = Nodes[connection.OutputNodeIndex];
                var inputData = inputNode.JobDataBuffer;

                var inputPortIndex = connection.InputPort;
                var inputBuffer = inputBuffers[inputPortIndex];
                float* targetBuffer = inputBuffer.Buffer;

                if (MixAndCleanOutputIntoTargetBuffer(inputData->NativeJobData.OutputBuffers, connection,
                    targetBuffer, (int)inputBuffer.Channels, (int)sampleFrameCount, !inputBuffer.Initialized))
                {
                    inputBuffer.Initialized = true;
                    inputBuffers[inputPortIndex] = inputBuffer;
                }

                inputConnectionIndex = connection.NextInputConnectionIndex;
            }

            var inputCount = node.JobDataBuffer->NativeJobData.InputBufferCount;
            for (UInt32 i = 0; i < inputCount; ++i)
            {
                var inputBuffer = inputBuffers[i];
                if (inputBuffer.Initialized)
                    continue;
                Utility.ClearBuffer(inputBuffer.Buffer, 0, sampleFrameCount * inputBuffer.Channels * UnsafeUtility.SizeOf<float>());
                inputBuffer.Initialized = true;
                inputBuffers[i] = inputBuffer;
            }
            ProfilerMarkers.MixNodeInputsMarker.End();
        }

        /// <summary>
        /// The entry point for a node job
        /// </summary>
        /// <remarks>Bursted</remarks>
        /// <param name="node">The node whose job will be executed</param>
        internal void RunJobForNode(DSPNode node)
        {
            ProfilerMarkers.AudioKernelExecutionWrapperMarker.Begin();
            node.AllocateJobData(this);
            node.PrepareJobData(this, *m_UnsafeGraphBuffer);
            MixJobInputs(node);
            node.ExecuteJob(this);
            node.CleanupJobData(this);
            ProfilerMarkers.AudioKernelExecutionWrapperMarker.End();
        }

        void BuildTraversalCache(ExecutionMode executionMode)
        {
            var dependencyCount = 0;
            m_GraphTraversal.CheckCapacity(Nodes.Count);

            // Clear port reference counts
            for (int n = 0; n < Nodes.Count; n++)
            {
                var node = Nodes[n];
                if (!node.Valid)
                    continue;

                for (int i = 0; i < node.Outputs.Count; i++)
                    node.OutputPortReferences[i] = 0;

                node.OutputReferences = 0;
            }

            // Put unattached subtrees into the traversal array before the root node
            // so that their job fences are set up when it's time to set up the root node dependencies
            if (EnumHasFlags(executionMode, ExecutionMode.ExecuteNodesWithNoOutputs))
            {
                m_TopologicalRoots.Clear();
                for (int i = 1; i < Nodes.Count; ++i)
                {
                    if (Nodes[i].Valid && Nodes[i].IsTopologicalRoot)
                    {
                        m_TopologicalRoots.Add(i);
                        dependencyCount += BuildTraversalCacheRecursive(Nodes, Connections, m_GraphTraversal, i, 0);
                    }
                }
            }

            dependencyCount += BuildTraversalCacheRecursive(Nodes, Connections, m_GraphTraversal, 0, 0);
            m_TraversalDependencies.CheckCapacity(dependencyCount + m_GraphTraversal.Count);
        }

        private int BuildTraversalCacheRecursive(GrowableBuffer<DSPNode> nodes, GrowableBuffer<DSPConnection> connections, GrowableBuffer<int> graphTraversal, int nodeIndex, int portIndex)
        {
            var node = nodes[nodeIndex];

            if (node.OutputPortReferences.Count > 0)
                node.OutputPortReferences[portIndex]++;
            if (node.OutputReferences++ > 0)
                return 0;

            var subConnectionCount = 0;
            for (var connectionIndex = node.InputConnectionIndex; connectionIndex != DSPConnection.InvalidIndex; ++subConnectionCount)
            {
                var conn = connections[connectionIndex];
                subConnectionCount += BuildTraversalCacheRecursive(nodes, connections, graphTraversal, conn.OutputNodeIndex, conn.OutputPort);
                connectionIndex = conn.NextInputConnectionIndex;
            }

            graphTraversal.Add(nodeIndex);
            return subConnectionCount;
        }

        private void SyncPreviousMix()
        {
            DSPGraphInternal.Internal_SyncFenceNoWorkSteal(*m_RootNode.JobFence);
        }

        private void BeginMixSynchronous()
        {
            for (int i = 0; i < m_GraphTraversal.Count; ++i)
                RunJobForNode(Nodes[m_GraphTraversal[i]]);
        }

        private void BeginMixJobified(ExecutionMode executionMode)
        {
            m_ExecutionBuffer.Clear();
            m_ExecutionBuffer.CheckCapacity(m_GraphTraversal.Count);
            m_TraversalDependencies.Clear();

            for (int i = 0; i < m_GraphTraversal.Count; ++i)
            {
                var node = Nodes[m_GraphTraversal[i]];
                var parentIndex = m_TraversalDependencies.Count;
                m_TraversalDependencies.Add((IntPtr)node.JobFence);
                var inputConnectionIndex = node.InputConnectionIndex;
                while (inputConnectionIndex != DSPConnection.InvalidIndex)
                {
                    var conn = Connections[inputConnectionIndex];
                    m_TraversalDependencies.Add((IntPtr)Nodes[conn.OutputNodeIndex].JobFence);
                    inputConnectionIndex = conn.NextInputConnectionIndex;
                }

                if (EnumHasFlags(executionMode, ExecutionMode.ExecuteNodesWithNoOutputs) && m_RootNode.Equals(node))
                    // Add topological roots as dependencies to the root node so that sync works properly
                    for (int rootIndex = 0; rootIndex < m_TopologicalRoots.Count; ++rootIndex)
                        m_TraversalDependencies.Add((IntPtr)Nodes[m_TopologicalRoots[rootIndex]].JobFence);

                m_ExecutionBuffer.Add(new DSPGraphExecutionNode
                {
                    JobData = node.JobDataBuffer,
                    JobStructData = node.JobStructData,
                    ReflectionData = node.JobReflectionData,
                    ResourceContext = node.ResourceContextHead,
                    FunctionIndex = 0,
                    FenceIndex = parentIndex,
                    FenceCount = m_TraversalDependencies.Count - parentIndex,
                });
            }

            // TODO: Job batch scope
            DSPGraphInternal.Internal_ScheduleGraph(default, *m_ExecutionBuffer.UnsafeDataPointer, m_ExecutionBuffer.Count, null, *m_TraversalDependencies.UnsafeDataPointer);
        }

        private static void ValidateExecutionMode(ExecutionMode mode)
        {
            ValidateExecutionModeMono(mode);
            ValidateExecutionModeBurst(mode);
        }

        [BurstDiscard]
        private static void ValidateExecutionModeMono(ExecutionMode mode)
        {
            if (EnumHasFlags(mode, ExecutionMode.Jobified) == EnumHasFlags(mode, ExecutionMode.Synchronous))
                throw new ArgumentException("Execution mode must contain exactly one of: Jobified, Synchronous");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateExecutionModeBurst(ExecutionMode mode)
        {
            if (EnumHasFlags(mode, ExecutionMode.Jobified) == EnumHasFlags(mode, ExecutionMode.Synchronous))
                throw new ArgumentException("Execution mode must contain exactly one of: Jobified, Synchronous");
        }

        internal void CreateDSPNode(Handle nodeHandle, void* jobReflectionData, void* persistentStructMemory, AudioKernelExtensions.DSPParameterDescription* descriptions, int parameterCount, AudioKernelExtensions.DSPSampleProviderDescription* dspSampleProviderDescription, int sampleProviderCount)
        {
            nodeHandle.Id = FindFreeNodeIndex();
            var node = new DSPNode(this, nodeHandle, *m_UnsafeGraphBuffer, jobReflectionData, persistentStructMemory, descriptions, parameterCount, dspSampleProviderDescription, sampleProviderCount);
            Nodes[nodeHandle.Id] = node;
            ProfilerMarkers.AudioKernelInitializeMarker.Begin();
            DSPGraphInternal.Internal_InitializeJob(persistentStructMemory, jobReflectionData, node.ResourceContextHead);
            ProfilerMarkers.AudioKernelInitializeMarker.End();
        }

        private int FindFreeNodeIndex()
        {
            for (int i = 0; i < Nodes.Count; ++i)
                if (!Nodes[i].Valid)
                    return i;
            Nodes.Add(default);
            return Nodes.Count - 1;
        }

        internal void ReleaseDSPNode(Handle nodeHandle)
        {
            var id = nodeHandle.Id;
            DisposeHandle(nodeHandle);
            Nodes[id] = default;
            TrashTraversalCache();
        }

        internal void ScheduleDSPNodeDisposal(Handle nodeHandle)
        {
            var node = Nodes[nodeHandle.Id];

            // Disconnect connections before disposing
            while (node.InputConnectionIndex != DSPConnection.InvalidIndex)
                Disconnect(Connections[node.InputConnectionIndex]);
            while (node.OutputConnectionIndex != DSPConnection.InvalidIndex)
                Disconnect(Connections[node.OutputConnectionIndex]);

            ReleaseDSPNode(nodeHandle);

            //DSPNode* temporaryNode = m_TemporaryNodeAllocator.Acquire();
            m_TemporaryNodeAllocator.Acquire(out DSPNode* temporaryNode);
            *temporaryNode = node;
            m_NodesPendingDisposal.Enqueue(temporaryNode);
        }

        internal void RunDSPNodeDisposeJob(DSPNode node)
        {
            ProfilerMarkers.AudioKernelDisposeMarker.Begin();
            DSPGraphInternal.Internal_DisposeJob(node.JobStructData, node.JobReflectionData, node.ResourceContextHead);
            ProfilerMarkers.AudioKernelDisposeMarker.End();
            using (var block = CreateCommandBlock())
                block.ClearDSPNode(node);
        }

        private void DisposeDSPNode(Handle nodeHandle, DisposeBehavior disposeBehavior)
        {
            if (disposeBehavior == DisposeBehavior.RunDisposeJobs)
                ScheduleDSPNodeDisposal(nodeHandle);
            else
            {
                var node = Nodes[nodeHandle.Id];
                ReleaseDSPNode(nodeHandle);
                node.Dispose(this);
            }
        }

        internal DSPNode LookupNode(int index)
        {
            return Nodes[index];
        }

        internal DSPNode LookupNode(Handle node)
        {
            return LookupNode(node.Id);
        }

        internal int FreeParameterKeys(int keyIndex, long upperClockLimit = long.MaxValue)
        {
            while (keyIndex != DSPParameterKey.NullIndex)
            {
                var key = ParameterKeys[keyIndex];
                if (key.DSPClock > upperClockLimit)
                    return keyIndex;
                var oldKeyIndex = keyIndex;
                keyIndex = key.NextKeyIndex;
                ParameterKeys[oldKeyIndex] = DSPParameterKey.Default;
            }

            return DSPParameterKey.NullIndex;
        }

        internal void SetFloat(Handle nodeHandle, int parameter, float value, uint interpolationLength, void* jobReflectionData)
        {
            var node = Nodes[nodeHandle.Id];
            node.Validate();
            node.ValidateReflectionData(jobReflectionData);
            node.ValidateParameter(parameter);
            SetParameter(node, parameter, math.float4(value), interpolationLength);
        }

        void SetParameter(DSPNode node, int parameterIndex, float4 value, uint interpolationLength)
        {
            var parameter = node.Parameters[parameterIndex];

            FreeParameterKeys(parameter.KeyIndex);

            if (interpolationLength == 0)
                parameter = new DSPNode.Parameter
                {
                    KeyIndex = DSPParameterKey.NullIndex,
                    Value = value,
                };
            else
                parameter.KeyIndex = AppendKey(DSPParameterKey.NullIndex, DSPParameterKey.NullIndex, Math.Max(interpolationLength + DSPClock, 1) - 1, value);
            node.Parameters[parameterIndex] = parameter;
        }

        internal int AppendKey(int keyIndex, int afterKeyIndex, long dspClock, float4 value)
        {
            ValidateKeyIndicesForAppend(keyIndex, afterKeyIndex);

            var newIndex = FindFreeParameterKeyIndex();
            ParameterKeys[newIndex] = new DSPParameterKey
            {
                InUse = true,
                DSPClock = dspClock,
                NextKeyIndex = DSPParameterKey.NullIndex,
                Value = value,
            };

            if (afterKeyIndex != DSPParameterKey.NullIndex)
            {
                var afterKey = ParameterKeys[afterKeyIndex];
                afterKey.NextKeyIndex = newIndex;
                ParameterKeys[afterKeyIndex] = afterKey;
            }

            return newIndex;
        }

        private void ValidateKeyIndicesForAppend(int keyIndex, int afterKeyIndex)
        {
            ValidateKeyIndicesForAppendMono(keyIndex, afterKeyIndex);
            ValidateKeyIndicesForAppendBurst(keyIndex, afterKeyIndex);
        }

        [BurstDiscard]
        private void ValidateKeyIndicesForAppendMono(int keyIndex, int afterKeyIndex)
        {
            if (keyIndex == DSPParameterKey.NullIndex && afterKeyIndex != DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to append a key to a mismatching parameter");
            if (keyIndex != DSPParameterKey.NullIndex && afterKeyIndex == DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to insert the first key to a parameter that already has keys");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateKeyIndicesForAppendBurst(int keyIndex, int afterKeyIndex)
        {
            if (keyIndex == DSPParameterKey.NullIndex && afterKeyIndex != DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to append a key to a mismatching parameter");
            if (keyIndex != DSPParameterKey.NullIndex && afterKeyIndex == DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to insert the first key to a parameter that already has keys");
        }

        private int FindFreeParameterKeyIndex()
        {
            for (int i = 0; i < ParameterKeys.Count; ++i)
                if (!ParameterKeys[i].InUse)
                    return i;
            ParameterKeys.Add(DSPParameterKey.Default);
            return ParameterKeys.Count - 1;
        }

        internal void AddFloatKey(Handle nodeHandle, void* jobReflectionData, int parameterIndex, long dspClock, float4 value, DSPParameterKeyType type)
        {
            var node = Nodes[nodeHandle.Id];
            node.Validate();
            node.ValidateReflectionData(jobReflectionData);
            node.ValidateParameter(parameterIndex);
            ValidateDSPClock(dspClock);

            var parameter = node.Parameters[parameterIndex];
            int lastKeyIndex = GetLastParameterKeyIndex(parameter.KeyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];

            ValidateConsecutiveParameterKey(lastKey, dspClock);

            float4 keyValue = default;
            Utility.ValidateIndex((int)type, (int)DSPParameterKeyType.Sustain, (int)DSPParameterKeyType.Value);
            switch (type)
            {
                case DSPParameterKeyType.Value:
                    keyValue = value;
                    break;
                case DSPParameterKeyType.Sustain:
                    keyValue = lastKey.InUse ? lastKey.Value : parameter.Value;
                    break;
                default:
                    break;
            }
            var keyIndex = AppendKey(parameter.KeyIndex, lastKeyIndex, dspClock, keyValue);
            if (parameter.KeyIndex == DSPParameterKey.NullIndex)
            {
                parameter.KeyIndex = keyIndex;
                node.Parameters[parameterIndex] = parameter;
            }
        }

        internal int GetLastParameterKeyIndex(int parameterKeyIndex, long upperClockLimit = long.MaxValue)
        {
            if (parameterKeyIndex == DSPParameterKey.NullIndex)
                return DSPParameterKey.NullIndex;

            if (parameterKeyIndex > ParameterKeys.Count)
                return DSPParameterKey.NullIndex;

            while (true)
            {
                var key = ParameterKeys[parameterKeyIndex];
                if (!key.InUse)
                    return DSPParameterKey.NullIndex;
                if (key.DSPClock >= upperClockLimit || key.NextKeyIndex == DSPParameterKey.NullIndex)
                    return parameterKeyIndex;
                parameterKeyIndex = key.NextKeyIndex;
            }
        }

        private void ValidateDSPClock(long dspClock)
        {
            ValidateDSPClockWithMeaningfulMessage(dspClock);
            ValidateDSPClockBurst(dspClock);
        }

        [BurstDiscard]
        private void ValidateDSPClockWithMeaningfulMessage(long dspClock)
        {
            if (dspClock < 0 || dspClock < DSPClock)
                throw new InvalidOperationException($"DSP clock value {dspClock} is in the past");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateDSPClockBurst(long dspClock)
        {
            if (dspClock < 0 || dspClock < DSPClock)
                throw new InvalidOperationException("DSP clock value is in the past");
        }

        internal enum DSPParameterKeyType
        {
            Value,
            Sustain,
        }

        internal void Connect(Handle sourceHandle, int outputPort, Handle destinationHandle, int inputPort, Handle connectionHandle)
        {
            var destination = Nodes[destinationHandle.Id];
            var source = Nodes[sourceHandle.Id];

            ValidateConnectionRequest(source, outputPort, destination, inputPort);

            int freeIndex = FindFreeConnectionIndex();
            connectionHandle.Id = freeIndex;

            Connections[freeIndex] = new DSPConnection
            {
                Graph = Handle,
                Handle = connectionHandle,

                OutputNodeIndex = sourceHandle.Id,
                OutputPort = outputPort,

                InputNodeIndex = destinationHandle.Id,
                InputPort = inputPort,

                NextInputConnectionIndex = destination.InputConnectionIndex,
                NextOutputConnectionIndex = source.OutputConnectionIndex,

                Attenuation = new DSPNode.Parameter
                {
                    KeyIndex = DSPParameterKey.NullIndex,
                    Value = math.float4(DSPConnection.DefaultAttenuation),
                },
            };

            destination.InputConnectionIndex = freeIndex;
            source.OutputConnectionIndex = freeIndex;

            TrashTraversalCache();
        }

        private void TrashTraversalCache()
        {
            m_GraphTraversal.Clear();
            m_TraversalDependencies.Clear();
        }

        private int FindFreeConnectionIndex()
        {
            for (int i = 0; i < Connections.Count; ++i)
                if (!Connections[i].Valid)
                    return i;
            Connections.Add(default);
            return Connections.Count - 1;
        }

        private void ValidateConnectionRequest(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            ValidateConnectionRequestWithMeaningfulMessages(source, outputPort, destination, inputPort);
            ValidateConnectionRequestBurst(source, outputPort, destination, inputPort);
        }

        [BurstDiscard]
        private void ValidateConnectionRequestWithMeaningfulMessages(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            if (!source.Valid)
                throw new ArgumentException("Invalid DSPNode", nameof(source));
            if (!destination.Valid)
                throw new ArgumentException("Invalid DSPNode", nameof(destination));

            var outputs = source.Outputs;
            var inputs = destination.Inputs;

            if (outputPort >= outputs.Count)
                throw new ArgumentOutOfRangeException(nameof(outputPort), $"Invalid output port {outputPort} on node {source.Handle.Id}");
            if (inputPort >= inputs.Count)
                throw new ArgumentOutOfRangeException(nameof(inputPort), $"Invalid input port {inputPort} on node {destination.Handle.Id}");


            var outputDesc = outputs[outputPort];
            var inputDesc = inputs[inputPort];

            if (outputDesc.Channels != inputDesc.Channels)
                throw new InvalidOperationException(
                    $"Trying to connect incompatible DSP ports together\nInput: {inputDesc.Channels} channels\nOutput: {outputDesc.Channels} channels");

            if (FindConnectionIndex(source.Handle.Id, outputPort, destination.Handle.Id, inputPort) != DSPConnection.InvalidIndex)
                throw new InvalidOperationException("Trying to make DSPNode connection that already exists");

            // If there is already a path from destination to source,
            // then making this connection will create a cycle
            if (ContainsPath(destination, source))
                throw new InvalidOperationException("Trying to connect two nodes that would result in a DSP cycle");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateConnectionRequestBurst(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            if (!source.Valid || !destination.Valid)
                throw new ArgumentException("Invalid DSPNode");

            var outputs = source.Outputs;
            var inputs = destination.Inputs;

            if (outputPort >= outputs.Count)
                throw new ArgumentOutOfRangeException("outputPort");
            if (inputPort >= inputs.Count)
                throw new ArgumentOutOfRangeException("inputPort");

            var outputDesc = outputs[outputPort];
            var inputDesc = inputs[inputPort];

            if (outputDesc.Channels != inputDesc.Channels)
                throw new InvalidOperationException("Trying to connect incompatible DSP ports together");

            if (FindConnectionIndex(source.Handle.Id, outputPort, destination.Handle.Id, inputPort) != DSPConnection.InvalidIndex)
                throw new InvalidOperationException("Trying to make DSPNode connection that already exists");

            // If there is already a path from destination to source,
            // then making this connection will create a cycle
            if (ContainsPath(destination, source))
                throw new InvalidOperationException("Trying to connect two nodes that would result in a DSP cycle");
        }

        /// <summary>
        /// Determine whether a path exists in the graph from source to destination
        /// </summary>
        private bool ContainsPath(DSPNode source, DSPNode destination)
        {
            // Base case: if source == destination, there exists a path from source to destination
            if (source.Equals(destination))
                return true;

            // Check all output connections from source for a path to destination
            var outputConnectionIndex = source.OutputConnectionIndex;
            while (outputConnectionIndex != DSPConnection.InvalidIndex)
            {
                var outgoingConnection = Connections[outputConnectionIndex];
                if (ContainsPath(Nodes[outgoingConnection.InputNodeIndex], destination))
                    return true;

                outputConnectionIndex = outgoingConnection.NextOutputConnectionIndex;
            }

            // All outgoing connections from source have been tested, no path exists
            return false;
        }

        internal int FindConnectionIndex(int sourceNodeIndex, int outputPort, int destinationNodeIndex, int inputPort)
        {
            for (int i = 0; i < Connections.Count; ++i)
            {
                var connection = Connections[i];
                if (connection.Valid &&
                    connection.InputNodeIndex == destinationNodeIndex &&
                    connection.OutputNodeIndex == sourceNodeIndex &&
                    connection.InputPort == inputPort &&
                    connection.OutputPort == outputPort)
                    return i;
            }

            return DSPConnection.InvalidIndex;
        }

        internal void Disconnect(Handle source, int outputPort, Handle destination, int inputPort)
        {
            int index = FindConnectionIndex(source.Id, outputPort, destination.Id, inputPort);
            if (index == DSPConnection.InvalidIndex)
                return;

            Disconnect(Connections[index]);
        }

        internal void Disconnect(Handle connectionHandle)
        {
            Disconnect(Connections[connectionHandle.Id]);
        }

        void Disconnect(DSPConnection connection)
        {
            var disconnectionIndex = connection.Handle.Id;
            ValidateConnectionForDisconnect(connection, Nodes);

            var input = Nodes[connection.InputNodeIndex];
            if (input.Valid)
            {
                if (disconnectionIndex == input.InputConnectionIndex)
                    input.InputConnectionIndex = connection.NextInputConnectionIndex;
                else
                {
                    var index = input.InputConnectionIndex;
                    var cursor = Connections[index];

                    while (cursor.NextInputConnectionIndex != disconnectionIndex)
                    {
                        index = cursor.NextInputConnectionIndex;
                        ValidateConnectionIndex(index);
                        cursor = Connections[index];
                    }

                    cursor.NextInputConnectionIndex = connection.NextInputConnectionIndex;
                    Connections[index] = cursor;
                }
            }

            var output = Nodes[connection.OutputNodeIndex];
            if (output.Valid)
            {
                if (disconnectionIndex == output.OutputConnectionIndex)
                    output.OutputConnectionIndex = connection.NextOutputConnectionIndex;
                else
                {
                    var index = output.OutputConnectionIndex;
                    var cursor = Connections[index];

                    while (cursor.NextOutputConnectionIndex != disconnectionIndex)
                    {
                        index = cursor.NextOutputConnectionIndex;
                        ValidateConnectionIndex(index);
                        cursor = Connections[index];
                    }

                    cursor.NextOutputConnectionIndex = connection.NextOutputConnectionIndex;
                    Connections[index] = cursor;
                }
            }

            DisposeHandle(connection.Handle);
            Connections[disconnectionIndex] = default;
        }

        private void ValidateConnectionForDisconnect(DSPConnection connection, GrowableBuffer<DSPNode> nodes)
        {
            ValidateConnectionForDisconnectMono(connection, nodes);
            ValidateConnectionForDisconnectBurst(connection, nodes);
        }

        [BurstDiscard]
        private void ValidateConnectionForDisconnectMono(DSPConnection connection, GrowableBuffer<DSPNode> nodes)
        {
            if (connection.InputNodeIndex >= nodes.Count || connection.OutputNodeIndex >= nodes.Count)
                throw new InvalidOperationException("Invalid topology");
            if (!connection.Valid)
                throw new InvalidOperationException("Connection is not in use");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateConnectionForDisconnectBurst(DSPConnection connection, GrowableBuffer<DSPNode> nodes)
        {
            if (connection.InputNodeIndex >= nodes.Count || connection.OutputNodeIndex >= nodes.Count)
                throw new InvalidOperationException("Invalid topology");
            if (!connection.Valid)
                throw new InvalidOperationException("Connection is not in use");
        }

        private void ValidateConnectionIndex(int index)
        {
            ValidateConnectionIndexMono(index);
            ValidateConnectionIndexBurst(index);
        }

        [BurstDiscard]
        private void ValidateConnectionIndexMono(int index)
        {
            if (index == DSPConnection.InvalidIndex)
                throw new InvalidOperationException("Invalid topology");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateConnectionIndexBurst(int index)
        {
            if (index == DSPConnection.InvalidIndex)
                throw new InvalidOperationException("Invalid topology");
        }

        internal DSPConnection LookupConnection(Handle connectionHandle)
        {
            var connections = Connections;
            ValidateConnectionHandle(connectionHandle, connections.Count);
            return connections[connectionHandle.Id];
        }

        static void ValidateConnectionHandle(Handle connectionHandle, int connectionHandleCount)
        {
            ValidateConnectionHandleMono(connectionHandle, connectionHandleCount);
            ValidateConnectionHandleBurst(connectionHandle, connectionHandleCount);
        }

        [BurstDiscard]
        static void ValidateConnectionHandleMono(Handle connectionHandle, int connectionHandleCount)
        {
            if (!connectionHandle.Valid || connectionHandle.Id < 0 || connectionHandle.Id >= connectionHandleCount)
                throw new ArgumentException("Invalid connection");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateConnectionHandleBurst(Handle connectionHandle, int connectionHandleCount)
        {
            if (!connectionHandle.Valid || connectionHandle.Id < 0 || connectionHandle.Id >= connectionHandleCount)
                throw new ArgumentException("Invalid connection");
        }

        static float4 FromBuffer(float* buffer, int dimension)
        {
            Utility.ValidateIndex(dimension, 4, 1);
            switch (dimension)
            {
                case 1:
                    return math.float4(*buffer);
                case 2:
                    return math.float4(buffer[0], buffer[1], buffer[0], buffer[1]);
                case 3:
                    // ?
                    return math.float4(buffer[0], buffer[1], buffer[2], 1.0f);
                case 4:
                    return math.float4(buffer[0], buffer[1], buffer[2], buffer[3]);
                default:
                    return default;
            }
        }

        internal void SetAttenuation(Handle connectionHandle, float* values, byte dimension, uint interpolationLength)
        {
            var connection = Connections[connectionHandle.Id];
            var keyIndex = connection.Attenuation.KeyIndex;
            var previousValue = (keyIndex == DSPParameterKey.NullIndex)
                ? connection.Attenuation.Value
                : DSPParameterInterpolator.Generate(0, *ParameterKeys.UnsafeDataPointer, keyIndex, DSPClock,
                DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, connection.Attenuation.Value);

            FreeParameterKeys(keyIndex);

            Utility.ValidateIndex(dimension, 4);

            float4 newValue = FromBuffer(values, dimension);
            if (interpolationLength == 0)
                keyIndex = DSPParameterKey.NullIndex;
            else
            {
                keyIndex = AppendKey(DSPParameterKey.NullIndex, DSPParameterKey.NullIndex, Math.Max(interpolationLength + DSPClock, 1) - 1, newValue);

                // We apply the previous value to the new attenuation parameter so that interpolation starts from the correct place
                newValue = previousValue;
            }

            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = keyIndex,
                Value = newValue,
            };
            Connections[connectionHandle.Id] = connection;
        }

        internal void AddAttenuationKey(Handle connectionHandle, float* values, byte dimension, long dspClock)
        {
            var connection = Connections[connectionHandle.Id];
            var keyIndex = connection.Attenuation.KeyIndex;

            Utility.ValidateIndex(dimension, 4);
            float4 value = FromBuffer(values, dimension);

            int lastKeyIndex = GetLastParameterKeyIndex(keyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];
            ValidateConsecutiveParameterKey(lastKey, dspClock);

            var newKeyIndex = AppendKey(keyIndex, lastKeyIndex, dspClock, value);
            if (keyIndex != DSPParameterKey.NullIndex)
                return;

            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = newKeyIndex,
                Value = connection.Attenuation.Value,
            };
            Connections[connectionHandle.Id] = connection;
        }

        internal void SustainAttenuation(Handle connectionHandle, long dspClock)
        {
            var connection = LookupConnection(connectionHandle);

            int lastKeyIndex = GetLastParameterKeyIndex(connection.Attenuation.KeyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];

            ValidateConsecutiveParameterKey(lastKey, dspClock);

            var sustainValue = lastKey.InUse ? lastKey.Value.x : connection.Attenuation.Value.x;
            AppendKey(connection.Attenuation.KeyIndex, lastKeyIndex, dspClock, sustainValue);
        }

        private void ValidateConsecutiveParameterKey(DSPParameterKey lastKey, long dspClock)
        {
            ValidateConsecutiveParameterKeyMono(lastKey, dspClock);
            ValidateConsecutiveParameterKeyBurst(lastKey, dspClock);
        }

        [BurstDiscard]
        private void ValidateConsecutiveParameterKeyMono(DSPParameterKey lastKey, long dspClock)
        {
            if (lastKey.InUse && lastKey.DSPClock >= dspClock)
                throw new InvalidOperationException("Adding non-consecutive key to parameter");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateConsecutiveParameterKeyBurst(DSPParameterKey lastKey, long dspClock)
        {
            if (lastKey.InUse && lastKey.DSPClock >= dspClock)
                throw new InvalidOperationException("Adding non-consecutive key to parameter");
        }

        internal void AddPort(Handle nodeHandle, int channelCount, PortType type)
        {
            ValidateNodeHandleForPortAddition(nodeHandle);

            var node = Nodes[nodeHandle.Id];
            RejectRootNodePortAddition(node, type);

            var port = new DSPNode.PortDescription
            {
                Channels = channelCount,
            };

            Utility.ValidateIndex((int)type, (int)PortType.Outlet, (int)PortType.Inlet);
            switch (type)
            {
                case PortType.Inlet:
                    node.Inputs.Add(port);
                    break;
                case PortType.Outlet:
                    node.Outputs.Add(port);
                    node.OutputPortReferences.Add(0);
                    break;
                default:
                    break;
            }
        }

        void ValidateNodeHandleForPortAddition(Handle nodeHandle)
        {
            ValidateNodeHandleForPortAdditionMono(nodeHandle);
            ValidateNodeHandleForPortAdditionBurst(nodeHandle);
        }

        [BurstDiscard]
        void ValidateNodeHandleForPortAdditionMono(Handle nodeHandle)
        {
            if (!nodeHandle.Alive)
                throw new ArgumentException("Cannot add port to inactive node", nameof(nodeHandle));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ValidateNodeHandleForPortAdditionBurst(Handle nodeHandle)
        {
            if (!nodeHandle.Alive)
                throw new ArgumentException("Cannot add port to inactive node", nameof(nodeHandle));
        }

        void RejectRootNodePortAddition(DSPNode node, PortType type)
        {
            RejectRootNodePortAdditionMono(node, type);
            RejectRootNodePortAdditionBurst(node, type);
        }

        [BurstDiscard]
        void RejectRootNodePortAdditionMono(DSPNode node, PortType type)
        {
            if (m_RootNode.Equals(node) && (type == PortType.Outlet || node.Inputs.Count > 0))
                throw new ArgumentException("Cannot add ports to the root node");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void RejectRootNodePortAdditionBurst(DSPNode node, PortType type)
        {
            if (m_RootNode.Equals(node) && (type == PortType.Outlet || node.Inputs.Count > 0))
                throw new ArgumentException("Cannot add ports to the root node");
        }

        internal void SetSampleProvider(Handle nodeHandle, int providerIndex, int subIndex, uint providerId, bool destroyOnRemove)
        {
            var node = LookupNode(nodeHandle);
            node.SetSampleProvider(providerIndex, subIndex, providerId, destroyOnRemove);
        }

        internal void InsertSampleProvider(Handle nodeHandle, int providerIndex, int subIndex, uint providerId, bool destroyOnRemove)
        {
            var node = LookupNode(nodeHandle);
            node.InsertSampleProvider(providerIndex, subIndex, providerId, destroyOnRemove);
        }

        internal void RemoveSampleProvider(Handle nodeHandle, int providerIndex, int subIndex)
        {
            var node = LookupNode(nodeHandle);
            node.RemoveSampleProvider(providerIndex, subIndex);
        }

        internal int FindFreeEventHandlerIndex()
        {
            for (int i = 0; i < EventHandlers.Count; ++i)
                if (EventHandlers[i].Hash == 0)
                    return i;
            EventHandlers.Add(default);
            return EventHandlers.Count - 1;
        }

        private int RegisterNodeEventHandler<TNodeEvent>(GCHandle handler)
            where TNodeEvent : struct
        {
            int index = FindFreeEventHandlerIndex();
            EventHandlers[index] = new EventHandlerDescription
            {
                Hash = BurstRuntime.GetHashCode64<TNodeEvent>(),
                Handler = handler,
            };
            return index;
        }

        private bool UnregisterNodeEventHandler(int handlerId)
        {
            if (handlerId < 0 || handlerId >= EventHandlers.Count || EventHandlers[handlerId].Hash == 0)
                return false;
            ReleaseGCHandle(EventHandlers[handlerId].Handler);
            EventHandlers[handlerId] = default;
            return true;
        }

        internal int FindFreeUpdateRequestIndex()
        {
            for (int i = 0; i < m_UpdateRequestHandles.Count; ++i)
                if (!m_UpdateRequestHandles[i].Node.Valid)
                    return i;
            m_UpdateRequestHandles.Add(default);
            return m_UpdateRequestHandles.Count - 1;
        }

        /// <summary>
        /// Entry point for update job execution
        /// </summary>
        /// <param name="node">The node connected to the update job</param>
        /// <param name="jobStructMemory">The update job audio kernel memory</param>
        /// <param name="updateReflectionData">The reflection data for the update job</param>
        /// <param name="jobReflectionData">The reflection data for the kernel</param>
        /// <param name="requestHandle">The request handle for the update job</param>
        internal void ExecuteUpdateJob(Handle node, void* jobStructMemory, void* updateReflectionData, void* jobReflectionData, Handle requestHandle = default)
        {
            LookupNode(node).ExecuteUpdateJob(jobStructMemory, updateReflectionData, jobReflectionData, requestHandle, out JobHandle fence);

            if (!requestHandle.Valid)
            {
                Utility.FreeUnsafe(jobStructMemory);
                return;
            }

            var description = m_UpdateRequestHandles[requestHandle.Id];
            description.Fence = fence;
            description.JobStructData = jobStructMemory;
            m_UpdateRequestHandles[requestHandle.Id] = description;
            var callbackDescription = new EventHandlerDescription
            {
                Handler = description.Callback,
            };
            if (description.Callback.IsAllocated)
            {
                //EventHandlerDescription* temporaryCallback = EventHandlerAllocator.Acquire();
                EventHandlerAllocator.Acquire(out EventHandlerDescription* temporaryCallback);
                *temporaryCallback = callbackDescription;
                MainThreadCallbacks.Enqueue(temporaryCallback);
            }
        }

        internal DSPNodeUpdateRequestDescription LookupUpdateRequest(Handle handle)
        {
            return m_UpdateRequestHandles[handle.Id];
        }

        internal void RegisterUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> request, GCHandle callback)
            where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParameters, TProviders, TAudioKernel>
            where TParameters : unmanaged, Enum
            where TProviders : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            var handle = request.Handle;
            handle.Id = FindFreeUpdateRequestIndex();
            m_UpdateRequestHandles[handle.Id] = new DSPNodeUpdateRequestDescription
            {
                Node = request.OwningNode,
                Callback = callback,
                Handle = handle,
            };
        }

        internal void ReleaseUpdateRequest(Handle handle)
        {
            var request = m_UpdateRequestHandles[handle.Id];
            ReleaseGCHandle(request.Callback);
            if (request.JobStructData != null)
                Utility.FreeUnsafe(request.JobStructData, Allocator.Persistent);
            m_UpdateRequestHandles[handle.Id] = default;
            DisposeHandle(handle);
        }

        static void ReleaseGCHandle(GCHandle handle)
        {
            if (handle.IsAllocated)
                handle.Free();
        }

        static bool EnumHasFlags<T>(T value, T flags)
            where T : unmanaged, Enum
        {
            var flagsInt = UnsafeUtility.EnumToInt(flags);
            return (UnsafeUtility.EnumToInt(value) & flagsInt) == flagsInt;
        }

        private void ValidateInitialState()
        {
            ValidateInitialStateMono();
            ValidateInitialStateBurst();
        }

        [BurstDiscard]
        private void ValidateInitialStateMono()
        {
            if (!m_RootNode.Valid || m_RootNode.Inputs.Count <= 0)
                throw new InvalidOperationException("DSPGraph has not been initalized");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateInitialStateBurst()
        {
            if (!m_RootNode.Valid || m_RootNode.Inputs.Count <= 0)
                throw new InvalidOperationException("DSPGraph has not been initalized");
        }

        internal struct EventHandlerDescription
        {
            public long Hash;
            public GCHandle Handler;
            public void* Data;
            public int NodeIndex;
        }

        internal enum PortType
        {
            Inlet,
            Outlet,
        }

        internal enum DisposeBehavior
        {
            RunDisposeJobs,
            DeallocateOnly,
        }

        /// <summary>
        /// We run this kernel on the root node.
        /// Its only purpose is to update the clock after the graph has run.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        struct UpdateDSPClockKernel : IAudioKernel<UpdateDSPClockKernel.NoParameters, UpdateDSPClockKernel.NoProviders>
        {
            [NativeDisableUnsafePtrRestriction]
            public long* DSPClock;

            public enum NoParameters {}

            public enum NoProviders {}

            public void Initialize()
            {
            }

            public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
            {
                *DSPClock += context.DSPBufferSize;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Pass in the DSPClock pointer from the owning graph
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        struct InitializeDSPClockKernel : IAudioKernelUpdate<UpdateDSPClockKernel.NoParameters, UpdateDSPClockKernel.NoProviders, UpdateDSPClockKernel>
        {
            [NativeDisableUnsafePtrRestriction]
            public long* DSPClock;

            public void Update(ref UpdateDSPClockKernel audioKernel)
            {
                audioKernel.DSPClock = DSPClock;
            }
        }
    }
}
