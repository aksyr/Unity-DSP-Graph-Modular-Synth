using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Audio.DSPGraphSamples
{
    public class SineToneBehaviour : MonoBehaviour
    {
        public float Frequency;
        public float Duration;

        AudioOutputHandle m_Handle;

        // It's important to mark audio output jobs for synchronous burst compilation when running in the Unity editor.
        // Otherwise, they can be executed via mono until burst compilation finishes,
        // which will make the audio mixer thread subject to pausing for garbage collection for the rest of the Unity session.
        [BurstCompile(CompileSynchronously = true)]
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

            public void BeginMix(int frameCount) {}

            public void EndMix(NativeArray<float> output, int frames)
            {
#if UNITY_2020_2_OR_NEWER
                // Interleaving happens in the output hook manager
                EndMixDeinterleaved(output, frames);
#else
                EndMixInterleaved(output, frames);
#endif
            }

            public void EndMixInterleaved(NativeArray<float> output, int frames)
            {
                for (var f = 0; f < frames; f++)
                {
                    for (var c = 0; c < m_ChannelCount; c++)
                        output[(f * m_ChannelCount) + c] = math.sin(m_Phase * 2 * math.PI);

                    m_Phase += m_Delta;
                    m_Phase -= math.floor(m_Phase);
                }
            }

            public void EndMixDeinterleaved(NativeArray<float> output, int frames)
            {
                for (var f = 0; f < frames; f++)
                {
                    for (var c = 0; c < m_ChannelCount; c++)
                        output[(c * frames) + f] = math.sin(m_Phase * 2 * math.PI);

                    m_Phase += m_Delta;
                    m_Phase -= math.floor(m_Phase);
                }
            }

            public void Dispose() {}
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
}
