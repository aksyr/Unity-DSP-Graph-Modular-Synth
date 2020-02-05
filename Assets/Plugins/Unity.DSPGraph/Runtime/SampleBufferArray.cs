using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct NativeSampleBuffer
    {
        public uint Channels;
        public SoundFormat Format;
        public float* Buffer;
        public bool Initialized;
    }

    /// <summary>
    /// Specifies the sound channel configuration.
    /// </summary>
    public enum SoundFormat
    {
        Raw,
        Mono,
        Stereo,
        Quad,
        Surround,
        FiveDot1,
        SevenDot1
    }

    /// <summary>
    /// Represents a block of sample frames in a specific data format.
    /// </summary>
    public unsafe struct SampleBuffer
    {
        /// <summary>
        /// Gets the number of sample frames stored in the buffer.
        /// </summary>
        public int Samples { get; internal set; }

        /// <summary>
        /// Gets the channels for the sound held in the buffer.
        /// </summary>
        public int Channels => (int)NativeBuffer->Channels;

        /// <summary>
        /// Gets the format that each sample is stored in.
        /// </summary>
        public SoundFormat Format => NativeBuffer->Format;

        /// <summary>
        /// Provides access to the sample buffer which provides access to the actual sample data of the buffer.
        /// </summary>
        public NativeArray<float> Buffer
        {
            get
            {
                var length = Samples * Channels;
                var buffer = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(NativeBuffer->Buffer, length, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref buffer, Safety);
#endif

                return buffer;
            }
        }

        internal NativeSampleBuffer* NativeBuffer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif
    }

    public unsafe struct SampleBufferArray
    {
        public int Count { get; internal set; }

        public SampleBuffer GetSampleBuffer(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new SampleBuffer
            {
                NativeBuffer = &Buffers[index],
                Samples = SampleCount,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Safety = Safety
#endif
            };
        }

        internal NativeSampleBuffer* Buffers;
        internal int SampleCount;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif
    }
}
