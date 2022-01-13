using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct NativeSampleBuffer
    {
        public uint Channels;
        [Obsolete("No longer used", false)]
        public SoundFormat Format;
        public float* Buffer;
        public bool Initialized;
    }

    /// <summary>
    /// Specifies the sound channel configuration.
    /// </summary>
    public enum SoundFormat
    {
        /// <summary>
        /// No format interpretation should be applied
        /// </summary>
        Raw,

        /// <summary>
        /// One channel
        /// </summary>
        Mono,

        /// <summary>
        /// Two channels
        /// </summary>
        Stereo,

        /// <summary>
        /// 4.0 surround
        /// </summary>
        Quad,

        /// <summary>
        /// 5.0 surround
        /// </summary>
        Surround,

        /// <summary>
        /// 5.1 surround (6 channels)
        /// </summary>
        FiveDot1,

        /// <summary>
        /// 7.1 surround (8 channels)
        /// </summary>
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
        /// Provides access to the sample buffer which provides access to the actual sample data of the buffer.
        /// </summary>
        /// <param name="channelIndex">The channel to get the sample buffer for</param>
        /// <returns></returns>
        public NativeArray<float> GetBuffer(int channelIndex)
        {
            Utility.ValidateIndex(channelIndex, Channels - 1);
            var length = Samples;
            var buffer = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(NativeBuffer->Buffer + (length * channelIndex), length, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref buffer, Safety);
#endif

            return buffer;
        }

//        /// <summary>
//        /// Provides access to the sample buffer which provides access to the actual sample data of the buffer.
//        /// </summary>
//        public NativeArray<float> Buffer
//        {
//            get
//            {
//                var length = Samples * Channels;
//                var buffer = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(NativeBuffer->Buffer, length, Allocator.Invalid);

//#if ENABLE_UNITY_COLLECTIONS_CHECKS
//                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref buffer, Safety);
//#endif

//                return buffer;
//            }
//        }

        internal NativeSampleBuffer* NativeBuffer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif
    }

    /// <summary>
    /// A simple structure representing an array of sample buffers
    /// </summary>
    public unsafe struct SampleBufferArray
    {
        /// <summary>
        /// The number of sample buffers in the array
        /// </summary>
        public int Count { get; internal set; }

        /// <summary>
        /// Get a sample buffer
        /// </summary>
        /// <param name="index">The index of the sample buffer to be retrieved</param>
        /// <returns>The requested sample buffer</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown when the index is out of range</exception>
        public SampleBuffer GetSampleBuffer(int index)
        {
            Utility.ValidateIndex(index, Count - 1);
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
