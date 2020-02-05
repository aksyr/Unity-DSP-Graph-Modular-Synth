using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct ADSRNode : IAudioKernel<ADSRNode.Parameters, ADSRNode.Providers>
{
    public enum Parameters
    {
        Attack,
        Decay,
        Sustain,
        Release
    }

    public enum Providers
    {
    }

    NativeArray<bool> _Attacking;
    NativeArray<float> _Envelope;

    public void Initialize()
    {
        _Attacking = new NativeArray<bool>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
        _Envelope = new NativeArray<float>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Inputs.Count != 1 && context.Outputs.Count != 1) return;

        SampleBuffer gate = context.Inputs.GetSampleBuffer(0);
        SampleBuffer output = context.Outputs.GetSampleBuffer(0);

        NativeArray<float> gateBuffer = gate.Buffer;
        NativeArray<float> outputBuffer = output.Buffer;

        int channelsCount = math.min(gate.Channels, output.Channels);

        for (int s = 0; s < gate.Samples; ++s)
        {
            float attackParam = math.max(context.Parameters.GetFloat(Parameters.Attack, s), 1e-3f);
            float decayParam = math.max(context.Parameters.GetFloat(Parameters.Decay, s), 1e-3f);
            float sustainParam = context.Parameters.GetFloat(Parameters.Sustain, s);
            float releaseParam = math.max(context.Parameters.GetFloat(Parameters.Release, s), 1e-3f);

            float attackDelta = (1.0f / (attackParam * (float)context.SampleRate));
            float decayDelta = (1.0f - sustainParam) / (decayParam * (float)context.SampleRate);
            float releaseDelta = (sustainParam == 0.0f ? 1.0f : sustainParam) / (releaseParam * (float)context.SampleRate);

            for (int c = 0; c < channelsCount; ++c)
            {
                float gateVal = gateBuffer[s * gate.Channels + c];

                float targetEnv = 0.0f;
                float delta = releaseDelta;
                if(gateVal != 0.0f)
                {
                    if(_Attacking[c])
                    {
                        targetEnv = 1.0f;
                        delta = attackDelta;
                    }
                    else
                    {
                        targetEnv = sustainParam;
                        delta = decayDelta;
                    }
                }

                float sign = math.sign(targetEnv - _Envelope[c]);
                _Envelope[c] = math.clamp(_Envelope[c] + sign * delta, 0f, 1f);

                // turn attack off when envelope reaches high
                if (_Envelope[c] >= 1.0f) _Attacking[c] = false;
                // turn attack on when if gate is off
                if (gateVal <= 0.0f) _Attacking[c] = true;

                outputBuffer[s * output.Channels + c] = _Envelope[c];
            }
        }
    }

    public void Dispose()
    {
        if (_Attacking.IsCreated) _Attacking.Dispose();
        if (_Envelope.IsCreated) _Envelope.Dispose();
    }
}
