using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Media.Utilities;
using UnityEngine;

namespace Unity.Audio
{
    // Internal implementation details have been separated to this partial for now
    public unsafe partial struct DSPGraph
    {
        internal readonly Handle Handle;
        private readonly DSPNode m_RootNode;

        private readonly AtomicFreeListDescription m_HandleAllocator;
        private AtomicFreeList<Handle.Node> HandleAllocator => AtomicFreeList<Handle.Node>.FromDescription(m_HandleAllocator);

        private readonly AtomicFreeListDescription m_CommandAllocator;
        private AtomicFreeList<ClearDSPNodeCommand> CommandAllocator => AtomicFreeList<ClearDSPNodeCommand>.FromDescription(m_CommandAllocator);

        private readonly AtomicFreeListDescription m_TemporaryNodeAllocator;
        private AtomicFreeList<DSPNode> TemporaryNodeAllocator => AtomicFreeList<DSPNode>.FromDescription(m_TemporaryNodeAllocator);

        private readonly AtomicFreeListDescription m_EventHandlerAllocator;
        internal AtomicFreeList<EventHandlerDescription> EventHandlerAllocator => AtomicFreeList<EventHandlerDescription>.FromDescription(m_EventHandlerAllocator);

        private readonly AtomicQueueDescription m_Commands;
        private AtomicQueue<DSPCommand> Commands => AtomicQueue<DSPCommand>.FromDescription(m_Commands);

        private readonly AtomicQueueDescription m_MainThreadCallbacks;
        internal AtomicQueue<EventHandlerDescription> MainThreadCallbacks => AtomicQueue<EventHandlerDescription>.FromDescription(m_MainThreadCallbacks);

        private readonly AtomicQueueDescription m_NodesPendingDisposal;
        internal AtomicQueue<DSPNode> NodesPendingDisposal => AtomicQueue<DSPNode>.FromDescription(m_NodesPendingDisposal);

        private readonly GrowableBufferDescription m_Nodes;
        internal GrowableBuffer<DSPNode> Nodes => GrowableBuffer<DSPNode>.FromDescription(m_Nodes);

        private readonly GrowableBufferDescription m_Connections;
        internal GrowableBuffer<DSPConnection> Connections => GrowableBuffer<DSPConnection>.FromDescription(m_Connections);

        private readonly GrowableBufferDescription m_ParameterKeys;
        internal GrowableBuffer<DSPParameterKey> ParameterKeys => GrowableBuffer<DSPParameterKey>.FromDescription(m_ParameterKeys);

        private readonly GrowableBufferDescription m_UpdateRequestHandles;
        private GrowableBuffer<DSPNodeUpdateRequestDescription> UpdateRequestHandles => GrowableBuffer<DSPNodeUpdateRequestDescription>.FromDescription(m_UpdateRequestHandles);

        private readonly GrowableBufferDescription m_OwnedSampleProviders;
        private GrowableBuffer<int> OwnedSampleProviders => GrowableBuffer<int>.FromDescription(m_OwnedSampleProviders);

        private readonly GrowableBufferDescription m_RootBuffer;
        internal GrowableBuffer<float> RootBuffer => GrowableBuffer<float>.FromDescription(m_RootBuffer);

        private readonly GrowableBufferDescription m_GraphTraversal;
        private GrowableBuffer<int> GraphTraversal => GrowableBuffer<int>.FromDescription(m_GraphTraversal);

        private readonly GrowableBufferDescription m_TraversalDependencies;
        private GrowableBuffer<IntPtr> TraversalDependencies => GrowableBuffer<IntPtr>.FromDescription(m_TraversalDependencies);

        private readonly GrowableBufferDescription m_EventHandlers;
        internal GrowableBuffer<EventHandlerDescription> EventHandlers => GrowableBuffer<EventHandlerDescription>.FromDescription(m_EventHandlers);

        private readonly GrowableBufferDescription m_ExecutionBuffer;
        private GrowableBuffer<DSPGraphExecutionNode> ExecutionBuffer => GrowableBuffer<DSPGraphExecutionNode>.FromDescription(m_ExecutionBuffer);

        private readonly GrowableBufferDescription m_TopologicalRoots;
        private GrowableBuffer<int> TopologicalRoots => GrowableBuffer<int>.FromDescription(m_TopologicalRoots);

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
            m_DisposeFunctionPointer = Marshal.GetFunctionPointerForDelegate(DSPGraphExtensions.DisposeMethod);;
            ProfilerMarkers = ProfilerMarkers.Create();

            var handleAllocator = new AtomicFreeList<Handle.Node>(AllocationMode.Pooled);
            m_HandleAllocator = handleAllocator.Description;
            m_CommandAllocator = new AtomicFreeList<ClearDSPNodeCommand>(AllocationMode.Pooled).Description;
            m_TemporaryNodeAllocator = new AtomicFreeList<DSPNode>(AllocationMode.Pooled).Description;
            m_EventHandlerAllocator = new AtomicFreeList<EventHandlerDescription>(AllocationMode.Pooled).Description;

            m_Commands = AtomicQueue<DSPCommand>.Create().Description;
            m_MainThreadCallbacks = AtomicQueue<EventHandlerDescription>.Create().Description;
            m_NodesPendingDisposal = AtomicQueue<DSPNode>.Create().Description;

            m_Nodes = new GrowableBuffer<DSPNode>(Allocator.Persistent).Description;
            m_Connections = new GrowableBuffer<DSPConnection>(Allocator.Persistent).Description;
            m_ParameterKeys = new GrowableBuffer<DSPParameterKey>(Allocator.Persistent).Description;
            m_UpdateRequestHandles = new GrowableBuffer<DSPNodeUpdateRequestDescription>(Allocator.Persistent).Description;
            m_OwnedSampleProviders = new GrowableBuffer<int>(Allocator.Persistent).Description;
            m_RootBuffer = new GrowableBuffer<float>(Allocator.Persistent, dspBufferSize * outputChannels).Description;
            m_GraphTraversal = new GrowableBuffer<int>(Allocator.Persistent).Description;
            m_TraversalDependencies = new GrowableBuffer<IntPtr>(Allocator.Persistent).Description;
            m_EventHandlers = new GrowableBuffer<EventHandlerDescription>(Allocator.Persistent).Description;
            m_ExecutionBuffer = new GrowableBuffer<DSPGraphExecutionNode>(Allocator.Persistent).Description;
            m_TopologicalRoots = new GrowableBuffer<int>(Allocator.Persistent).Description;
            m_UnsafeGraphBuffer = DSPGraphExtensions.UnsafeGraphBuffer;

            Handle.Node* node = handleAllocator.Acquire();
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
            AddPort(m_RootNode.Handle, outputChannels, outputFormat, PortType.Inlet);
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

            var nodes = Nodes;
            var leakedNodeCount = 0;
            // Skip root node
            for (int i = 1; i < nodes.Count; ++i)
            {
                var node = nodes[i];
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
            var connections = Connections;
            for (int i = 0; i < connections.Count; ++i)
                if (connections[i].Valid)
                    DisposeHandle(connections[i].Handle);
            Connections.Dispose();

            // Dangling update requests need to be released
            var requests = UpdateRequestHandles;
            for (int i = 0; i < requests.Count; ++i)
                if (requests[i].Handle.Valid)
                    ReleaseUpdateRequest(requests[i].Handle);
            UpdateRequestHandles.Dispose();

            Commands.Dispose();
            MainThreadCallbacks.Dispose();
            NodesPendingDisposal.Dispose();
            ParameterKeys.Dispose();
            OwnedSampleProviders.Dispose();
            RootBuffer.Dispose();
            GraphTraversal.Dispose();
            TraversalDependencies.Dispose();
            EventHandlers.Dispose();
            ExecutionBuffer.Dispose();
            TopologicalRoots.Dispose();
            CommandAllocator.Dispose();
            TemporaryNodeAllocator.Dispose();
            EventHandlerAllocator.Dispose();
            DisposeHandle(Handle);
            HandleAllocator.Dispose();
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
            Handle.Node* node = HandleAllocator.Acquire();
            node->Id = Handle.Node.InvalidId;
            return new Handle(node);
        }

        internal T* AllocateCommand<T>()
            where T : unmanaged, IDSPCommand
        {
            ValidateCommandAllocation<T>();
            return (T*)CommandAllocator.Acquire();
        }

        private static void ValidateCommandAllocation<T>()
            where T : unmanaged
        {
            ValidateCommandAllocationWithMeaningfulMessage<T>();
            if (UnsafeUtility.SizeOf<T>() > UnsafeUtility.SizeOf<ClearDSPNodeCommand>())
                throw new ArgumentException("Command size is too large");
        }

        [BurstDiscard]
        private static void ValidateCommandAllocationWithMeaningfulMessage<T>()
            where T : unmanaged
        {
            if (UnsafeUtility.SizeOf<T>() > UnsafeUtility.SizeOf<ClearDSPNodeCommand>())
                throw new ArgumentException($"Size of {typeof(T).FullName} is larger than allowed maximum {UnsafeUtility.SizeOf<ClearDSPNodeCommand>()}");
        }

        internal void ReleaseCommand(void* commandPointer)
        {
            DSPCommand.Dispose(commandPointer);
            ClearDSPNodeCommand* command = (ClearDSPNodeCommand*)commandPointer;
            *command = default;
            CommandAllocator.Release(command);
        }

        /// <summary>
        /// Release a handle to the graph's free list
        /// </summary>
        /// <param name="handle">The handle to release</param>
        internal void DisposeHandle(Handle handle)
        {
            handle.FlushNode();
            HandleAllocator.Release(handle.AtomicNode);
        }

        /// <summary>
        /// Add a set of commands to the graph's command queue (any thread)
        /// </summary>
        /// <param name="commands">The commands to add (IntPtrs that are really DSPCommand*)</param>
        internal void ScheduleCommandBuffer(IList<IntPtr> commands)
        {
            var commandQueue = Commands;
            for (int i = 0; i < commands.Count; ++i)
                commandQueue.Enqueue((DSPCommand*)commands[i]);
        }

        /// <summary>
        /// Execute the pending commands from the graph's command queue (mixer thread only)
        /// </summary>
        internal void ApplyScheduledCommands()
        {
            var commandQueue = Commands;
            while (!commandQueue.IsEmpty)
            {
                var command = commandQueue.Dequeue();
                DSPCommand.Schedule(command);
                ReleaseCommand(command);
            }
        }

        // These could all be generalized with loader and attenuator functions, but how will that affect performance?
        // e.g. Attenuate(float* source, float* destination, float4 attenuation, int frames, int channels, Func<float, IntPtr, int> LoadDestination, Func<float, float4, int> GetAttenuation)
        static void Attenuate(float* source, float* destination, float4 attenuation, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            var total = frames * channels;
            for (int beginFrameOffset = 0; beginFrameOffset < total; beginFrameOffset += channels)
                for (int channel = 0; channel < channels; ++channel)
                    destination[beginFrameOffset + channel] += source[beginFrameOffset + channel] * attenuation[channel % 4];
        }

        static void AttenuateAndClear(float* source, float* destination, float4 attenuation, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            var total = frames * channels;
            for (int beginFrameOffset = 0; beginFrameOffset < total; beginFrameOffset += channels)
                for (int channel = 0; channel < channels; ++channel)
                    destination[beginFrameOffset + channel] = source[beginFrameOffset + channel] * attenuation[channel % 4];
        }

        private void ApplyInterpolatedAttenuation(DSPConnection connection, float* source, float* destination, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            DSPParameterKey* keys = ParameterKeys.UnsafeDataPointer;
            var attenuation = connection.Attenuation;
            float4 attenuationValue = 1f;
            var connections = Connections;

            for (int frame = 0; frame < frames; ++frame)
            {
                attenuationValue = DSPParameterInterpolator.Generate(frame, keys, attenuation.KeyIndex, DSPClock,
                    DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, attenuation.Value);
                for (int channel = 0; channel < channels; ++channel)
                {
                    var bufferIndex = frame * channels + channel;
                    destination[bufferIndex] += source[bufferIndex] * attenuationValue[channel % 4];
                }
            }

            // Update attenuation with last interpolated value
            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = FreeParameterKeys(attenuation.KeyIndex, DSPClock + frames),
                Value = attenuationValue,
            };
            connections[connection.Handle.Id] = connection;
        }

        private void ApplyInterpolatedAttenuationAndClear(DSPConnection connection, float* source, float* destination, int frames, int channels)
        {
            // FIXME: Per-channel attenuation for >4 channels?
            DSPParameterKey* keys = ParameterKeys.UnsafeDataPointer;
            var attenuation = connection.Attenuation;
            float4 attenuationValue = 1f;
            var connections = Connections;

            for (int frame = 0; frame < frames; ++frame)
            {
                attenuationValue = DSPParameterInterpolator.Generate(frame, keys, attenuation.KeyIndex, DSPClock,
                    DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, attenuation.Value);
                for (int channel = 0; channel < channels; ++channel)
                {
                    var bufferIndex = frame * channels + channel;
                    destination[bufferIndex] = source[bufferIndex] * attenuationValue[channel % 4];
                }
            }

            // Update attenuation with last interpolated value
            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = FreeParameterKeys(attenuation.KeyIndex, DSPClock + frames),
                Value = attenuationValue,
            };
            connections[connection.Handle.Id] = connection;
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
            if (attenuation.KeyIndex != DSPParameterKey.NullIndex)
                if (clear)
                    ApplyInterpolatedAttenuationAndClear(inputConnection, sourceBuffer, targetBuffer, sampleFrameCount, channels);
                else
                    ApplyInterpolatedAttenuation(inputConnection, sourceBuffer, targetBuffer, sampleFrameCount, channels);
            else if (clear)
                AttenuateAndClear(sourceBuffer, targetBuffer, attenuation.Value, sampleFrameCount, channels);
            else
                Attenuate(sourceBuffer, targetBuffer, attenuation.Value, sampleFrameCount, channels);
            return true;
        }

        void MixJobInputs(DSPNode node)
        {
            ProfilerMarkers.MixNodeInputsMarker.Begin();
            var nodes = Nodes;
            var connections = Connections;
            var inputBuffers = node.JobDataBuffer->NativeJobData.InputBuffers;
            var sampleFrameCount = node.JobDataBuffer->NativeJobData.SampleReadCount;

            for (var inputConnectionIndex = node.InputConnectionIndex; inputConnectionIndex != DSPConnection.InvalidIndex;)
            {
                var connection = connections[inputConnectionIndex];
                var inputNode = nodes[connection.OutputNodeIndex];
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
            var graphTraversal = GraphTraversal;
            var traversalDependencies = TraversalDependencies;
            var nodes = Nodes;
            var dependencyCount = 0;
            graphTraversal.CheckCapacity(nodes.Count);

            // Clear port reference counts
            for (int n = 0; n < nodes.Count; n++)
            {
                var node = nodes[n];
                if (!node.Valid)
                    continue;

                var outputPortReferences = node.OutputPortReferences;
                for (int i = 0; i < node.Outputs.Count; i++)
                    outputPortReferences[i] = 0;

                node.OutputReferences = 0;
            }

            // Put unattached subtrees into the traversal array before the root node
            // so that their job fences are set up when it's time to set up the root node dependencies
            if (EnumHasFlags(executionMode, ExecutionMode.ExecuteNodesWithNoOutputs))

            {
                var roots = TopologicalRoots;
                roots.Clear();
                for (int i = 1; i < nodes.Count; ++i)
                {
                    if (nodes[i].Valid && nodes[i].IsTopologicalRoot)
                    {
                        roots.Add(i);
                        dependencyCount += BuildTraversalCacheRecursive(nodes, Connections, graphTraversal, i, 0);
                    }
                }
            }

            dependencyCount += BuildTraversalCacheRecursive(nodes, Connections, graphTraversal, 0, 0);
            traversalDependencies.CheckCapacity(dependencyCount + graphTraversal.Count);
        }

        private int BuildTraversalCacheRecursive(GrowableBuffer<DSPNode> nodes, GrowableBuffer<DSPConnection> connections, GrowableBuffer<int> graphTraversal, int nodeIndex, int portIndex)
        {
            var node = nodes[nodeIndex];
            var outputPortReferences = node.OutputPortReferences;

            outputPortReferences[portIndex]++;
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
            var nodes = Nodes;
            var graphTraversal = GraphTraversal;

            for (int i = 0; i < graphTraversal.Count; ++i)
                RunJobForNode(nodes[graphTraversal[i]]);
        }

        private void BeginMixJobified(ExecutionMode executionMode)
        {
            var graphTraversal = GraphTraversal;
            var traversalDependencies = TraversalDependencies;
            var nodes = Nodes;
            var connections = Connections;
            var executionBuffer = ExecutionBuffer;
            executionBuffer.Clear();
            executionBuffer.CheckCapacity(graphTraversal.Count);
            traversalDependencies.Clear();

            for (int i = 0; i < graphTraversal.Count; ++i)
            {
                var node = nodes[graphTraversal[i]];
                var parentIndex = traversalDependencies.Count;
                traversalDependencies.Add((IntPtr)node.JobFence);
                var inputConnectionIndex = node.InputConnectionIndex;
                while (inputConnectionIndex != DSPConnection.InvalidIndex)
                {
                    var conn = connections[inputConnectionIndex];
                    traversalDependencies.Add((IntPtr)nodes[conn.OutputNodeIndex].JobFence);
                    inputConnectionIndex = conn.NextInputConnectionIndex;
                }

                if (EnumHasFlags(executionMode, ExecutionMode.ExecuteNodesWithNoOutputs) && m_RootNode.Equals(node))
                {
                    // Add topological roots as dependencies to the root node so that sync works properly
                    var roots = TopologicalRoots;
                    for (int rootIndex = 0; rootIndex < roots.Count; ++rootIndex)
                        traversalDependencies.Add((IntPtr) nodes[roots[rootIndex]].JobFence);
                }

                executionBuffer.Add(new DSPGraphExecutionNode
                {
                    JobData = node.JobDataBuffer,
                    JobStructData = node.JobStructData,
                    ReflectionData = node.JobReflectionData,
                    ResourceContext = node.ResourceContextHead,
                    FunctionIndex = 0,
                    FenceIndex = parentIndex,
                    FenceCount = traversalDependencies.Count - parentIndex,
                });
            }

            // TODO: Job batch scope
            DSPGraphInternal.Internal_ScheduleGraph(default, executionBuffer.UnsafeDataPointer, executionBuffer.Count, null, traversalDependencies.UnsafeDataPointer);
        }

        private static void ValidateExecutionMode(ExecutionMode mode)
        {
            if (EnumHasFlags(mode, ExecutionMode.Jobified) == EnumHasFlags(mode, ExecutionMode.Synchronous))
                throw new ArgumentException("Execution mode must contain exactly one of: Jobified, Synchronous");
        }

        internal void CreateDSPNode(Handle nodeHandle, void* jobReflectionData, void* persistentStructMemory, AudioKernelExtensions.DSPParameterDescription* descriptions, int parameterCount, AudioKernelExtensions.DSPSampleProviderDescription* dspSampleProviderDescription, int sampleProviderCount)
        {
            var nodes = Nodes;
            nodeHandle.Id = FindFreeNodeIndex();
            var node = new DSPNode(this, nodeHandle, *m_UnsafeGraphBuffer, jobReflectionData, persistentStructMemory, descriptions, parameterCount, dspSampleProviderDescription, sampleProviderCount);
            nodes[nodeHandle.Id] = node;
            ProfilerMarkers.AudioKernelInitializeMarker.Begin();
            DSPGraphInternal.Internal_InitializeJob(persistentStructMemory, jobReflectionData, node.ResourceContextHead);
            ProfilerMarkers.AudioKernelInitializeMarker.End();
        }

        private int FindFreeNodeIndex()
        {
            var nodes = Nodes;
            for (int i = 0; i < nodes.Count; ++i)
                if (!nodes[i].Valid)
                    return i;
            nodes.Add(default);
            return nodes.Count - 1;
        }

        internal void ReleaseDSPNode(Handle nodeHandle)
        {
            var nodes = Nodes;
            var id = nodeHandle.Id;
            var node = nodes[id];

            DisposeHandle(nodeHandle);
            nodes[id] = default;
            TrashTraversalCache();
        }

        internal void ScheduleDSPNodeDisposal(Handle nodeHandle)
        {
            var node = Nodes[nodeHandle.Id];

            // Disconnect connections before disposing
            var connections = Connections;
            while (node.InputConnectionIndex != DSPConnection.InvalidIndex)
                Disconnect(connections[node.InputConnectionIndex]);
            while (node.OutputConnectionIndex != DSPConnection.InvalidIndex)
                Disconnect(connections[node.OutputConnectionIndex]);

            ReleaseDSPNode(nodeHandle);

            DSPNode* temporaryNode = TemporaryNodeAllocator.Acquire();
            *temporaryNode = node;
            NodesPendingDisposal.Enqueue(temporaryNode);
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

        public int FreeParameterKeys(int keyIndex, long upperClockLimit = long.MaxValue)
        {
            var keys = ParameterKeys;
            while (keyIndex != DSPParameterKey.NullIndex)
            {
                var key = keys[keyIndex];
                if (key.DSPClock > upperClockLimit)
                    return keyIndex;
                var oldKeyIndex = keyIndex;
                keyIndex = key.NextKeyIndex;
                keys[oldKeyIndex] = DSPParameterKey.Default;
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
            var parameters = node.Parameters;
            var parameter = parameters[parameterIndex];

            FreeParameterKeys(parameter.KeyIndex);

            if (interpolationLength == 0)
                parameter = new DSPNode.Parameter
                {
                    KeyIndex = DSPParameterKey.NullIndex,
                    Value = value,
                };
            else
                parameter.KeyIndex = AppendKey(DSPParameterKey.NullIndex, DSPParameterKey.NullIndex, Math.Max(interpolationLength + DSPClock, 1) - 1, value);
            parameters[parameterIndex] = parameter;
        }

        internal int AppendKey(int keyIndex, int afterKeyIndex, long dspClock, float4 value)
        {
            var keys = ParameterKeys;
            if (keyIndex == DSPParameterKey.NullIndex && afterKeyIndex != DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to append a key to a mismatching parameter");
            if (keyIndex != DSPParameterKey.NullIndex && afterKeyIndex == DSPParameterKey.NullIndex)
                throw new ArgumentException("Trying to insert the first key to a parameter that already has keys");

            var newIndex = FindFreeParameterKeyIndex();
            keys[newIndex] = new DSPParameterKey
            {
                InUse = true,
                DSPClock = dspClock,
                NextKeyIndex = DSPParameterKey.NullIndex,
                Value = value,
            };

            if (afterKeyIndex != DSPParameterKey.NullIndex)
            {
                var afterKey = keys[afterKeyIndex];
                afterKey.NextKeyIndex = newIndex;
                keys[afterKeyIndex] = afterKey;
            }

            return newIndex;
        }

        private int FindFreeParameterKeyIndex()
        {
            var keys = ParameterKeys;
            for (int i = 0; i < keys.Count; ++i)
                if (!keys[i].InUse)
                    return i;
            keys.Add(DSPParameterKey.Default);
            return keys.Count - 1;
        }

        internal void AddFloatKey(Handle nodeHandle, void* jobReflectionData, int parameterIndex, long dspClock, float4 value, DSPParameterKeyType type)
        {
            var node = Nodes[nodeHandle.Id];
            node.Validate();
            node.ValidateReflectionData(jobReflectionData);
            node.ValidateParameter(parameterIndex);
            ValidateDSPClock(dspClock);

            var parameters = node.Parameters;
            var parameter = parameters[parameterIndex];
            int lastKeyIndex = GetLastParameterKeyIndex(parameter.KeyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];

            if (lastKey.InUse && lastKey.DSPClock >= dspClock)
                throw new InvalidOperationException("Adding non-consecutive key to parameter");

            float4 keyValue;
            switch (type)
            {
                case DSPParameterKeyType.Value:
                    keyValue = value;
                    break;
                case DSPParameterKeyType.Sustain:
                    keyValue = lastKey.InUse ? lastKey.Value : parameter.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
            var keyIndex = AppendKey(parameter.KeyIndex, lastKeyIndex, dspClock, keyValue);
            if (parameter.KeyIndex == DSPParameterKey.NullIndex)
            {
                parameter.KeyIndex = keyIndex;
                parameters[parameterIndex] = parameter;
            }
        }

        internal int GetLastParameterKeyIndex(int parameterKeyIndex, long upperClockLimit = long.MaxValue)
        {
            if (parameterKeyIndex == DSPParameterKey.NullIndex)
                return DSPParameterKey.NullIndex;

            var keys = ParameterKeys;
            if (parameterKeyIndex > keys.Count)
                return DSPParameterKey.NullIndex;

            while (true)
            {
                var key = keys[parameterKeyIndex];
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
            if (dspClock < 0 || dspClock < DSPClock)
                throw new InvalidOperationException("DSP clock value is in the past");
        }

        [BurstDiscard]
        private void ValidateDSPClockWithMeaningfulMessage(long dspClock)
        {
            if (dspClock < 0 || dspClock < DSPClock)
                throw new InvalidOperationException($"DSP clock value {dspClock} is in the past");
        }

        internal enum DSPParameterKeyType
        {
            Value,
            Sustain,
        }

        internal void Connect(Handle sourceHandle, int outputPort, Handle destinationHandle, int inputPort, Handle connectionHandle)
        {
            var nodes = Nodes;
            var connections = Connections;
            var destination = nodes[destinationHandle.Id];
            var source = nodes[sourceHandle.Id];

            ValidateConnectionRequest(source, outputPort, destination, inputPort);

            int freeIndex = FindFreeConnectionIndex();
            connectionHandle.Id = freeIndex;

            connections[freeIndex] = new DSPConnection
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
            GraphTraversal.Clear();
            TraversalDependencies.Clear();
        }

        private int FindFreeConnectionIndex()
        {
            var connections = Connections;
            for (int i = 0; i < connections.Count; ++i)
                if (!connections[i].Valid)
                    return i;
            connections.Add(default);
            return connections.Count - 1;
        }

        private void ValidateConnectionRequest(DSPNode source, int outputPort, DSPNode destination, int inputPort)
        {
            ValidateConnectionRequestWithMeaningfulMessages(source, outputPort, destination, inputPort);
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

            if (outputDesc.Channels != inputDesc.Channels || outputDesc.Format != inputDesc.Format)
                throw new InvalidOperationException("Trying to connect incompatible DSP ports together");

            if (FindConnectionIndex(source.Handle.Id, outputPort, destination.Handle.Id, inputPort) != DSPConnection.InvalidIndex)
                throw new InvalidOperationException("Trying to make DSPNode connection that already exists");

            // If there is already a path from destination to source,
            // then making this connection will create a cycle
            if (ContainsPath(destination, source))
                throw new InvalidOperationException("Trying to connect two nodes that would result in a DSP cycle");
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

            if (outputDesc.Channels != inputDesc.Channels || outputDesc.Format != inputDesc.Format)
                throw new InvalidOperationException(
                    $"Trying to connect incompatible DSP ports together\nInput: {inputDesc.Channels} channels, format {inputDesc.Format}\nOutput: {outputDesc.Channels} channels, format {outputDesc.Format}");
        }

        /// <summary>
        /// Determine whether a path exists in the graph from source to destination
        /// </summary>
        private bool ContainsPath(DSPNode source, DSPNode destination)
        {
            // Base case: if source == destination, there exists a path from source to destination
            if (source.Equals(destination))
                return true;

            var connections = Connections;
            var nodes = Nodes;

            // Check all output connections from source for a path to destination
            var outputConnectionIndex = source.OutputConnectionIndex;
            while (outputConnectionIndex != DSPConnection.InvalidIndex)
            {
                var outgoingConnection = connections[outputConnectionIndex];
                if (ContainsPath(nodes[outgoingConnection.InputNodeIndex], destination))
                    return true;

                outputConnectionIndex = outgoingConnection.NextOutputConnectionIndex;
            }

            // All outgoing connections from source have been tested, no path exists
            return false;
        }

        internal int FindConnectionIndex(int sourceNodeIndex, int outputPort, int destinationNodeIndex, int inputPort)
        {
            var connections = Connections;
            for (int i = 0; i < connections.Count; ++i)
            {
                var connection = connections[i];
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
            var connections = Connections;
            var nodes = Nodes;
            var disconnectionIndex = connection.Handle.Id;

            if (connection.InputNodeIndex >= nodes.Count || connection.OutputNodeIndex >= nodes.Count)
                throw new InvalidOperationException("Invalid topology");
            if (!connection.Valid)
                throw new InvalidOperationException("Connection is not in use");

            var input = nodes[connection.InputNodeIndex];
            if (input.Valid)
            {
                if (disconnectionIndex == input.InputConnectionIndex)
                    input.InputConnectionIndex = connection.NextInputConnectionIndex;
                else
                {
                    var index = input.InputConnectionIndex;
                    var cursor = connections[index];

                    while (cursor.NextInputConnectionIndex != disconnectionIndex)
                    {
                        index = cursor.NextInputConnectionIndex;
                        if (index == DSPConnection.InvalidIndex)
                            throw new InvalidOperationException("Invalid topology");
                        cursor = connections[index];
                    }

                    cursor.NextInputConnectionIndex = connection.NextInputConnectionIndex;
                    connections[index] = cursor;
                }
            }

            var output = nodes[connection.OutputNodeIndex];
            if (output.Valid)
            {
                if (disconnectionIndex == output.OutputConnectionIndex)
                    output.OutputConnectionIndex = connection.NextOutputConnectionIndex;
                else
                {
                    var index = output.OutputConnectionIndex;
                    var cursor = connections[index];

                    while (cursor.NextOutputConnectionIndex != disconnectionIndex)
                    {
                        index = cursor.NextOutputConnectionIndex;
                        if (index == DSPConnection.InvalidIndex)
                            throw new InvalidOperationException("Invalid topology");
                        cursor = connections[index];
                    }

                    cursor.NextOutputConnectionIndex = connection.NextOutputConnectionIndex;
                    connections[index] = cursor;
                }
            }

            DisposeHandle(connection.Handle);
            connections[disconnectionIndex] = default;
        }

        internal DSPConnection LookupConnection(Handle connectionHandle)
        {
            var connections = Connections;
            ValidateConnectionHandle(connectionHandle, connections.Count);
            return connections[connectionHandle.Id];
        }

        static void ValidateConnectionHandle(Handle connectionHandle, int connectionHandleCount)
        {
            if (!connectionHandle.Valid || connectionHandle.Id < 0 || connectionHandle.Id >= connectionHandleCount)
                throw new ArgumentException("Invalid connection");
        }

        static float4 FromBuffer(float* buffer, int dimension)
        {
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
                    throw new ArgumentOutOfRangeException("dimension");
            }
        }

        internal void SetAttenuation(Handle connectionHandle, float* values, byte dimension, uint interpolationLength)
        {
            var connections = Connections;
            var connection = connections[connectionHandle.Id];
            var keyIndex = connection.Attenuation.KeyIndex;
            var previousValue = (keyIndex == DSPParameterKey.NullIndex)
                ? connection.Attenuation.Value
                : DSPParameterInterpolator.Generate(0, ParameterKeys.UnsafeDataPointer, keyIndex, DSPClock,
                    DSPConnection.MinimumAttenuation, DSPConnection.MaximumAttenuation, connection.Attenuation.Value);

            FreeParameterKeys(keyIndex);

            if (dimension > 4)
                throw new NotImplementedException("Attenuation buffers larger than 4 are not implemented");

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
            connections[connectionHandle.Id] = connection;
        }

        internal void AddAttenuationKey(Handle connectionHandle, float* values, byte dimension, long dspClock)
        {
            var connections = Connections;
            var connection = connections[connectionHandle.Id];
            var keyIndex = connection.Attenuation.KeyIndex;

            if (dimension > 4)
                throw new NotImplementedException("Attenuation buffers larger than 4 are not implemented");
            float4 value = FromBuffer(values, dimension);

            int lastKeyIndex = GetLastParameterKeyIndex(keyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];
            if (lastKey.InUse && lastKey.DSPClock >= dspClock)
                throw new InvalidOperationException("Adding non-consecutive key to parameter");

            var newKeyIndex = AppendKey(keyIndex, lastKeyIndex, dspClock, value);
            if (keyIndex != DSPParameterKey.NullIndex)
                return;

            connection.Attenuation = new DSPNode.Parameter
            {
                KeyIndex = newKeyIndex,
                Value = connection.Attenuation.Value,
            };
            connections[connectionHandle.Id] = connection;
        }

        internal void SustainAttenuation(Handle connectionHandle, long dspClock)
        {
            var connection = LookupConnection(connectionHandle);

            int lastKeyIndex = GetLastParameterKeyIndex(connection.Attenuation.KeyIndex, dspClock);
            var lastKey = (lastKeyIndex == DSPParameterKey.NullIndex) ? DSPParameterKey.Default : ParameterKeys[lastKeyIndex];

            if (lastKey.InUse && lastKey.DSPClock >= dspClock)
                throw new InvalidOperationException("Adding non-consecutive key to parameter");

            var sustainValue = lastKey.InUse ? lastKey.Value.x : connection.Attenuation.Value.x;
            AppendKey(connection.Attenuation.KeyIndex, lastKeyIndex, dspClock, sustainValue);
        }

        internal void AddPort(Handle nodeHandle, int channelCount, SoundFormat format, PortType type)
        {
            if (!nodeHandle.Alive)
                throw new ArgumentException("Cannot add port to inactive node", nameof(nodeHandle));

            var node = Nodes[nodeHandle.Id];
            if (m_RootNode.Equals(node) && (type == PortType.Outlet || node.Inputs.Count > 0))
                throw new ArgumentException("Cannot add ports to the root node");

            var port = new DSPNode.PortDescription
            {
                Channels = channelCount,
                Format = format,
            };

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
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid port type");
            }
        }

        internal void RemovePort(Handle nodeHandle, int portIndex, PortType type)
        {
            if(!nodeHandle.Alive)
                throw new ArgumentException("Cannot remove port from inactive node", nameof(nodeHandle));

            var node = Nodes[nodeHandle.Id];
            if(m_RootNode.Equals(node))
                throw new ArgumentException("Cannot remove ports from the root node");

            switch (type)
            {
                case PortType.Inlet:
                    {
                        var connections = Connections;
                        for (int i = connections.Count - 1; i >= 0; --i)
                        {
                            if (connections[i].InputPort == portIndex)
                            {
                                Disconnect(connections[i]);
                            }
                        }
                        node.Inputs.RemoveAt(portIndex);
                        break;
                    }
                case PortType.Outlet:
                    {
                        var connections = Connections;
                        for (int i = connections.Count - 1; i >= 0; --i)
                        {
                            if (connections[i].OutputPort == portIndex)
                            {
                                Disconnect(connections[i]);
                            }
                        }
                        node.Outputs.RemoveAt(portIndex);
                        node.OutputPortReferences.RemoveAt(portIndex);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid port type");
            }
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
            var handlers = EventHandlers;
            for (int i = 0; i < handlers.Count; ++i)
                if (handlers[i].Hash == 0)
                    return i;
            handlers.Add(default);
            return handlers.Count - 1;
        }

        private int RegisterNodeEventHandler<TNodeEvent>(GCHandle handler)
            where TNodeEvent : struct
        {
            int index = FindFreeEventHandlerIndex();
            var handlers = EventHandlers;
            handlers[index] = new EventHandlerDescription
            {
                Hash = BurstRuntime.GetHashCode64<TNodeEvent>(),
                Handler = handler,
            };
            return index;
        }

        private bool UnregisterNodeEventHandler(int handlerId)
        {
            var handlers = EventHandlers;
            if (handlerId < 0 || handlerId >= handlers.Count || handlers[handlerId].Hash == 0)
                return false;
            ReleaseGCHandle(handlers[handlerId].Handler);
            handlers[handlerId] = default;
            return true;
        }

        internal int FindFreeUpdateRequestIndex()
        {
            var requests = UpdateRequestHandles;
            for (int i = 0; i < requests.Count; ++i)
                if (!requests[i].Node.Valid)
                    return i;
            requests.Add(default);
            return requests.Count - 1;
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

            var requests = UpdateRequestHandles;
            var description = requests[requestHandle.Id];
            description.Fence = fence;
            description.JobStructData = jobStructMemory;
            requests[requestHandle.Id] = description;
            var callbackDescription = new EventHandlerDescription
            {
                Handler = description.Callback,
            };
            if (description.Callback.IsAllocated)
            {
                EventHandlerDescription* temporaryCallback = EventHandlerAllocator.Acquire();
                *temporaryCallback = callbackDescription;
                MainThreadCallbacks.Enqueue(temporaryCallback);
            }
        }

        internal DSPNodeUpdateRequestDescription LookupUpdateRequest(Handle handle)
        {
            return UpdateRequestHandles[handle.Id];
        }

        internal void RegisterUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel>(DSPNodeUpdateRequest<TAudioKernelUpdate, TParameters, TProviders, TAudioKernel> request, GCHandle callback)
            where TAudioKernelUpdate : struct, IAudioKernelUpdate<TParameters, TProviders, TAudioKernel>
            where TParameters : unmanaged, Enum
            where TProviders : unmanaged, Enum
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
        {
            var requests = UpdateRequestHandles;
            var handle = request.Handle;
            handle.Id = FindFreeUpdateRequestIndex();
            requests[handle.Id] = new DSPNodeUpdateRequestDescription
            {
                Node = request.OwningNode,
                Callback = callback,
                Handle = handle,
            };
        }

        internal void ReleaseUpdateRequest(Handle handle)
        {
            var requests = UpdateRequestHandles;
            var request = requests[handle.Id];
            ReleaseGCHandle(request.Callback);
            if (request.JobStructData != null)
                Utility.FreeUnsafe(request.JobStructData, Allocator.Persistent);
            requests[handle.Id] = default;
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
