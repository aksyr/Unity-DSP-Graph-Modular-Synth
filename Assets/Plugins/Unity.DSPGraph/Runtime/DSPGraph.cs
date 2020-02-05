using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// DSPGraph is the low level mixing engine container which provides methods to create DSPNodes and DSPConnections.
    /// The DSPNodes and DSPConnections created in a DSPGraph is local to that graph.
    /// </summary>
    public unsafe partial struct DSPGraph : IDisposable, IHandle<DSPGraph>
    {
        public bool Valid => Handle.Valid;

        /// <summary>
        /// An accessor for mixer thread operations.
        /// Throws <see cref="InvalidOperationException"/>InvalidOperationException</see> if called from a thread that is not the mixer thread for this graph.
        /// </summary>
        public OutputMixerHandle OutputMixer
        {
            get
            {
                // A readonly field cannot be used as a ref or out value
                var handle = Handle;
                DSPGraphInternal.Internal_AssertMixerThread(ref handle);
                return new OutputMixerHandle(this);
            }
        }

        /// <summary>
        /// This method is called before reading the samples from the mix
        /// </summary>
        /// <param name="frameCount">The number of frames to mix</param>
        /// <param name="executionMode">The execution mode to be used for audio kernels</param>
        /// <remarks>This method will be bursted if the owning IAudioOutputJob is decorated with BurstCompileAttribute</remarks>
        public void BeginMix(int frameCount, ExecutionMode executionMode = ExecutionMode.Jobified)
        {
            ValidateExecutionMode(executionMode);

            ProfilerMarkers.BeginMixMarker.Begin();
            if (!m_RootNode.Valid || m_RootNode.Inputs.Count <= 0)
                throw new InvalidOperationException("DSPGraph has not been initalized");

            SyncPreviousMix();
            ApplyScheduledCommands();

            if (GraphTraversal.Count == 0)
                BuildTraversalCache(executionMode);

            *m_LastReadLength = frameCount;
            if (LastReadLength == 0 || LastReadLength > DSPBufferSize)
                *m_LastReadLength = DSPBufferSize;

            if (EnumHasFlags(executionMode, ExecutionMode.Synchronous))
                BeginMixSynchronous();
            else
                BeginMixJobified(executionMode);
            ProfilerMarkers.BeginMixMarker.End();
        }

        /// <summary>
        /// Read the samples from the mix. Always call after BeginMix.
        /// </summary>
        /// <remarks>This method will be bursted if the owning IAudioOutputJob is decorated with BurstCompileAttribute</remarks>
        /// <param name="buffer">A Float array of the samples</param>
        /// <param name="frameCount">The number of frames to read</param>
        /// <param name="channelCount">The number of channels to read</param>
        public void ReadMix(NativeArray<float> buffer, int frameCount, int channelCount)
        {
            ValidateBufferSize(buffer, frameCount, channelCount);
            if (frameCount != LastReadLength)
                throw new InvalidOperationException($"Incompatible buffer passed to ReadMix, buffer of size {frameCount * channelCount} does not match previous read length {LastReadLength * channelCount}");

            ProfilerMarkers.ReadMixMarker.Begin();
            SyncPreviousMix();
            UnsafeUtility.MemCpy(buffer.GetUnsafePtr(), *(float**)m_RootBuffer.Data, frameCount * channelCount * UnsafeUtility.SizeOf<float>());
            ProfilerMarkers.ReadMixMarker.End();
        }

        /// <summary>
        /// Create and return a DSPGraph container
        /// </summary>
        /// <param name="outputFormat">SoundFormat of type Enum whose values are: Raw, Mono, Stereo, Quad, Surround, FiveDot1 or SevenDot1</param>
        /// <param name="outputChannels">Specify the number of output channels</param>
        /// <param name="dspBufferSize">Specify the buffer size </param>
        /// <param name="sampleRate">Specify the sample rate of the graph </param>
        /// <returns>A DSPGraph object</returns>
        public static DSPGraph Create(SoundFormat outputFormat, int outputChannels, int dspBufferSize, int sampleRate)
        {
            switch (outputFormat)
            {
                case SoundFormat.SevenDot1:
                    if (outputChannels != 8)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 8 channels");
                    break;
                case SoundFormat.FiveDot1:
                    if (outputChannels != 6)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 6 channels");
                    break;
                case SoundFormat.Surround:
                    if (outputChannels != 5)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 5 channels");
                    break;
                case SoundFormat.Quad:
                    if (outputChannels != 4)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 4 channels");
                    break;
                case SoundFormat.Stereo:
                    if (outputChannels != 2)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 2 channels");
                    break;
                case SoundFormat.Mono:
                    if (outputChannels != 1)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires exactly 1 channel");
                    break;
                case SoundFormat.Raw:
                    if (outputChannels < 2)
                        throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Output format {outputFormat} requires at least 2 channels");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outputFormat), $"Unknown output format {outputFormat}");
            }
            if (outputChannels < 1)
                throw new ArgumentOutOfRangeException(nameof(outputChannels), $"Invalid output channel count {outputChannels}");
            if (dspBufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(dspBufferSize), $"Invalid DSP buffer size {dspBufferSize}");
            if ((dspBufferSize % outputChannels) != 0)
                throw new ArgumentOutOfRangeException(nameof(dspBufferSize), "DSP buffer size must be a multiple of the channel count");
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), $"Invalid sample rate {sampleRate}");

            var graph = new DSPGraph(outputFormat, outputChannels, dspBufferSize, sampleRate);
            graph.Register();
            return graph;
        }

        /// <summary>
        /// Method to clean up resources after execution
        /// </summary>
        /// <remarks>This method will be bursted if the owning IAudioOutputJob is decorated with BurstCompileAttribute</remarks>
        public void Dispose()
        {
            // Delegate the actual disposal to managed code, so that we can manipulate gchandles etc.
            new FunctionPointer<Trampoline>(m_DisposeFunctionPointer).Invoke(ref this);
        }

        /// <summary>
        /// Create and return a <see cref="DSPCommandBlock"/> object that is used to pass commands to the DSPGraph.
        /// A command block queues commands which are submitted atomically to the graph once Complete() is called.
        /// </summary>
        /// <returns>A DSPCommandBlock object</returns>
        public DSPCommandBlock CreateCommandBlock()
        {
            return new DSPCommandBlock(this);
        }

        /// <summary>
        /// Method to get the root <see cref="DSPNode"/> of the DSPGraph its called on.
        /// </summary>
        /// <value>A DSPNode object</value>
        public DSPNode RootDSP => m_RootNode;

        /// <summary>
        /// The number of samples that was processed since the <see cref="DSPGraph"/> was created
        /// </summary>
        /// <value>The current DSP clock time</value>
        public long DSPClock => *m_DSPClock;

        /// <summary>
        /// The sample rate being used for the graph
        /// </summary>
        public int SampleRate => m_SampleRate;

        /// <summary>
        /// The DSP buffer size being used for the graph
        /// </summary>
        public int DSPBufferSize => m_DSPBufferSize;

        /// <summary>
        /// The number of channels being output by the graph
        /// </summary>
        public int OutputChannelCount => m_OutputChannelCount;

        /// <summary>
        /// The sound format being output by the graph
        /// </summary>
        public SoundFormat OutputFormat => m_OutputFormat;

        /// <summary>
        /// Adds an event handler to the <see cref="DSPNode"/> in DSPGraph. This callback is invoked asynchronously on the main thread after the DSPNode posts a matching event.
        /// </summary>
        /// <param name="handler"></param>
        /// <typeparam name="TNodeEvent"></typeparam>
        /// <returns>An event handler ID</returns>
        public int AddNodeEventHandler<TNodeEvent>(Action<DSPNode, TNodeEvent> handler) where TNodeEvent : unmanaged
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            NodeEventCallback callbackWrapper = new NodeEventWrapper<TNodeEvent>(handler).InvokeCallback;
            return RegisterNodeEventHandler<TNodeEvent>(GCHandle.Alloc(callbackWrapper));
        }

        public delegate void NodeEventCallback(DSPNode node, void* nodeEventPointer);

        internal struct NodeEventWrapper<TNodeEvent>
            where TNodeEvent : unmanaged
        {
            private readonly Action<DSPNode, TNodeEvent> m_Callback;

            public NodeEventWrapper(Action<DSPNode, TNodeEvent> callback)
            {
                m_Callback = callback;
            }

            public void InvokeCallback(DSPNode node, void* nodeEventPointer)
            {
                m_Callback.Invoke(node, *(TNodeEvent*)nodeEventPointer);
            }
        }

        /// <summary>
        /// Remove the event handler on the <see cref="DSPNode"/> to stop receiving updates.
        /// </summary>
        /// <param name="handlerId">Handler ID that needs to be removed</param>
        /// <returns>True if the handle exists and was removed else False</returns>
        public bool RemoveNodeEventHandler(int handlerId)
        {
            return UnregisterNodeEventHandler(handlerId);
        }

        /// <summary>
        /// Called every frame as part of MonoBehavior.Update
        /// </summary>
        public void Update()
        {
            var nodesPendingDisposal = NodesPendingDisposal;
            while (!nodesPendingDisposal.IsEmpty)
            {
                DSPNode* node = nodesPendingDisposal.Dequeue();
                RunDSPNodeDisposeJob(*node);
                TemporaryNodeAllocator.Release(node);
            }
            this.InvokePendingCallbacks();
        }

        /// <summary>
        /// A container for mixer thread operations
        /// </summary>
        public struct OutputMixerHandle
        {
            private readonly DSPGraph m_Graph;

            internal OutputMixerHandle(DSPGraph graph)
            {
                m_Graph = graph;
            }

            /// <summary>
            /// This method is called before reading the samples from the mix
            /// </summary>
            /// <param name="frameCount">The number of frames to mix</param>
            /// <param name="executionMode">The execution mode to be used for audio kernels</param>
            /// <remarks>This method will be bursted if the owning IAudioOutputJob is decorated with BurstCompileAttribute</remarks>
            public void BeginMix(int frameCount, ExecutionMode executionMode = ExecutionMode.Jobified)
            {
                m_Graph.BeginMix(frameCount, executionMode);
            }

            /// <summary>
            /// Read the samples from the mix. Always call after BeginMix.
            /// </summary>
            /// <remarks>This method will be bursted if the owning IAudioOutputJob is decorated with BurstCompileAttribute</remarks>
            /// <param name="buffer">A Float array of the samples</param>
            /// <param name="frameCount">The number of frames to read</param>
            /// <param name="channelCount">The number of channels to read</param>
            public void ReadMix(NativeArray<float> buffer, int frameCount, int channelCount)
            {
                m_Graph.ReadMix(buffer, frameCount, channelCount);
            }
        }

        private static void ValidateBufferSize(NativeArray<float> buffer, int frameCount, int channelCount)
        {
            if (frameCount * channelCount > buffer.Length)
                throw new ArgumentOutOfRangeException($"Buffer of size {buffer.Length} is not large enough to read {frameCount} frames x {channelCount} channels");
        }

        public bool Equals(DSPGraph other)
        {
            return Handle.Equals(other.Handle);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPGraph other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Handle.GetHashCode();
        }

        /// <summary>
        /// Enum for specifying how the mix should be jobified
        /// </summary>
        [Flags]
        public enum ExecutionMode
        {
            /// <summary>
            /// Normal execution mode, where audio kernels are executed via Unity's job system
            /// </summary>
            Jobified = 1 << 0,

            /// <summary>
            /// Audio kernels are executed synchronously on the audio mixer thread
            /// </summary>
            Synchronous = 1 << 1,

            /// <summary>
            /// Subgraphs with no outputs will be executed
            /// </summary>
            ExecuteNodesWithNoOutputs = 1 << 2,
        }
    }
}
