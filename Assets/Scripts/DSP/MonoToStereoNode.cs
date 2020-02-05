using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
public struct MonoToStereoNode : IAudioKernel<MonoToStereoNode.Parameters, MonoToStereoNode.Providers>
{
    public enum Parameters
    {
    }

    public enum Providers
    {

    }

    public void Initialize()
    {
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count == 0 || context.Outputs.Count == 0) return;

        SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        Debug.Assert(output.Channels == 2);
        var outputBuffer = output.Buffer;

        int count = math.min(2, context.Inputs.Count);
        for(int s=0; s<output.Samples; ++s)
        {
            for(int c=0; c<count; ++c)
            {
                SampleBuffer input = context.Inputs.GetSampleBuffer(c);
                Debug.Assert(input.Channels == 1);
                outputBuffer[s * output.Channels+c] = input.Buffer[s];
            }
        }
    }

    public void Dispose()
    {
    }
}
