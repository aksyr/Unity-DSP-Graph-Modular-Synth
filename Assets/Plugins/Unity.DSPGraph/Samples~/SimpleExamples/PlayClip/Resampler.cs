using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio.DSPGraphSamples
{
    [BurstCompile(CompileSynchronously = true)]
    public struct Resampler
    {
        public double Position;
        private float m_LastLeft;
        private float m_LastRight;

        public bool ResampleLerpRead<T>(
            SampleProvider provider,
            NativeArray<float> input,
            SampleBuffer outputBuffer,
            ParameterData<T> parameterData,
            T rateParam)
            where T : unmanaged, Enum
        {
            var finishedSampleProvider = false;

            var outputL = outputBuffer.GetBuffer(0);
            var outputR = outputBuffer.GetBuffer(1);
            for (var i = 0; i < outputL.Length; i++)
            {
                Position += parameterData.GetFloat(rateParam, i);

                var length = input.Length / 2;

                while (Position >= length - 1)
                {
                    m_LastLeft = input[length - 1];
                    m_LastRight = input[input.Length - 1];

                    finishedSampleProvider |= ReadSamples(provider, new NativeSlice<float>(input, 0));

                    Position -= length;
                }

                var positionFloor = Math.Floor(Position);
                var positionFraction = Position - positionFloor;
                var previousSampleIndex = (int)positionFloor;
                var nextSampleIndex = previousSampleIndex + 1;

                var prevSampleL = (previousSampleIndex < 0) ? m_LastLeft : input[previousSampleIndex];
                var prevSampleR = (previousSampleIndex < 0) ? m_LastRight : input[previousSampleIndex + length];
                var sampleL = input[nextSampleIndex];
                var sampleR = input[nextSampleIndex + length];

                outputL[i] = (float)(prevSampleL + (sampleL - prevSampleL) * positionFraction);
                outputR[i] = (float)(prevSampleR + (sampleR - prevSampleR) * positionFraction);
            }

            return finishedSampleProvider;
        }

        // read either mono or stereo, always convert to stereo interleaved
        static unsafe bool ReadSamples(SampleProvider provider, NativeSlice<float> destination)
        {
            if (!provider.Valid)
                return true;

            var finished = false;

            // Read from SampleProvider and convert to stereo if needed
            var destinationFrames = destination.Length / 2;
            if (provider.ChannelCount == 2)
            {
                var read = provider.Read(destination.Slice(0, destination.Length));
                if (read < destinationFrames)
                {
                    for (var i = read; i < destinationFrames; i++)
                    {
                        destination[i] = 0;
                        destination[i + destinationFrames] = 0;
                    }

                    return true;
                }
            }
            else
            {
                var buffer = destination.Slice(0, destinationFrames);
                var read = provider.Read(buffer);

                if (read < destinationFrames)
                {
                    for (var i = read; i < destinationFrames; i++)
                        destination[i] = 0;

                    finished = true;
                }

                var left = (float*)destination.GetUnsafePtr();
                var right = left + read;
                UnsafeUtility.MemCpy(right, left, read * UnsafeUtility.SizeOf<float>());
            }

            return finished;
        }
    }
}
