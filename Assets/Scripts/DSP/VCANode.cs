using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct VCANode : IAudioKernel<VCANode.Parameters, VCANode.Providers>
{
    public enum Parameters
    {
        Multiplier,
    }

    public enum Providers
    {
    }

    public void Initialize()
    {
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count != 2 || context.Outputs.Count != 1) return;

        SampleBuffer voltage = context.Inputs.GetSampleBuffer(0);
        SampleBuffer input = context.Inputs.GetSampleBuffer(1);
        SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        var voltageBuffer = voltage.Buffer;
        var inputBuffer = input.Buffer;
        var outputBuffer = output.Buffer;

        int samplesCount = voltage.Samples;
        int channelsCount = math.min(math.min(voltage.Channels, input.Channels), output.Channels);

        for(int s=0; s<samplesCount; ++s)
        {
            float multiplier = context.Parameters.GetFloat(Parameters.Multiplier, s);
            for(int c=0; c<channelsCount; ++c)
            {
                outputBuffer[s * output.Channels + c] = voltageBuffer[s * voltage.Channels + c] * inputBuffer[s * output.Channels + c] * multiplier;
            }
        }
    }

    public void Dispose()
    {
    }
}
