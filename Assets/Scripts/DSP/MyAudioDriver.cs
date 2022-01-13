using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Audio;

[BurstCompile(CompileSynchronously=true)]
public struct MyAudioDriver : IAudioOutput
{
    public DSPGraph Graph;
    int m_ChannelCount;
    private bool m_FirstMix;

    public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
    {
        m_ChannelCount = channelCount;
        m_FirstMix = true;
    }

    public void BeginMix(int frameCount)
    {
        if (!m_FirstMix)
            return;
        m_FirstMix = false;
        Graph.OutputMixer.BeginMix(frameCount, DSPGraph.ExecutionMode.Jobified | DSPGraph.ExecutionMode.ExecuteNodesWithNoOutputs);
    }

    public void EndMix(NativeArray<float> output, int frames)
    {
        Graph.OutputMixer.ReadMix(output, frames, m_ChannelCount);
        Graph.OutputMixer.BeginMix(frames);
    }

    public void Dispose()
    {
        Graph.Dispose();
    }
}
