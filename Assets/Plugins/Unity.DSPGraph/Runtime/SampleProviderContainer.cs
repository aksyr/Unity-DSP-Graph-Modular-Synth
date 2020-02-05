using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Audio;

namespace Unity.Audio
{
    [AttributeUsage(AttributeTargets.Field)]
    public class SampleProviderArrayAttribute : Attribute
    {
        internal readonly int Size;

        public SampleProviderArrayAttribute(int size = -1)
        {
            Size = size;
        }
    }

    public unsafe struct SampleProvider
    {
        /// <summary>
        /// Enumeration of the formats that source data may be converted to.
        /// </summary>
        public enum NativeFormatType
        {
            FLOAT_BE,
            FLOAT_LE,
            PCM8,
            PCM16_BE,
            PCM16_LE,
            PCM24_BE,
            PCM24_LE,
        }

        public bool Valid => ProviderHandle != InvalidProviderHandle && AudioSampleProvider.InternalIsValid(ProviderHandle);

        /// <summary>
        /// Reads a part of the samples as bytes in the specified output format from the sample provider.
        /// </summary>
        /// <param name="destination">The destination buffer that decoded samples are written to.</param>
        /// <param name="format">The destination format that the samples are decoded to.</param>
        /// <returns>The number of samples that could be read. Will be less than the destination size when the end of the sound is reached.</returns>
        public int Read(NativeSlice<byte> destination, NativeFormatType format)
        {
            CheckValidAndThrow();
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
        public int Read(NativeSlice<short> destination, NativeFormatType format)
        {
            CheckValidAndThrow();
            if (format != NativeFormatType.PCM16_LE && format != NativeFormatType.PCM16_BE)
                throw new ArgumentException("Using buffer of short to capture samples of a different size.");

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
            CheckValidAndThrow();
            return DSPSampleProviderInternal.Internal_ReadFloatFromSampleProviderById(
                ProviderHandle, destination.GetUnsafePtr(),
                destination.Length);
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
            CheckValidAndThrow();
            AudioSampleProvider.InternalRemove(ProviderHandle);
        }

        private void CheckValidAndThrow()
        {
            if (!Valid)
                throw new InvalidOperationException("Invalid SampleProvider being used.");
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(Safety);
#endif
        }

        internal uint ProviderHandle;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle Safety;
#endif

        internal const uint InvalidProviderHandle = 0;
    }

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
            if (itemIndex < 0 || itemIndex >= Count)
                throw new IndexOutOfRangeException("itemIndex");

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
            if (itemIndex < 0 || itemIndex >= Count)
                throw new ArgumentOutOfRangeException(nameof(itemIndex));

            int globalIndex = SampleProviderIndices[itemIndex];

            // Happens if the 'index'th item is an empty array.
            if (globalIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

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
                if (globalIndex >= SampleProvidersCount)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }
            else
            {
                // Happens if indexing beyond the end of the current item.
                if (globalIndex >= nextGlobalIndex)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
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
