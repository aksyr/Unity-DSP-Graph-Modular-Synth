using System;

namespace Unity.Audio
{
    /// <summary>
    /// A context for execution of DSP nodes, providing
    /// I/O operations and information.
    /// It is used inside <see cref="IAudioKernel{TParameters,TProviders}.Execute"/>
    /// </summary>
    /// <see cref="IAudioKernel{TParameters,TProviders}"/>
    public unsafe struct ExecuteContext<TParameters, TProviders>
        where TParameters : unmanaged, Enum
        where TProviders  : unmanaged, Enum
    {
        /// <summary>
        /// The amount of samples that has been processed for this DSP graph so far.
        /// This is a monotonic clock representing an abstract playback position
        /// in a DSP graph. This value is shared for all nodes in a mixdown.
        /// </summary>
        public long DSPClock { get; internal set; }

        /// <summary>
        /// The amount of samples being processed in this mix down.
        /// <see cref="Inputs"/> and <see cref="Outputs"/>, the array size of
        /// the I/O buffers will be of this size, and after this mixdown,
        /// the <see cref="DSPClock"/> will increase by this amount.
        /// </summary>
        public int DSPBufferSize { get; internal set; }

        /// <summary>
        /// The sample rate currently being used in this DSP graph.
        /// </summary>
        public int SampleRate { get; internal set; }

        /// <summary>
        /// Posts an event back to the main thread, to any handlers listening
        /// for this event.
        /// <see cref="DSPGraph.AddNodeEventHandler{T}"/>
        /// </summary>
        /// <param name="eventMsg"></param>
        /// <typeparam name="TNodeEvent">Value and type of this event.</typeparam>
        /// <remarks>This function is currently not Burst compatible</remarks>
        public void PostEvent<TNodeEvent>(TNodeEvent eventMsg) where TNodeEvent : struct
        {
            GraphBuffer[GraphIndex].PostEvent(NodeIndex, eventMsg);
        }

        internal int GraphIndex;
        internal int NodeIndex;
        internal DSPGraph* GraphBuffer;

        /// <summary>
        /// An array of read-only input buffers of audio samples.
        /// This is incoming samples from other DSP nodes.
        /// </summary>
        public SampleBufferArray               Inputs;

        /// <summary>
        /// An array of write-only output buffers of audio samples.
        /// These buffers will later on be used from other DSP nodes.
        /// </summary>
        public SampleBufferArray               Outputs;

        /// <summary>
        /// A context providing interpolated parameter values.
        /// <see cref="IAudioKernel{TParameters,TProviders}"/>
        /// </summary>
        public ParameterData<TParameters>          Parameters;

        /// <summary>
        /// A container holding the available sample providers for this audio job.
        /// This is where you get access to sample playback.
        /// <see cref="SampleProvider"/>
        /// <seealso cref="IAudioKernel{TParameters,TProviders}"/>
        /// </summary>
        public SampleProviderContainer<TProviders> Providers;
    }
}
