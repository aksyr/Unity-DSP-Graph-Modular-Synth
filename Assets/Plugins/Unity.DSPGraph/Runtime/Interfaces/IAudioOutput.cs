using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    /// <summary>
    /// Output job for a DSPGraph
    /// </summary>
    [JobProducerType(typeof(AudioOutputExtensions.AudioOutputHookStructProduce<>))]
    public interface IAudioOutput
    {
        void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize);
        void BeginMix(int frameCount);
        void EndMix(NativeArray<float> output, int frames);
        void Dispose();
    }
}
