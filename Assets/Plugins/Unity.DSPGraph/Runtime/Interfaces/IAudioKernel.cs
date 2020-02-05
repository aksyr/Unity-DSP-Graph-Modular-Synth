using System;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// The interface for creating an audio kernel ("DSP node") for use in the DSPGraph.
    /// An audio kernel has access to audio inputs and outputs (<see cref="DSPCommandBlock"/>
    /// for configuration), and can alter the audio stream.
    /// The kernel must be a struct whose memory will be persistent and managed
    /// by the DSPGraph.
    /// </summary>
    /// <typeparam name="TParameters">
    /// An enum describing the interpolated parameters available for this audio kernel.
    /// Use these to create smooth transitions and variations in your DSP code.
    /// Each enum value will create a parameter internally, and act as an
    /// identifier in other methods.
    /// <seealso cref="ParameterData{TParameters}"/>
    /// </typeparam>
    /// <typeparam name="TProviders">
    /// An enum describing the available sample providers for this audio kernel.
    /// Use these to provide sample playback in a DSP node.
    /// Each enum value will create a sample provider [array] internally, and act as
    /// an identifier in other methods.
    /// <seealso cref="SampleProvider"/>
    /// </typeparam>
    /// <seealso cref="DSPGraph"/>
    [JobProducerType(typeof(AudioKernelExtensions.AudioKernelJobStructProduce< , , >))]
    public interface IAudioKernel<TParameters, TProviders>
        where TParameters : unmanaged, Enum
        where TProviders  : unmanaged, Enum
    {
        /// <summary>
        /// This function is called initially before processing is started.
        /// It can be used as a constructor.
        /// </summary>
        void Initialize();

        /// <summary>
        /// This function is called during a mix - <see cref="DSPGraph.OutputMixerHandle.BeginMix"/>,
        /// in a potentially multi threaded context.
        /// Inside this function you can process the audio inputs given to you,
        /// and write to the outputs.
        /// <see cref="ExecuteContext{TParameters,TProviders}"/>
        /// </summary>
        /// <param name="context">
        /// The context will give access to these buffers,
        /// in addition to the sample providers and parameters that have been
        /// registered for this audio job.
        /// </param>
        void Execute(ref ExecuteContext<TParameters, TProviders> context);

        /// <summary>
        /// This function is called just before a job is destroyed.
        /// </summary>
        void Dispose();
    }
}
