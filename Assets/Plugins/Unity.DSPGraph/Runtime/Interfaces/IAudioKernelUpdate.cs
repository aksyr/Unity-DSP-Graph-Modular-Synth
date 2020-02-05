using System;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// The interface for declaring an update kernel, that is capable of
    /// reading/writing to a live, running DSP node in the DSP graph.
    /// This can either be used for reading back data asynchronously from
    /// the DSP graph, or as a method of changing a DSP node in a way that is
    /// not possible using parameters.
    /// See <see cref="DSPCommandBlock.UpdateAudioKernel{TAudioKernelUpdate, TParams, TProvs, TAudioKernel}"/>
    /// or <see cref="DSPCommandBlock.CreateUpdateRequest{TAudioKernelUpdate, TParams, TProvs, TAudioKernel}"/>.
    /// See also <seealso cref="IAudioKernel{TParams,TProvs}"/> for an in depth description of the
    /// the type parameters.
    /// </summary>
    [JobProducerType(typeof(AudioKernelUpdateExtensions.AudioKernelUpdateJobStructProduce< , , , >))]
    public interface IAudioKernelUpdate<TParameters, TProviders, TKernel>
        where TParameters : unmanaged, Enum
        where TProviders  : unmanaged, Enum
        where TKernel     : struct, IAudioKernel<TParameters, TProviders>
    {
        /// <summary>
        /// This method is called when you should perform your task of updating the
        /// audio kernel. The changes done to the audio kernel or yourself persists.
        /// This method is called potentially in a multi threaded context.
        /// </summary>
        /// <param name="audioKernel">
        /// The audio kernel that you're updating.
        /// </param>
        void Update(ref TKernel audioKernel);
    }
}
