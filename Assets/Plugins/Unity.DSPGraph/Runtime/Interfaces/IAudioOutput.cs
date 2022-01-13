using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// Output job for a DSPGraph
    /// </summary>
    /// <remarks>
    /// It's important to mark audio output jobs for synchronous burst compilation when running in the Unity editor.
    /// Otherwise, they can be executed via mono until burst compilation finishes,
    /// which will make the audio mixer thread subject to pausing for garbage collection for the rest of the Unity session.
    /// </remarks>
    [JobProducerType(typeof(AudioOutputExtensions.AudioOutputHookStructProduce<>))]
    public interface IAudioOutput
    {
        /// <summary>
        /// Called to allow the user to initialize an audio output
        /// </summary>
        /// <param name="channelCount">The channel count for the output</param>
        /// <param name="format">The format of the output buffer</param>
        /// <param name="sampleRate">The sample rate for the output in Hz</param>
        /// <param name="dspBufferSize">The buffer size for the output in samples per channel</param>
        void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize);

        /// <summary>
        /// Called to prompt the user to begin a mix
        /// </summary>
        /// <param name="frameCount">The number of frames per channel to mix</param>
        void BeginMix(int frameCount);

        /// <summary>
        /// Called to prompt the user to write mixing output
        /// </summary>
        /// <param name="output">Mixing output should be written here</param>
        /// <param name="frames">The number of frames per channel to be written</param>
        void EndMix(NativeArray<float> output, int frames);

        /// <summary>
        /// Called to prompt the user to dispose any resources
        /// </summary>
        void Dispose();
    }
}
