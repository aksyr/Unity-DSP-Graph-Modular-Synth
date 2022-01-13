using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Debug = UnityEngine.Debug;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// Various utilities for unity media collections
    /// </summary>
    public static class Utility
    {
        private static ConcurrentDictionary<Allocator, ConcurrentDictionary<long, long>> s_AllocationTracker;
        private static SpinWait s_Waiter = new SpinWait();

        /// <summary>
        /// Enable tracking of leaks via AllocateUnsafe/FreeUnsafe
        /// </summary>
        /// <remarks>
        /// This also requires UNITY_MEDIA_MONITOR_NATIVE_ALLOCATIONS to be defined.
        /// Burst is currently not supported.
        /// </remarks>
        public static bool EnableLeakTracking { get; set; }

        /// <summary>
        /// Allocate native memory
        /// </summary>
        /// <param name="count">The number of elements to allocate</param>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="permanentAllocation">Whether this allocation will remain alive for the lifetime of the program. This is used for leak tracking.</param>
        /// <typeparam name="T">The element type to allocate</typeparam>
        /// <returns>A pointer to a newly-allocated buffer of count elements</returns>
        public static unsafe T* AllocateUnsafe<T>(int count = 1, Allocator allocator = Allocator.Persistent, bool permanentAllocation = false)
            where T : unmanaged
        {
            if (count == 0)
                return null;
            var memory = (T*)UnsafeUtility.Malloc(count * UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator);
            if (!permanentAllocation)
                RegisterAllocation(memory, allocator);
            return memory;
        }

        /// <summary>
        /// Free native memory
        /// </summary>
        /// <param name="memory">A pointer to the native memory to be freed</param>
        /// <param name="allocator">The native allocator with which the memory was allocated</param>
        /// <typeparam name="T">The element type of memory</typeparam>
        public static unsafe void FreeUnsafe<T>(T* memory, Allocator allocator = Allocator.Persistent)
            where T : unmanaged
        {
            if (memory == null)
                return;
            RegisterDeallocation(memory, allocator);
            UnsafeUtility.Free(memory, allocator);
        }

        /// <summary>
        /// Free native memory
        /// </summary>
        /// <param name="memory">A pointer to the native memory to be freed</param>
        /// <param name="allocator">The native allocator with which the memory was allocated</param>
        public static unsafe void FreeUnsafe(void* memory, Allocator allocator = Allocator.Persistent)
        {
            RegisterDeallocation(memory, allocator);
            UnsafeUtility.Free(memory, allocator);
        }

        /// <summary>
        /// Make a copy of a structure in native memory
        /// </summary>
        /// <param name="structure">The structure to copy</param>
        /// <typeparam name="T">The type of structure</typeparam>
        /// <returns>A pointer to newly-allocated memory containing a copy of structure</returns>
        /// <remarks>
        /// This uses the Persistent native allocator
        /// </remarks>
        public static unsafe void* CopyToPersistentAllocation<T>(ref T structure)
            where T : struct
        {
            void* copy = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
            RegisterAllocation(copy, Allocator.Persistent);
            UnsafeUtility.CopyStructureToPtr(ref structure, copy);
            return copy;
        }

        /// <summary>
        /// Clears a portion of a buffer using a provided clear value
        /// </summary>
        /// <param name="buffer">The buffer to be cleared</param>
        /// <param name="clearValue">The value to be set in each byte</param>
        /// <param name="byteCount">The number of bytes to clear</param>
        /// <remarks>
        /// Equivalent to libc's memset()
        /// </remarks>
        public static unsafe void ClearBuffer(void* buffer, byte clearValue, long byteCount)
        {
#if UNITY_2019_3_OR_NEWER
            UnsafeUtility.MemSet(buffer, clearValue, byteCount);
#else
            var byteBuffer = (byte*)buffer;
            for (var i = 0; i < byteCount; ++i)
                byteBuffer[i] = clearValue;
#endif
        }

        /// <summary>
        /// Read a per-channel audio stream (LLLL...RRRR...) and write an interleaved audio stream (LRLRLRLR...)
        /// </summary>
        /// <param name="source">A pointer to a per-channel buffer</param>
        /// <param name="destination">A pointer to the destination buffer</param>
        /// <param name="frames">How many frames are to be converted</param>
        /// <param name="channels">How many channels are in the source</param>
        /// <param name="writeMode">The write mode to be used</param>
        /// <remarks>The source buffer is assumed to be one float per channel per frame</remarks>
        public static unsafe void InterleaveAudioStream(float* source, float* destination, int frames, int channels, BufferWriteMode writeMode = BufferWriteMode.Overwrite)
        {
            if (writeMode == BufferWriteMode.Additive)
            {
                InterleaveAudioStreamAdditive(source, destination, frames, channels);
                return;
            }

            for (int channel = 0; channel < channels; ++channel)
            {
                int interleavedIndex = channel;
                int baseFrame = channel * frames;
                for (int frame = 0; frame < frames; ++frame, interleavedIndex += channels)
                    destination[interleavedIndex] = source[baseFrame + frame];
            }
        }

        static unsafe void InterleaveAudioStreamAdditive(float* source, float* destination, int frames, int channels)
        {
            for (int channel = 0; channel < channels; ++channel)
            {
                int interleavedIndex = channel;
                int baseFrame = channel * frames;
                for (int frame = 0; frame < frames; ++frame, interleavedIndex += channels)
                    destination[interleavedIndex] = destination[interleavedIndex] + source[baseFrame + frame];
            }
        }

        /// <summary>
        /// Read an interleaved audio stream (LRLRLRLR...) and write an per-channel audio stream (LLLL...RRRR...)
        /// </summary>
        /// <param name="source">A pointer to an interleaved buffer</param>
        /// <param name="destination">A pointer to the destination buffer</param>
        /// <param name="frames">How many frames are to be converted</param>
        /// <param name="channels">How many channels are in the source</param>
        /// <param name="writeMode">The write mode to be used</param>
        /// <remarks>The source buffer is assumed to be one float per channel per frame</remarks>
        public static unsafe void DeinterleaveAudioStream(float* source, float* destination, int frames, int channels, BufferWriteMode writeMode = BufferWriteMode.Overwrite)
        {
            if (writeMode == BufferWriteMode.Additive)
            {
                DeinterleaveAudioStreamAdditive(source, destination, frames, channels);
                return;
            }

            for (int channel = 0; channel < channels; ++channel)
            {
                int interleavedIndex = channel;
                int baseFrame = channel * frames;
                for (int frame = 0; frame < frames; ++frame, interleavedIndex += channels)
                    destination[baseFrame + frame] = source[interleavedIndex];
            }
        }

        private static unsafe void DeinterleaveAudioStreamAdditive(float* source, float* destination, int frames, int channels)
        {
            for (int channel = 0; channel < channels; ++channel)
            {
                int interleavedIndex = channel;
                int baseFrame = channel * frames;
                for (int frame = 0; frame < frames; ++frame, interleavedIndex += channels)
                    destination[baseFrame + frame] = destination[baseFrame + frame] + source[interleavedIndex];
            }
        }

        [BurstDiscard]
        [Conditional("UNITY_MEDIA_MONITOR_NATIVE_ALLOCATIONS")]
        internal static unsafe void RegisterAllocation(void* memory, Allocator allocator)
        {
            if (!EnableLeakTracking)
                return;
            if (s_AllocationTracker == null)
                s_AllocationTracker = new ConcurrentDictionary<Allocator, ConcurrentDictionary<long, long>>();
            if (!s_AllocationTracker.TryGetValue(allocator, out ConcurrentDictionary<long, long> allocations))
            {
                s_AllocationTracker.TryAdd(allocator, new ConcurrentDictionary<long, long>());
                s_AllocationTracker.TryGetValue(allocator, out allocations);
            }

            if (!allocations.TryAdd((long)memory, 0))
                throw new InvalidOperationException($"{(long)memory:x} has been double-registered");
#if UNITY_MEDIA_LOG_NATIVE_ALLOCATIONS
            Debug.Log($"DSPGraph: Allocated {(long)memory:x} with allocator {allocator}");
#endif
        }

        [BurstDiscard]
        [Conditional("UNITY_MEDIA_MONITOR_NATIVE_ALLOCATIONS")]
        internal static unsafe void RegisterDeallocation(void* memory, Allocator allocator)
        {
            if (!EnableLeakTracking || memory == null)
                return;
            if (s_AllocationTracker == null)
                throw new InvalidOperationException("Allocation tracker has not been initialized");
            if (!s_AllocationTracker.TryGetValue(allocator, out ConcurrentDictionary<long, long> _))
                throw new InvalidOperationException($"Allocation tracker has no entries for allocator {allocator}");
            long unused;
            if (!s_AllocationTracker[allocator].TryRemove((long)memory, out unused))
                throw new InvalidOperationException($"Deallocating unknown memory {(long) memory:x} from allocator {allocator}");
        }

        /// <summary>
        /// Perform leak verification, if leak tracking was enabled
        /// </summary>
        [BurstDiscard]
        [Conditional("UNITY_MEDIA_MONITOR_NATIVE_ALLOCATIONS")]
        public static void VerifyLeaks()
        {
            if (s_AllocationTracker == null || !EnableLeakTracking)
                return;
            foreach (KeyValuePair<Allocator, ConcurrentDictionary<long, long>> pair in s_AllocationTracker)
            {
                foreach (long memory in pair.Value.Keys)
                    Debug.LogWarning($"Unity.Media.Utilities: Leaked {memory:x} from allocator {pair.Key}");
                pair.Value.Clear();
            }
        }

        [BurstDiscard]
        internal static void YieldProcessor()
        {
            s_Waiter.SpinOnce();
        }

        internal static long InterlockedReadLong(ref long location)
        {
            // Burst doesn't support Interlocked.Read
            return Interlocked.Add(ref location, 0);
        }

        internal static int InterlockedReadInt(ref int location)
        {
            // Burst doesn't support Interlocked.Read
            return Interlocked.Add(ref location, 0);
        }

        /// <summary>
        /// Validate an index within a range
        /// </summary>
        /// <param name="index">The index to be validated</param>
        /// <param name="highestIndex">The highest valid index</param>
        /// <param name="lowestIndex">The lowest valid index</param>
        /// <exception cref="IndexOutOfRangeException">
        /// Thrown if <paramref name="index"/> is greater than <paramref name="highestIndex"/> or less than <paramref name="lowestIndex"/>
        /// </exception>
        public static void ValidateIndex(int index, int highestIndex = int.MaxValue, int lowestIndex = 0)
        {
            ValidateIndexMono(index, highestIndex, lowestIndex);
            ValidateIndexBurst(index, highestIndex, lowestIndex);
        }

        [BurstDiscard]
        private static void ValidateIndexMono(int index, int highestIndex, int lowestIndex)
        {
            if (index < lowestIndex || index > highestIndex)
                throw new IndexOutOfRangeException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateIndexBurst(int index, int highestIndex, int lowestIndex)
        {
            if (index < lowestIndex || index > highestIndex)
                throw new IndexOutOfRangeException();
        }
    }
}
