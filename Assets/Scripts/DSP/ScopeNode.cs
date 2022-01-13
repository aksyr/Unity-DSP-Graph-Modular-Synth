using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
public struct ScopeNode : IAudioKernel<ScopeNode.Parameters, ScopeNode.Providers>
{
    public enum Parameters
    {
        Time,
        TriggerTreshold
    }

    public enum Providers
    {
    }

    public const int BUFFER_SIZE = 512;
    public const int MAX_CHANNELS = 16;

    private NativeArray<float> _BufferX;
    private int _InputChannelsX;

    private NativeArray<SchmittTrigger> _Triggers;

    private float _TriggerThreshold;
    private int _BufferIdx;
    private int _StepIdx;
    private float _WaitingTime;

    private NativeArray<float> _Accumulators;

    public NativeArray<float> BufferX {
        get { return _BufferX; }
    }
    public int InputChannelsX {
        get { return _InputChannelsX; }
    }
    public float TriggerThreshold {
        get { return _TriggerThreshold; }
    }
    public int BufferIdx {
        get { return _BufferIdx; }
    }

    public void Initialize()
    {
        _InputChannelsX = 0;
        _BufferX = new NativeArray<float>(MAX_CHANNELS * BUFFER_SIZE, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
        _Triggers = new NativeArray<SchmittTrigger>(MAX_CHANNELS, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
        unsafe
        {
            byte* triggersPtr = (byte*)_Triggers.GetUnsafePtr();
            for (int i = 0; i < _Triggers.Length; ++i)
            {
                SchmittTrigger* trigger = (SchmittTrigger*)(triggersPtr + UnsafeUtility.SizeOf<SchmittTrigger>() * i);
                trigger->Reset();
            }
        }
        _BufferIdx = 0;
        _StepIdx = 0;
        _WaitingTime = 0f;
        _Accumulators = new NativeArray<float>(MAX_CHANNELS, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        //if (context.Inputs.Count < 1) return;

        //SampleBuffer input = context.Inputs.GetSampleBuffer(0);
        //NativeArray<float> inputBuffer = input.Buffer;

        //// time
        //float deltaTime = context.Parameters.GetFloat(Parameters.Time, 0); // how much time fits in scope
        //int displayedFramesCount = (int)math.max(math.ceil(context.SampleRate * deltaTime), 1); // how many samples is scope showing
        //int inputFramesCount = input.Samples;// / context.Inputs.GetSampleBuffer(0).Channels;
        //int step = (int)math.ceil((float)displayedFramesCount / (float)inputFramesCount);
        //_StepIdx = _StepIdx%step;


        //// clear buffers
        //Debug.Assert(input.Channels <= MAX_CHANNELS);
        //if (input.Channels != _InputChannelsX)
        //{
        //    _InputChannelsX = input.Channels;
        //    unsafe
        //    {
        //        UnsafeUtility.MemClear(_BufferX.GetUnsafePtr(), _BufferX.Length * sizeof(float));
        //    }
        //    Trigger();
        //}

        //// wait for trigger if buffer is full
        //if (_BufferIdx >= BUFFER_SIZE)
        //{
        //    _TriggerThreshold = context.Parameters.GetFloat(Parameters.TriggerTreshold, 0);
        //    if (!CheckTriggers(ref input, _TriggerThreshold))
        //    {
        //        _WaitingTime += (float)input.Samples / (float)context.SampleRate;
        //        if(_WaitingTime < 0.5f) {
        //            return;
        //        } else {
        //            Trigger();
        //            _StepIdx = 0;
        //        }
        //    }
        //}

        //// add frames to buffer
        //for (; _StepIdx < inputFramesCount; _StepIdx+=step)
        //{
        //    for (int c=0; c<input.Channels; ++c)
        //    {
        //        _BufferX[_BufferIdx + c*BUFFER_SIZE] = inputBuffer[_StepIdx * input.Channels + c];
        //    }

        //    ++_BufferIdx;

        //    if (_BufferIdx >= BUFFER_SIZE)
        //    {
        //        break;
        //    }
        //}

        if (context.Inputs.Count < 1) return;

        SampleBuffer input = context.Inputs.GetSampleBuffer(0);


        // time
        float deltaTime = context.Parameters.GetFloat(Parameters.Time, 0); // how much time fits in scope
        int displayedFramesCount = (int)math.max(math.ceil(context.SampleRate * deltaTime), 1); // how many samples is scope showing
        int inputFramesCount = input.Samples;// / context.Inputs.GetSampleBuffer(0).Channels;
        int step = (int)math.ceil((float)displayedFramesCount / (float)inputFramesCount);
        _StepIdx = _StepIdx % step;


        // clear buffers
        Debug.Assert(input.Channels <= MAX_CHANNELS);
        if (input.Channels != _InputChannelsX)
        {
            _InputChannelsX = input.Channels;
            unsafe
            {
                UnsafeUtility.MemClear(_BufferX.GetUnsafePtr(), _BufferX.Length * sizeof(float));
            }
            Trigger();
        }

        // wait for trigger if buffer is full
        if (_BufferIdx >= BUFFER_SIZE)
        {
            _TriggerThreshold = context.Parameters.GetFloat(Parameters.TriggerTreshold, 0);
            if (!CheckTriggers(ref input, _TriggerThreshold))
            {
                _WaitingTime += (float)input.Samples / (float)context.SampleRate;
                if (_WaitingTime < 0.5f)
                {
                    return;
                }
                else
                {
                    Trigger();
                    _StepIdx = 0;
                }
            }
        }

        // add frames to buffer
        for (; _StepIdx < inputFramesCount; _StepIdx += step)
        {
            for (int c = 0; c < input.Channels; ++c)
            {
                NativeArray<float> inputBuffer = input.GetBuffer(c);
                _BufferX[_BufferIdx + c * BUFFER_SIZE] = inputBuffer[_StepIdx];
            }

            ++_BufferIdx;

            if (_BufferIdx >= BUFFER_SIZE)
            {
                break;
            }
        }
    }

    private bool CheckTriggers(ref SampleBuffer sampleBuffer, float trigThreshold)
    {
        //unsafe
        //{
        //    byte* triggersPtr = (byte*)_Triggers.GetUnsafePtr();
        //    NativeArray<float> buffer = sampleBuffer.Buffer;
        //    for (int s = 0; s < sampleBuffer.Samples; ++s)
        //    {
        //        for (int c = 0; c < sampleBuffer.Channels; ++c)
        //        {
        //            float val = math.remap(trigThreshold, trigThreshold + 0.005f, 0.0f, 1.0f, buffer[s * sampleBuffer.Channels + c]);

        //            SchmittTrigger* trigger = (SchmittTrigger*)(triggersPtr + UnsafeUtility.SizeOf<SchmittTrigger>() * c);
        //            if (trigger->Process(val))
        //            {
        //                Trigger();
        //                _StepIdx = s;
        //                return true;
        //            }
        //        }
        //    }
        //    return false;
        //}

        unsafe
        {
            byte* triggersPtr = (byte*)_Triggers.GetUnsafePtr();
            //NativeArray<float> buffer = sampleBuffer.Buffer;
            for (int s = 0; s < sampleBuffer.Samples; ++s)
            {
                for (int c = 0; c < sampleBuffer.Channels; ++c)
                {
                    NativeArray<float> buffer = sampleBuffer.GetBuffer(c);
                    float val = math.remap(trigThreshold, trigThreshold + 0.005f, 0.0f, 1.0f, buffer[s]);

                    SchmittTrigger* trigger = (SchmittTrigger*)(triggersPtr + UnsafeUtility.SizeOf<SchmittTrigger>() * c);
                    if (trigger->Process(val))
                    {
                        Trigger();
                        _StepIdx = s;
                        return true;
                    }
                }
            }
            return false;
        }
    }

    private void Trigger()
    {
        //unsafe
        //{
        //    byte* triggersPtr = (byte*)_Triggers.GetUnsafePtr();
        //    for (int i = 0; i < _Triggers.Length; ++i)
        //    {
        //        SchmittTrigger* trigger = (SchmittTrigger*)(triggersPtr + UnsafeUtility.SizeOf<SchmittTrigger>() * i);
        //        trigger->Reset();
        //    }
        //}
        //unsafe
        //{
        //    UnsafeUtility.MemClear(_Accumulators.GetUnsafePtr(), _Accumulators.Length * UnsafeUtility.SizeOf<float>());
        //}
        //_BufferIdx = 0;
        //_StepIdx = 0;
        //_WaitingTime = 0f;

        unsafe
        {
            byte* triggersPtr = (byte*)_Triggers.GetUnsafePtr();
            for (int i = 0; i < _Triggers.Length; ++i)
            {
                SchmittTrigger* trigger = (SchmittTrigger*)(triggersPtr + UnsafeUtility.SizeOf<SchmittTrigger>() * i);
                trigger->Reset();
            }
        }
        unsafe
        {
            UnsafeUtility.MemClear(_Accumulators.GetUnsafePtr(), _Accumulators.Length * UnsafeUtility.SizeOf<float>());
        }
        _BufferIdx = 0;
        _StepIdx = 0;
        _WaitingTime = 0f;
    }

    public void Dispose()
    {
        if(_BufferX.IsCreated) _BufferX.Dispose();
        if (_Triggers.IsCreated) _Triggers.Dispose();
        if (_Accumulators.IsCreated) _Accumulators.Dispose();
    }
}
