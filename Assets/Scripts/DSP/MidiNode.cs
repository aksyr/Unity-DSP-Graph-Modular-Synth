using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using System.Runtime.InteropServices;
using System;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

[BurstCompile]
public struct MidiNode : IAudioKernel<MidiNode.Parameters, MidiNode.Providers>
{
    public enum Parameters
    {
    }

    public enum Providers
    {
    }

    NativeArray<byte> _HeldNotes;
    int _HeldNotesCount;
    NativeArray<byte> _Notes;
    NativeArray<bool> _Gates;
    NativeArray<PulseGenerator> _Retrigger;
    bool _Pedal;
    int _RotateChannelIndex;

    public void Initialize()
    {
        _HeldNotes = new NativeArray<byte>(128, Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
        _HeldNotesCount = 0;
        _Notes = new NativeArray<byte>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
        _Gates = new NativeArray<bool>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
        _Retrigger = new NativeArray<PulseGenerator>(16, Allocator.AudioKernel, NativeArrayOptions.ClearMemory);
        _Pedal = false;
        _RotateChannelIndex = -1;
    }

    public void Execute(ref ExecuteContext<Parameters, Providers> context)
    {
        if (context.Outputs.Count != 3) return;

        SampleBuffer gatesOutput = context.Outputs.GetSampleBuffer(0);
        var gatesOutputBuffer = gatesOutput.Buffer;
        SampleBuffer notesOutput = context.Outputs.GetSampleBuffer(1);
        var notesOutputBuffer = notesOutput.Buffer;
        SampleBuffer triggerOutput = context.Outputs.GetSampleBuffer(2);
        var triggerOutputBuffer = triggerOutput.Buffer;

        while (true)
        {
            ulong data = DequeueIncomingData();
            if (data == 0) break;
            MidiJack.MidiMessage message = new MidiJack.MidiMessage(data);
            ProcessMessage(message);
        }

        float sampleDuration = 1.0f / context.SampleRate;
        int samplesCount = gatesOutput.Samples;
        for (int s = 0; s < samplesCount; ++s)
        {
            for (int c = 0; c < math.min(16, gatesOutput.Channels); ++c)
            {
                gatesOutputBuffer[s * gatesOutput.Channels + c] = _Gates[c] ? 1.0f : 0.0f;
            }

            for (int c = 0; c < math.min(16, notesOutput.Channels); ++c)
            {
                notesOutputBuffer[s * notesOutput.Channels + c] = (_Notes[c] - 60f) / 12f;
            }

            for(int c=0; c<math.min(16, triggerOutput.Channels); ++c)
            {
                unsafe
                {
                    PulseGenerator* retrigger = (PulseGenerator*)((byte*)_Retrigger.GetUnsafePtr() + UnsafeUtility.SizeOf<PulseGenerator>() * c);
                    triggerOutputBuffer[s * triggerOutput.Channels + c] = retrigger->Process(sampleDuration) ? 1.0f : 0.0f;
                }
            }
        }
    }

    void ProcessMessage(MidiJack.MidiMessage message)
    {
        var midiStatusCode = message.status >> 4;
        var midiChannelNumber = message.status & 0xf;
        switch (midiStatusCode)
        {
            case 0x8:
                ReleaseNote(message.data1);
                break;
            case 0x9:
                if(message.data2 > 0)
                {
                    int channel = AssignChannel(message.data1);
                    PressNote(message.data1, channel);
                }
                else
                {
                    ReleaseNote(message.data1);
                }
                break;
            case 0xA:
                // aftertouch
                break;
            case 0xB:
                // CC (knobs)
                break;
            case 0xC:
                // program change
                break;
            case 0xD:
                // aftertouch
                break;
            case 0xE:
                // pitch wheel
                break;
        }
    }

    void PressNote(byte note, int channel)
    {
        int index = -1;
        for (int i = 0; i < _HeldNotesCount; ++i)
        {
            if(_HeldNotes[i] == note)
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            if(_HeldNotesCount == 1)
            {
                _HeldNotesCount = 0;
            }
            else
            {
                --_HeldNotesCount;
                _HeldNotes[index] = _HeldNotes[_HeldNotesCount];
            }
        }

        //Debug.Log("pressing note " + note + " at channel " + channel);

        _HeldNotes[_HeldNotesCount] = note;
        ++_HeldNotesCount;

        _Notes[channel] = note;
        _Gates[channel] = true;
        unsafe
        {
            PulseGenerator* retrigger = (PulseGenerator*)((byte*)_Retrigger.GetUnsafePtr() + UnsafeUtility.SizeOf<PulseGenerator>() * channel);
            retrigger->Trigger();
        }
    }

    void ReleaseNote(byte note)
    {
        int index = -1;
        for (int i = 0; i < _HeldNotesCount; ++i)
        {
            if (_HeldNotes[i] == note)
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            if (_HeldNotesCount == 1)
            {
                _HeldNotesCount = 0;
            }
            else
            {
                --_HeldNotesCount;
                _HeldNotes[index] = _HeldNotes[_HeldNotesCount];
            }
        }

        if (_Pedal) return;

        for(int i=0; i<16; ++i)
        {
            if(_Notes[i] == note)
            {
                _Gates[i] = false;
                //Debug.Log("releasing note " + note);
            }
        }
    }

    int AssignChannel(byte note)
    {
        for(int i=0; i<16; ++i)
        {
            _RotateChannelIndex = (_RotateChannelIndex + 1) % 16;
            if(_Gates[_RotateChannelIndex] == false)
            {
                return _RotateChannelIndex;
            }
        }
        _RotateChannelIndex = (_RotateChannelIndex + 1) % 16;
        return _RotateChannelIndex;
    }

    public void Dispose()
    {
        if (_HeldNotes.IsCreated) _HeldNotes.Dispose();
        if(_Notes.IsCreated) _Notes.Dispose();
        if (_Gates.IsCreated) _Gates.Dispose();
        if (_Retrigger.IsCreated) _Retrigger.Dispose();
    }

    [DllImport("MidiJackPlugin", EntryPoint = "MidiJackDequeueIncomingData")]
    public static extern ulong DequeueIncomingData();
}
