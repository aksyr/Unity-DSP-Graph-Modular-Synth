using System;
using Unity.Burst;
using Random = Unity.Mathematics.Random;

namespace Unity.Audio
{
    [BurstCompile(CompileSynchronously = true)]
    public struct NoiseFilter : IAudioKernel<NoiseFilter.Parameters, NoiseFilter.Providers>
    {
        public enum Parameters
        {
            [ParameterDefault(0.0f)] [ParameterRange(-1.0f, 1.0f)]
            Offset
        }

        public enum Providers
        {
        }

        Random m_Random;

        public void Initialize()
        {
        }

        public void Execute(ref ExecuteContext<Parameters, Providers> context)
        {
            if (context.Outputs.Count == 0)
                return;

            if (m_Random.state == 0)
                m_Random.InitState(2747636419u);

            var outputBuffer = context.Outputs.GetSampleBuffer(0).Buffer;
            var outputChannels = context.Outputs.GetSampleBuffer(0).Channels;

            var inputCount = context.Inputs.Count;
            for (var i = 0; i < inputCount; i++)
            {
                var inputBuff = context.Inputs.GetSampleBuffer(i).Buffer;
                for (var s = 0; s < outputBuffer.Length; s++)
                    outputBuffer[s] += inputBuff[s];
            }

            var frames = outputBuffer.Length / outputChannels;
            var parameters = context.Parameters;
            for (int s = 0, i = 0; s < frames; s++)
            {
                for (var c = 0; c < outputChannels; c++)
                    outputBuffer[i++] += m_Random.NextFloat() * 2.0f - 1.0f + parameters.GetFloat(Parameters.Offset, s);
            }
        }

        public void Dispose()
        {
        }
    }
}
