using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;
using UnityEngine.Experimental.Audio;

namespace Unity.Audio
{
    /// <summary>
    /// Annotate a sample provider enum value as being an array
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SampleProviderArrayAttribute : Attribute
    {
        internal readonly int Size;

        /// <summary>
        /// Annotate a sample provider enum value as being an array
        /// </summary>
        /// <param name="size">The size of the sample provider array (-1 for variable size)</param>
        public SampleProviderArrayAttribute(int size = -1)
        {
            Size = size;
        }
    }

    /// <summary>
    /// A helper for reading samples from a native Unity resource, such as an AudioClip or a VideoPlayer
    /// </summary>
    public unsafe struct SampleProvider : Unity.Media.Utilities.IValidatable
    {
        /// <summary>
        /// Enumeration of the formats that source data may be converted to.
        /// </summary>
        public enum NativeFormatType
        {
            /// <summary>
            /// Float big-endian
            /// </summary>
            FLOAT_BE,

            /// <summary>
            /// Float little-endian
            /// </summary>
            FLOAT_LE,

            /// <summary>
            /// 8-bit PCM
            /// </summary>
            PCM8,

            /// <summary>
            /// 16-bit PCM, big-endian
            /// </summary>
            PCM16_BE,

            /// <summary>
            /// 16-bit PCM, little-endian
            /// </summary>
            PCM16_LE,

            /// <summary>
            /// 24-bit PCM, big-endian
            /// </summary>
            PCM24_BE,

            /// <summary>
            /// 24-bit PCM, little-endian
            /// </summary>
            PCM24_LE,
        }

        /// <summary>
        /// Whether the provider is valid and readable
        /// </summary>
        public bool Valid
        {
            get
            {
                if (ProviderHandle == InvalidProviderHandle || !AudioSampleProvider.InternalIsValid(ProviderHandle))
                    return false;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(Safety);
#endif
                return true;
            }
        }

        /// <summary>
        /// Reads a part of the samples as bytes in the specified output format from the sample provider.
        /// </summary>
        /// <param name="destination">The destination buffer that decoded samples are written to.</param>
        /// <param name="format">The destination format that the samples are decoded to.</param>
        /// <returns>The number of samples that could be read. Will be less than the destination size when the end of the sound is reached.</returns>
        /// <remarks>Unlike the float overloads, here the buffer is written in interleaved order</remarks>
        public int Read(NativeSlice<byte> destination, NativeFormatType format)
        {
            this.Validate();
            // Not doing any format/size checks here. byte is the only valid choice for 8-bits,
            // 24-bits as well as big-endian float. Users may have reasons to want 16-bit samples
            // carried via a buffer-of-bytes, so we're being totally flexible here.
            return DSPSampleProviderInternal.Internal_ReadUInt8FromSampleProviderById(
                ProviderHandle, (int)format, destination.GetUnsafePtr(),
                destination.Length);
        }

        /// <summary>
        /// Reads a part of the samples as short integers in the specified output format from the sample provider.
        /// </summary>
        /// <param name="destination">The destination buffer that decoded samples are written to.</param>
        /// <param name="format">The destination format that the samples are decoded to.</param>
        /// <returns>The number of samples that could be read. Will be less than the destination size when the end of the sound is reached.</returns>
        /// <remarks>Unlike the float overloads, here the buffer is written in interleaved order</remarks>
        public int Read(NativeSlice<short> destination, NativeFormatType format)
        {
            this.Validate();

            return DSPSampleProviderInternal.Internal_ReadSInt16FromSampleProviderById(
                ProviderHandle, (int)format,
                destination.GetUnsafePtr(), destination.Length);
        }

        /// <summary>
        /// Reads a part of the samples as floats from the sample provider.
        /// </summary>
        /// <param name="destination">The destination buffer that decoded samples are written to.</param>
        /// <returns>The number of samples that could be read. Will be less than the destination size when the end of the sound is reached.</returns>
        public int Read(NativeSlice<float> destination)
        {
            this.Validate();
            var temp = new NativeArray<float>(destination.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var buffer = (float*)temp.GetUnsafePtr();
            int readLength = DSPSampleProviderInternal.Internal_ReadFloatFromSampleProviderById(
                ProviderHandle, buffer,
                destination.Length);
            Utility.DeinterleaveAudioStream(buffer, (float*)destination.GetUnsafePtr(), readLength, ChannelCount);
            temp.Dispose();
            return readLength;
        }

        /// <summary>
        /// Reads a part of the samples as floats from the sample provider.
        /// </summary>
        /// <param name="destination">A sample buffer that decoded samples are written to.</param>
        /// <param name="start">The frame index at which to start writing to the buffer.</param>
        /// <param name="length">The number of frames to write to the buffer, -1 to fill the buffer completely.</param>
        /// <param name="writeMode">The mode for writing samples to the buffer</param>
        /// <returns>The number of samples that could be read. Will be less than the requested size when the end of the sound is reached.</returns>
        /// <exception cref="IndexOutOfRangeException"></exception>
        public int Read(SampleBuffer destination, int start = 0, int length = -1, BufferWriteMode writeMode = BufferWriteMode.Overwrite)
        {
            this.Validate();
            Utility.ValidateIndex(start);

            if (length < 0)
                length = destination.Samples - start;
            if (length == 0)
                return 0;
            Utility.ValidateIndex(length, destination.Samples + start);

            var channelCount = destination.Channels;
            NativeArray<float> temp = new NativeArray<float>(length * channelCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int readLength = DSPSampleProviderInternal.Internal_ReadFloatFromSampleProviderById(ProviderHandle, temp.GetUnsafePtr(), temp.Length);
            int readFactor = (writeMode == BufferWriteMode.Overwrite) ? 0 : 1;

            // Sample provider reads interleaved data; we want per-channel streams
            for (int channel = 0; channel < channelCount; ++channel)
            {
                int interleavedIndex = channel;
                var buffer = destination.GetBuffer(channel);
                for (int frame = start; frame < start + readLength; ++frame, interleavedIndex += channelCount)
                    buffer[frame] = (buffer[frame] * readFactor) + temp[interleavedIndex];
            }

            temp.Dispose();
            return readLength;
        }

        /// <summary>
        /// Gets the format of the samples used internally by the system. This will likely be the format that yields best performance because conversion can be skipped.
        /// </summary>
        public NativeFormatType NativeFormat => NativeFormatType.FLOAT_LE;

        /// <summary>
        /// Gets the number of channels in the decoded sound.
        /// </summary>
        public short ChannelCount => (short)DSPSampleProviderInternal.Internal_GetChannelCountById(ProviderHandle);

        /// <summary>
        /// Get the sample rate of the decoded sound.
        /// </summary>
        public int SampleRate => (int)DSPSampleProviderInternal.Internal_GetSampleRateById(ProviderHandle);

        /// <summary>
        /// Deallocates the sample provider.
        /// </summary>
        public void Release()
        {
            this.Validate();
            AudioSampleProvider.InternalRemove(ProviderHandle);
        }

        internal uint ProviderHandle;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif

        internal const uint InvalidProviderHandle = 0;
    }

    /// <summary>
    /// A simple container for a <see cref="DSPNode"/>'s sample providers
    /// </summary>
    /// <typeparam name="TProviders">The type of the providers enum for the associated <see cref="IAudioKernel{TParameters,TProviders}"/></typeparam>
    public unsafe struct SampleProviderContainer<TProviders> where TProviders : unmanaged, Enum
    {
        /// <summary>
        /// Gets the total number of sample providers in a container.
        /// </summary>
        public int Count { get; internal set; }

        /// <summary>
        /// Gets the number of sample providers associated with a given enum value in a container.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public int GetCount(TProviders p)
        {
            var itemIndex = UnsafeUtility.EnumToInt(p);
            Utility.ValidateIndex(itemIndex, Count - 1);

            int globalIndex = SampleProviderIndices[itemIndex];

            // Happens if the 'itemIndex'th item is an empty array.
            if (globalIndex < 0)
                return 0;

            // Find the index of the next non-empty item.
            int nextItemIndex = itemIndex + 1;
            int nextGlobalIndex = -1;
            for (; nextItemIndex < Count; ++nextItemIndex)
            {
                nextGlobalIndex = SampleProviderIndices[nextItemIndex];
                if (nextGlobalIndex >= 0)
                    break;
            }

            // All items after itemIndex are empty containers.
            if (nextGlobalIndex < 0)
                return SampleProvidersCount - globalIndex;

            return nextGlobalIndex - globalIndex;
        }

        /// <summary>
        /// Gets the sample provider associated with a given enum value.
        /// </summary>
        /// <param name="p"></param>
        /// <param name="arrayIndex"></param>
        /// <returns>Returns a SampleProvider</returns>
        public SampleProvider GetSampleProvider(TProviders p, int arrayIndex = 0)
        {
            return GetSampleProvider(UnsafeUtility.EnumToInt(p), arrayIndex);
        }

        /// <summary>
        /// Gets the sample provider associated with a given index.
        /// </summary>
        /// <param name="itemIndex"></param>
        /// <param name="arrayIndex"></param>
        /// <returns></returns>
        public SampleProvider GetSampleProvider(int itemIndex = 0, int arrayIndex = 0)
        {
            Utility.ValidateIndex(itemIndex, Count - 1);
            int globalIndex = SampleProviderIndices[itemIndex];

            // Happens if the 'index'th item is an empty array.
            Utility.ValidateIndex(globalIndex);

            globalIndex += arrayIndex;

            // Find the index of the next non-empty item.
            int nextItemIndex = itemIndex + 1;
            int nextGlobalIndex = -1;
            for (; nextItemIndex < Count; ++nextItemIndex)
            {
                nextGlobalIndex = SampleProviderIndices[nextItemIndex];
                if (nextGlobalIndex != -1)
                    break;
            }

            if (nextGlobalIndex == -1)
            {
                // Happens if indexing beyond the end of the last item.
                Utility.ValidateIndex(globalIndex, SampleProvidersCount - 1);
            }
            else
            {
                // Happens if indexing beyond the end of the current item.
                Utility.ValidateIndex(globalIndex, nextGlobalIndex - 1);
            }

            return new SampleProvider
            {
                ProviderHandle = SampleProviders[globalIndex],
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Safety = Safety,
#endif
            };
        }

        internal int* SampleProviderIndices;

        internal int SampleProvidersCount;
        internal uint* SampleProviders;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif
    }
}
