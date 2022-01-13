using NUnit.Framework;
using Unity.Media.Utilities;

namespace Unity.Media.Utilities.Tests.Runtime
{
    public class Utilities
    {
        [Test]
        public unsafe void InterleaveAudioStream_InterleavesAudioStream(
            [Values(1, 2, 3, 4, 5, 6, 7, 8)] int channelCount, [Values] BufferWriteMode writeMode)
        {
            int frameCount = 100;
            float initialValue = 1.0f;
            float expectedAddedValue = (writeMode == BufferWriteMode.Additive) ? 1.0f : 0.0f;
            int bufferLength = frameCount * channelCount;
            var source = GetPerChannelBuffer(channelCount, frameCount);
            var destination = new float[bufferLength];
            fixed(float* sourceBuffer = source)
            {
                fixed(float* destinationBuffer = destination)
                {
                    for (int i = 0; i < bufferLength; ++i)
                        destinationBuffer[i] = initialValue;
                    Utility.InterleaveAudioStream(sourceBuffer, destinationBuffer, frameCount, channelCount, writeMode);
                }
            }

            var expected = GetInterleavedBuffer(channelCount, frameCount, expectedAddedValue);
            for (int i = 0; i < bufferLength; ++i)
                Assert.AreEqual(expected[i], destination[i], .0001f);
        }

        [Test]
        public unsafe void DeinterleaveAudioStream_DeinterleavesAudioStream(
            [Values(1, 2, 3, 4, 5, 6, 7, 8)] int channelCount, [Values] BufferWriteMode writeMode)
        {
            int frameCount = 100;
            float initialValue = 1.0f;
            float expectedAddedValue = (writeMode == BufferWriteMode.Additive) ? 1.0f : 0.0f;
            int bufferLength = frameCount * channelCount;
            var source = GetInterleavedBuffer(channelCount, frameCount);
            var destination = new float[bufferLength];
            fixed(float* sourceBuffer = source)
            {
                fixed(float* destinationBuffer = destination)
                {
                    for (int i = 0; i < bufferLength; ++i)
                        destinationBuffer[i] = initialValue;
                    Utility.DeinterleaveAudioStream(sourceBuffer, destinationBuffer, frameCount, channelCount,
                        writeMode);
                }
            }

            var expected = GetPerChannelBuffer(channelCount, frameCount, expectedAddedValue);
            for (int i = 0; i < bufferLength; ++i)
                Assert.AreEqual(expected[i], destination[i], .0001f);
        }

        [Test]
        public unsafe void Interleave_Roundtrip_FromPerChannel([Values(1, 2, 3, 4, 5, 6, 7, 8)] int channelCount,
            [Values] BufferWriteMode writeMode)
        {
            int frameCount = 100;
            float initialValue = 1.0f;
            float expectedAddedValue = (writeMode == BufferWriteMode.Additive) ? 1.0f : 0.0f;
            int bufferLength = frameCount * channelCount;
            var source = GetPerChannelBuffer(channelCount, frameCount);
            var destination = new float[bufferLength];
            fixed(float* sourceBuffer = source)
            {
                fixed(float* destinationBuffer = destination)
                {
                    var temp = Utility.AllocateUnsafe<float>(bufferLength);
                    for (int i = 0; i < bufferLength; ++i)
                        destinationBuffer[i] = initialValue;
                    Utility.InterleaveAudioStream(sourceBuffer, temp, frameCount, channelCount);
                    Utility.DeinterleaveAudioStream(temp, destinationBuffer, frameCount, channelCount, writeMode);
                    Utility.FreeUnsafe(temp);
                }
            }

            for (int i = 0; i < bufferLength; ++i)
                Assert.AreEqual(source[i] + expectedAddedValue, destination[i], .0001f);
        }

        [Test]
        public unsafe void Interleave_Roundtrip_FromInterleaved([Values(1, 2, 3, 4, 5, 6, 7, 8)] int channelCount,
            [Values] BufferWriteMode writeMode)
        {
            int frameCount = 100;
            float initialValue = 1.0f;
            float expectedAddedValue = (writeMode == BufferWriteMode.Additive) ? 1.0f : 0.0f;
            int bufferLength = frameCount * channelCount;
            var source = GetInterleavedBuffer(channelCount, frameCount);
            var destination = new float[bufferLength];
            fixed(float* sourceBuffer = source)
            {
                fixed(float* destinationBuffer = destination)
                {
                    var temp = Utility.AllocateUnsafe<float>(bufferLength);
                    for (int i = 0; i < bufferLength; ++i)
                        destinationBuffer[i] = initialValue;
                    Utility.DeinterleaveAudioStream(sourceBuffer, temp, frameCount, channelCount);
                    Utility.InterleaveAudioStream(temp, destinationBuffer, frameCount, channelCount, writeMode);
                    Utility.FreeUnsafe(temp);
                }
            }

            for (int i = 0; i < bufferLength; ++i)
                Assert.AreEqual(source[i] + expectedAddedValue, destination[i], .0001f);
        }

        [Test]
        public unsafe void AllocateZero_ReturnsNull()
        {
            Assert.That(Utility.AllocateUnsafe<int>(0) == null);
        }

        [Test]
        public unsafe void FreeNull_DoesNotError()
        {
            Utility.FreeUnsafe((int*)null);
        }

        /// <summary>
        /// Get a new managed per-channel buffer with channelIndex + addedValue in each element
        /// </summary>
        static float[] GetPerChannelBuffer(int channelCount, int frameCount, float addedValue = 0.0f)
        {
            var buffer = new float[frameCount * channelCount];
            var index = 0;
            for (int channel = 0; channel < channelCount; ++channel)
                for (int frame = 0; frame < frameCount; ++frame, ++index)
                    buffer[index] = channel + addedValue;
            return buffer;
        }

        /// <summary>
        /// Get a new managed interleaved buffer with channelIndex + addedValue in each element
        /// </summary>
        static float[] GetInterleavedBuffer(int channelCount, int frameCount, float addedValue = 0.0f)
        {
            var buffer = new float[frameCount * channelCount];
            var index = 0;
            for (int frame = 0; frame < frameCount; ++frame)
                for (int channel = 0; channel < channelCount; ++channel, ++index)
                    buffer[index] = channel + addedValue;
            return buffer;
        }
    }
}
