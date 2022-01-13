using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

[BurstCompile(CompileSynchronously = true)]
public struct OscilatorNode : IAudioKernel<OscilatorNode.Parameters, OscilatorNode.Providers>
{
    public enum Parameters
    {
        [ParameterDefault(261.63f), ParameterRange(2f, 24000f)]
        Frequency,
        [ParameterDefault(0f), ParameterRange(0f, 4f)]
        Mode,
        [ParameterDefault(0f)]
        FMMultiplier,
        [ParameterDefault(0f), ParameterRange(0f, 1f)]
        Unidirectional
    }

    public enum Providers
    {
    }

    public enum Mode : int
    {
        Sine = 0,
        Triangle,
        Saw,
        Square,
    }

    NativeArray<float> _Phases;
    NativeArray<float> _Pitches;

    public void Initialize()
    {
        _Phases = new NativeArray<float>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
        _Pitches = new NativeArray<float>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Outputs.Count != 1 || context.Inputs.Count != 3) return;

        //SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        //NativeArray<float> outputBuffer = output.Buffer;
        //int samplesCount = output.Samples;

        //SampleBuffer fmInput = context.Inputs.GetSampleBuffer(0);
        //NativeArray<float> fmInputBuffer = fmInput.Buffer;
        //SampleBuffer pitchInput = context.Inputs.GetSampleBuffer(1);
        //NativeArray<float> pitchInputBuffer = pitchInput.Buffer;
        //SampleBuffer resetInput = context.Inputs.GetSampleBuffer(2);
        //NativeArray<float> resetInputBuffer = resetInput.Buffer;

        //for (int s = 0; s < samplesCount; ++s)
        //{
        //    float paramFreq = context.Parameters.GetFloat(Parameters.Frequency, s);
        //    float pitch = math.log(paramFreq / 261.6256f) / math.log(1.0594630943592953f) / 12f;
        //    float fmMultiplier = context.Parameters.GetFloat(Parameters.FMMultiplier, s);
        //    Mode mode = (Mode)(int)math.round(context.Parameters.GetFloat(Parameters.Mode, s));
        //    bool unidirectional = context.Parameters.GetFloat(Parameters.Unidirectional, s) != 0.0f;

        //    unsafe
        //    {
        //        UnsafeUtility.MemCpyReplicate(_Pitches.GetUnsafePtr(), &pitch, sizeof(float), _Pitches.Length);
        //    }

        //    // fm modulation
        //    for(int c=0; c<math.min(fmInput.Channels, _Pitches.Length); ++c)
        //    {
        //        _Pitches[c] += fmInputBuffer[s * fmInput.Channels + c] * fmMultiplier;
        //    }
        //    // pitch
        //    for (int c = 0; c < math.min(pitchInput.Channels, _Pitches.Length); ++c)
        //    {
        //        _Pitches[c] += pitchInputBuffer[s * pitchInput.Channels + c];
        //    }
        //    // reset phase
        //    for(int c=0; c<math.min(resetInput.Channels, _Phases.Length); ++c)
        //    {
        //        if (resetInputBuffer[s * resetInput.Channels + c] != 0f) {
        //            _Phases[c] = 0f;
        //        }
        //    }

        //    for (int c = 0; c < math.min(output.Channels, _Phases.Length); c++)
        //    {
        //        outputBuffer[s * output.Channels + c] = Generate(mode, _Phases[c], unidirectional);

        //        float frequency = 261.6256f * math.pow(2.0f, _Pitches[c] + 30) / 1073741824f;
        //        float delta = frequency / context.SampleRate;

        //        _Phases[c] += delta;
        //        _Phases[c] -= math.floor(_Phases[c]);
        //    }
        //}

        SampleBuffer output = context.Outputs.GetSampleBuffer(0);
        int samplesCount = output.Samples;
        int channelsCount = output.Channels;

        SampleBuffer fmInput = context.Inputs.GetSampleBuffer(0);
        SampleBuffer pitchInput = context.Inputs.GetSampleBuffer(1);
        SampleBuffer resetInput = context.Inputs.GetSampleBuffer(2);



        for (int c = 0; c < channelsCount; ++c)
        {
            NativeArray<float> outputBuffer = output.GetBuffer(c);
            NativeArray<float> fmInputBuffer = fmInput.GetBuffer(c);
            NativeArray<float> pitchInputBuffer = pitchInput.GetBuffer(c);
            NativeArray<float> resetInputBuffer = resetInput.GetBuffer(c);

            for (int s = 0; s < samplesCount; ++s)
            {
                float paramFreq = context.Parameters.GetFloat(Parameters.Frequency, s);
                float pitch = math.log(paramFreq / 261.6256f) / math.log(1.0594630943592953f) / 12f;
                float fmMultiplier = context.Parameters.GetFloat(Parameters.FMMultiplier, s);
                Mode mode = (Mode)(int)math.round(context.Parameters.GetFloat(Parameters.Mode, s));
                bool unidirectional = context.Parameters.GetFloat(Parameters.Unidirectional, s) != 0.0f;

                pitch += fmInputBuffer[s] * fmMultiplier;
                pitch += pitchInputBuffer[s];
                if (resetInputBuffer[s] != 0f)
                {
                    _Phases[c] = 0f;
                }

                outputBuffer[s] = Generate(mode, _Phases[c], unidirectional);

                float frequency = 261.6256f * math.pow(2.0f, pitch + 30) / 1073741824f;
                float delta = frequency / context.SampleRate;

                _Phases[c] += delta;
                _Phases[c] -= math.floor(_Phases[c]);
            }
        }
    }

    public void Dispose()
    {
        if (_Phases.IsCreated) _Phases.Dispose();
        if (_Pitches.IsCreated) _Pitches.Dispose();
    }

    static float Generate(Mode mode, float phase, bool unidirectional)
    {
        float retVal = 0.0f;
        switch (mode)
        {
            case Mode.Sine:
                retVal = SineGenerator(phase);
                break;
            case Mode.Triangle:
                retVal = TriangleGenerator(phase);
                break;
            case Mode.Saw:
                retVal = SawGenerator(phase);
                break;
            case Mode.Square:
                retVal = SquareGenerator(phase);
                break;
        }
        if(unidirectional)
        {
            retVal += 1f;
        }
        return retVal;
    }

    static float SineGenerator(float phase)
    {
        return math.sin(phase * 2 * math.PI);
    }

    static float TriangleGenerator(float phase)
    {
        return (2/math.PI)*math.asin(math.sin(phase * 2 * math.PI));
    }

    static float SawGenerator(float phase)
    {
        return math.fmod(phase, 1f)*2f-1f;
    }

    static float SquareGenerator(float phase)
    {
        return math.sign(math.sin(phase * 2 * math.PI));
    }
}
