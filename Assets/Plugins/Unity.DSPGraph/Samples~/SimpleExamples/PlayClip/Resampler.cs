using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Audio
{
    [BurstCompile(CompileSynchronously = true)]
    public struct Resampler
    {
        const float k_HalfPi = math.PI * 0.5f;
        const float k_HalfSquareRootOfTwo = math.SQRT2 * 0.5f;

        public double Position;

        public bool ResampleLerpRead<T>(
            SampleProvider provider,
            NativeArray<float> input,
            NativeArray<float> output,
            ParameterData<T> parameterData,
            T rateParam)
            where T : unmanaged, Enum
        {
            var finishedSampleProvider = false;

            for (var i = 0; i < output.Length / 2; i++)
            {
                var rate = parameterData.GetFloat(rateParam, i);
                Position += rate;

                var length = input.Length / 2 - 1;

                while (Position >= length)
                {
                    input[0] = input[input.Length - 2];
                    input[1] = input[input.Length - 1];

                    finishedSampleProvider |= ReadSamples(provider, new NativeSlice<float>(input, 2));

                    Position -= input.Length / 2 - 1;
                }

                var positionFloor = Math.Floor(Position);
                var positionFraction = Position - positionFloor;
                var previousSampleIndex = (int)positionFloor;
                var nextSampleIndex = previousSampleIndex + 1;

                var prevSampleL = input[previousSampleIndex * 2 + 0];
                var prevSampleR = input[previousSampleIndex * 2 + 1];
                var sampleL = input[nextSampleIndex * 2 + 0];
                var sampleR = input[nextSampleIndex * 2 + 1];

                output[i * 2 + 0] = (float)(prevSampleL + (sampleL - prevSampleL) * positionFraction);
                output[i * 2 + 1] = (float)(prevSampleR + (sampleR - prevSampleR) * positionFraction);
            }

            return finishedSampleProvider;
        }

        // read either mono or stereo, always convert to stereo interleaved
        static bool ReadSamples(SampleProvider provider, NativeSlice<float> destination)
        {
            if (!provider.Valid)
                return true;

            var finished = false;

            // Read from SampleProvider and convert to interleaved stereo if needed
            if (provider.ChannelCount == 2)
            {
                var read = provider.Read(destination.Slice(0, destination.Length));
                if (read < destination.Length / 2)
                {
                    for (var i = read * 2; i < destination.Length; i++)
                        destination[i] = 0;
                    return true;
                }
            }
            else
            {
                var n = destination.Length / 2;
                var buffer = destination.Slice(0, n);
                var read = provider.Read(buffer);

                if (read < n)
                {
                    for (var i = read; i < n; i++)
                        destination[i] = 0;

                    finished = true;
                }

                for (var i = n - 1; i >= 0; i--)
                {
                    destination[i * 2 + 0] = destination[i];
                    destination[i * 2 + 1] = destination[i];
                }
            }

            return finished;
        }
    }
}
