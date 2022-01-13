using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
public struct SplitNode : IAudioKernel<SplitNode.Parameters, SplitNode.Providers>
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

        //SampleBuffer input = context.Inputs.GetSampleBuffer(0);

        //int count = math.min(input.Channels, context.Outputs.Count);
        //for(int c=0; c<count; ++c)
        //{
        //    SampleBuffer output = context.Outputs.GetSampleBuffer(c);
        //    Debug.Assert(output.Channels == 1);
        //    var outputbuffer = output.Buffer;
        //    for(int s=0; s<input.Samples; ++s)
        //    {
        //        outputbuffer[s] = input.Buffer[s * input.Channels + c];
        //    }
        //}

        SampleBuffer input = context.Inputs.GetSampleBuffer(0);
        int samples = input.Samples;

        int count = math.min(input.Channels, context.Outputs.Count);
        for (int c = 0; c < count; ++c)
        {
            SampleBuffer output = context.Outputs.GetSampleBuffer(c);
            Debug.Assert(output.Channels == 1);
            var outputBuffer = output.GetBuffer(0);
            var inputBuffer = input.GetBuffer(c);
            for (int s = 0; s < samples; ++s)
            {
                outputBuffer[s] = inputBuffer[s];
            }
        }
    }

    public void Dispose()
    {
    }
}
