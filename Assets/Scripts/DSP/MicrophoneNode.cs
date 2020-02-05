using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

[BurstCompile]
public struct MicrophoneNode : IAudioKernel<MicrophoneNode.Parameters, MicrophoneNode.Providers>
{
    public enum Parameters { Rate }
    public enum Providers { }

    [NativeDisableContainerSafetyRestriction]
    public NativeArray<float> MicrophoneBuffer;

    public int Position;

    public bool Playing;
    public bool WasPlaying;
    public int posToStart;

    public void Initialize()
    {
        MicrophoneBuffer = new NativeArray<float>(48000, Allocator.AudioKernel);
        Position = 0;
        Playing = false;
        WasPlaying = false;
    }

    long mymod(long x, long m)
    {
        return (x % m + m) % m;
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (Playing)
        {
            if(WasPlaying == false)
            {
                Position = (int)mymod(context.DSPClock - posToStart, MicrophoneBuffer.Length);
                WasPlaying = true;
            }

            if (context.Outputs.Count == 0) return;

            int samplesCount = context.Outputs.GetSampleBuffer(0).Samples;

            for (int i = 0; i < samplesCount; ++i)
            {
                for (int o = 0; o < context.Outputs.Count; ++o)
                {
                    SampleBuffer output = context.Outputs.GetSampleBuffer(o);
                    NativeArray<float> outputBuffer = output.Buffer;
                    for (int j = 0; j < output.Channels; ++j)
                    {
                        outputBuffer[(i * output.Channels) + j] = MicrophoneBuffer[Position];
                    }
                }

                ++Position;
                if (Position >= MicrophoneBuffer.Length)
                {
                    Position = 0;
                }
            }
        }
    }

    public void Dispose()
    {
        if (MicrophoneBuffer.IsCreated)
            MicrophoneBuffer.Dispose();
    }
}

[BurstCompile]
public struct MicrophoneNodeKernel : IAudioKernelUpdate<MicrophoneNode.Parameters, MicrophoneNode.Providers, MicrophoneNode>
{
    NativeArray<float> _Buffer;

    public MicrophoneNodeKernel(NativeArray<float> buffer)
    {
        _Buffer = buffer;
    }

    public void Update(ref MicrophoneNode audioKernel)
    {
        audioKernel.MicrophoneBuffer.CopyFrom(_Buffer);
    }
}

[BurstCompile]
public struct MicrophoneNodePlayKernel : IAudioKernelUpdate<MicrophoneNode.Parameters, MicrophoneNode.Providers, MicrophoneNode>
{
    int startPos;

    public MicrophoneNodePlayKernel(int startPos)
    {
        this.startPos = startPos;
    }

    public void Update(ref MicrophoneNode audioKernel)
    {
        audioKernel.Playing = true;
        audioKernel.posToStart = startPos;
    }
}
