using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
public struct MergeNode : IAudioKernel<MergeNode.Parameters, MergeNode.Providers>
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

        //SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        //var outputBuffer = output.Buffer;

        //int count = math.min(output.Channels, context.Inputs.Count);
        //for(int s=0; s<output.Samples; ++s)
        //{
        //    for (int c = 0; c < count; ++c)
        //    {
        //        SampleBuffer input = context.Inputs.GetSampleBuffer(c);
        //        Debug.Assert(input.Channels == 1);
        //        outputBuffer[s * output.Channels + c] = input.Buffer[s];
        //    }
        //}

        SampleBuffer output = context.Outputs.GetSampleBuffer(0);

        int count = math.min(output.Channels, context.Inputs.Count);
        for (int c = 0; c < count; ++c)
        {
            var outputBuffer = output.GetBuffer(0);
            SampleBuffer input = context.Inputs.GetSampleBuffer(c);
            Debug.Assert(input.Channels == 1);
            var inputBuffer = input.GetBuffer(0);

            for(int s=0; s<output.Samples; ++s)
            {
                outputBuffer[s] = inputBuffer[s];
            }
        }
    }

    public void Dispose()
    {
    }
}
