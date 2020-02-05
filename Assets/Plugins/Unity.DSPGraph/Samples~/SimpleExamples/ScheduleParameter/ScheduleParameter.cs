using System;
using Unity.Audio;
using UnityEngine;

public class ScheduleParameter : MonoBehaviour
{
    public float Cutoff = 5000.0f;
    public float Q = 1.0f;
    public float Gain;
    
    DSPGraph m_Graph;
    DSPNode m_NoiseFilter;
    DSPNode m_LowPass;

    void Start()
    {
        var format = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(AudioSettings.speakerMode);
        var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
        AudioSettings.GetDSPBufferSize(out var bufferLength, out var numBuffers);
        var sampleRate = AudioSettings.outputSampleRate;

        m_Graph = DSPGraph.Create(format, channels, bufferLength, sampleRate);

        var driver = new DefaultDSPGraphDriver { Graph = m_Graph };
        driver.AttachToDefaultOutput();

        using (var block = m_Graph.CreateCommandBlock())
        {
            m_NoiseFilter = block.CreateDSPNode<NoiseFilter.Parameters, NoiseFilter.Providers, NoiseFilter>();
            block.AddOutletPort(m_NoiseFilter, 2, SoundFormat.Stereo);

            m_LowPass = StateVariableFilter.Create(block, StateVariableFilter.FilterType.Lowpass);

            block.Connect(m_NoiseFilter, 0, m_LowPass, 0);
            block.Connect(m_LowPass, 0, m_Graph.RootDSP, 0);
        }
    }

    void Update()
    {
        m_Graph.Update();
    }

    void OnDestroy()
    {
        using (var block = m_Graph.CreateCommandBlock())
        {
            block.ReleaseDSPNode(m_NoiseFilter);
            block.ReleaseDSPNode(m_LowPass);
        }
    }

    void OnGUI()
    {
        using (var block = m_Graph.CreateCommandBlock())
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(100, 70, 300, 30), "Lowpass Cutoff:");
            var newCutoff = GUI.HorizontalSlider(new Rect(100, 100, 300, 30), Cutoff, 10.0f, 22000.0f);
            if (Math.Abs(newCutoff - Cutoff) > 0.01f)
            {
                block.SetFloat<StateVariableFilter.AudioKernel.Parameters, StateVariableFilter.AudioKernel.Providers, StateVariableFilter.AudioKernel>(m_LowPass, StateVariableFilter.AudioKernel.Parameters.Cutoff, newCutoff);
                Cutoff = newCutoff;
            }

            GUI.Label(new Rect(100, 160, 300, 30), "Lowpass Q:");
            var newq = GUI.HorizontalSlider(new Rect(100, 190, 300, 30), Q, 1.0f, 100.0f);
            if (Math.Abs(newq - Q) > 0.01f)
            {
                block.SetFloat<StateVariableFilter.AudioKernel.Parameters, StateVariableFilter.AudioKernel.Providers, StateVariableFilter.AudioKernel>(m_LowPass, StateVariableFilter.AudioKernel.Parameters.Q, newq);
                Q = newq;
            }

            GUI.Label(new Rect(100, 250, 300, 30), "Gain in dB:");
            var newGain = GUI.HorizontalSlider(new Rect(100, 280, 300, 30), Gain, -80.0f, 0.0f);
            if (Math.Abs(newGain - Gain) > 0.01f)
            {
                block.SetFloat<StateVariableFilter.AudioKernel.Parameters, StateVariableFilter.AudioKernel.Providers, StateVariableFilter.AudioKernel>(m_LowPass, StateVariableFilter.AudioKernel.Parameters.GainInDBs, newGain);
                Gain = newGain;
            }
        }
    }
}
