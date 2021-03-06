﻿using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Audio;

[BurstCompile]
public struct MyAudioDriver : IAudioOutput
{
    public DSPGraph Graph;
    int m_ChannelCount;

    public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
    {
        m_ChannelCount = channelCount;
    }

    public void BeginMix(int frameCount)
    {
        Graph.OutputMixer.BeginMix(frameCount, DSPGraph.ExecutionMode.Jobified | DSPGraph.ExecutionMode.ExecuteNodesWithNoOutputs);
    }

    public void EndMix(NativeArray<float> output, int frames)
    {
        Graph.OutputMixer.ReadMix(output, frames, m_ChannelCount);
    }

    public void Dispose()
    {
        Graph.Dispose();
    }
}
