using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.CodeEditor;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
public struct AttenuatorNode : IAudioKernel<AttenuatorNode.Parameters, AttenuatorNode.Providers>
{
    public enum Parameters
    {
        Multiplier
    }
    public enum Providers
    {
    }

    public void Initialize()
    {
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count != 1 && context.Outputs.Count != 1) return;

        SampleBuffer input = context.Inputs.GetSampleBuffer(0);
        SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        NativeArray<float> inputBuffer = input.Buffer;
        NativeArray<float> outputBuffer = output.Buffer;

        int channelsCount = math.min(input.Channels, output.Channels);
        for(int s=0; s<input.Samples; ++s)
        {
            float multiplier = context.Parameters.GetFloat(Parameters.Multiplier, s);
            for(int c=0; c<channelsCount; ++c)
            {
                outputBuffer[s * output.Channels + c] = inputBuffer[s * input.Channels + c] * multiplier;
            }
        }
    }

    public void Dispose()
    {
    }
}
