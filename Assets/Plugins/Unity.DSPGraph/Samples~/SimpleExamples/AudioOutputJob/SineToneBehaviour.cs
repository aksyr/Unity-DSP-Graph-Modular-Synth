using System;
using System.Collections;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class SineToneBehaviour: MonoBehaviour
{
    public float Frequency;
    public float Duration;

    AudioOutputHandle m_Handle;

    [BurstCompile]
    struct SineToneOutput : IAudioOutput
    {
        public float Frequency;
        float m_Phase;
        int m_ChannelCount;
        float m_Delta;

        public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
        {
            m_ChannelCount = channelCount;
            m_Delta = Frequency / sampleRate;
        }

        public void BeginMix(int frameCount) { }

        public void EndMix(NativeArray<float> output, int frames)
        {
            for (var f = 0; f < frames; f++)
            {
                for (var c = 0; c < m_ChannelCount; c++)
                    output[(f * m_ChannelCount) + c] = math.sin(m_Phase * 2 * math.PI);

                m_Phase += m_Delta;
                m_Phase -= math.floor(m_Phase);
            }
        }

        public void Dispose() { }
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 150, 100), "Play"))
        {
            var output = new SineToneOutput { Frequency = Frequency };

            if (m_Handle.Valid)
                m_Handle.Dispose();
            m_Handle = output.AttachToDefaultOutput();
            StartCoroutine(TimeoutSine(Duration));
        }
    }

    IEnumerator TimeoutSine(float time)
    {
        yield return new WaitForSeconds(time);

        if (m_Handle.Valid)
            m_Handle.Dispose();
    }
}
