using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;

namespace Unity.Audio
{
    /// <summary>
    /// A simple <see cref="IAudioOutput"/> implementation that writes the output from a single graph into the provided output buffer
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct DefaultDSPGraphDriver : IAudioOutput
    {
        /// <summary>
        /// The graph that will be mixed
        /// </summary>
        public DSPGraph Graph;

        int m_ChannelCount;
        private bool m_FirstMix;
#if !UNITY_2020_2_OR_NEWER
        [NativeDisableContainerSafetyRestriction]
        NativeArray<float> m_DeinterleavedBuffer;
#endif

        /// <summary>
        /// <see cref="IAudioOutput.Initialize"/>
        /// </summary>
        /// <param name="channelCount"></param>
        /// <param name="format"></param>
        /// <param name="sampleRate"></param>
        /// <param name="dspBufferSize"></param>
        public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
        {
            m_ChannelCount = channelCount;
#if !UNITY_2020_2_OR_NEWER
            m_DeinterleavedBuffer = new NativeArray<float>((int)(dspBufferSize * channelCount), Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
#endif
            m_FirstMix = true;
        }

        /// <summary>
        /// Calls <see cref="DSPGraph.OutputMixerHandle.BeginMix"/> on <see cref="Graph"/>
        /// </summary>
        /// <param name="frameCount">The number of frames to be mixed</param>
        public void BeginMix(int frameCount)
        {
            if (!m_FirstMix)
                return;
            m_FirstMix = false;
            Graph.OutputMixer.BeginMix(frameCount);
        }

        /// <summary>
        /// Calls <see cref="DSPGraph.OutputMixerHandle.ReadMix"/> on <see cref="Graph"/> and writes its output into the provided buffer
        /// </summary>
        /// <param name="output">The output buffer to be used. Contents will be overwritten.</param>
        /// <param name="frames">The number of frames to be mixed</param>
        public unsafe void EndMix(NativeArray<float> output, int frames)
        {
#if UNITY_2020_2_OR_NEWER
            // Interleaving happens in the output hook manager
            Graph.OutputMixer.ReadMix(output, frames, m_ChannelCount);
#else
            Graph.OutputMixer.ReadMix(m_DeinterleavedBuffer, frames, m_ChannelCount);
            Utility.InterleaveAudioStream((float*)m_DeinterleavedBuffer.GetUnsafeReadOnlyPtr(), (float*)output.GetUnsafePtr(), frames, m_ChannelCount);
#endif
            Graph.OutputMixer.BeginMix(frames);
        }

        /// <summary>
        /// Dispose <see cref="Graph"/>
        /// </summary>
        public void Dispose()
        {
            Graph.Dispose();
#if !UNITY_2020_2_OR_NEWER
            if (m_DeinterleavedBuffer.IsCreated)
                m_DeinterleavedBuffer.Dispose();
#endif
        }
    }
}
