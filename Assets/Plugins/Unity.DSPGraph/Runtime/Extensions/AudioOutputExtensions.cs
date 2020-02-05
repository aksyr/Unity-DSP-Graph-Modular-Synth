using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    public static class AudioOutputExtensions
    {
        [StructLayout(LayoutKind.Sequential)]
        struct InitializationData
        {
            public readonly int ChannelCount;
            public readonly SoundFormat SoundFormat;
            public readonly long DSPBufferSize;
            public readonly int SampleRate;
        }

        internal struct AudioOutputHookStructProduce<TOutput> where TOutput : struct, IAudioOutput
        {
            static IntPtr s_JobReflectionData;

            public static IntPtr Initialize()
            {
                if (s_JobReflectionData == IntPtr.Zero)
                    s_JobReflectionData = JobsUtility.CreateJobReflectionData(typeof(TOutput), JobType.Single, (ExecuteJobFunction)Execute);
                return s_JobReflectionData;
            }

            delegate void ExecuteJobFunction(ref TOutput data, IntPtr functionID, IntPtr unused, ref JobRanges unused2, int unused3);

            // These are hardcoded indices from the Unity runtime
            enum MethodID
            {
                BeginMix = 0,
                EndMix = 1,
                Dispose = 3,
                Initialize = 4,
            }

            [StructLayout(LayoutKind.Sequential)]
            unsafe struct EndMixData
            {
                public readonly float* Buffer;
                public readonly int ChampleCount;
                public readonly int ChannelCount;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                public readonly AtomicSafetyHandle Safety;
#endif
            }

            public static unsafe void Execute(ref TOutput data, IntPtr userData, IntPtr method, ref JobRanges ranges, int jobIndex)
            {
                var methodID = (MethodID)method.ToInt32();
                switch (methodID)
                {
                    case MethodID.BeginMix:
                    {
                        var champleCount = userData.ToInt32();
                        data.BeginMix(champleCount);
                        break;
                    }
                    case MethodID.EndMix:
                    {
                        UnsafeUtility.CopyPtrToStructure(userData.ToPointer(), out EndMixData endMixData);

                        var length = endMixData.ChannelCount * endMixData.ChampleCount;
                        var nativeBuffer = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(endMixData.Buffer, length, Allocator.Invalid);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeBuffer, endMixData.Safety);
#endif
                        var frameCount = endMixData.ChampleCount / endMixData.ChannelCount;

                        data.EndMix(nativeBuffer, frameCount);
                        break;
                    }
                    case MethodID.Dispose:
                    {
                        data.Dispose();
                        break;
                    }
                    case MethodID.Initialize:
                    {
                        var initializationData = (InitializationData*)userData;
                        data.Initialize(initializationData->ChannelCount, initializationData->SoundFormat, initializationData->SampleRate, initializationData->DSPBufferSize);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// Attach an output job to Unity's default audio output
        /// </summary>
        /// <param name="outputJob">An output job</param>
        /// <typeparam name="T">A type implementing <typeparamref name="IAudioOutput"/></typeparam>
        /// <returns>An <typeparamref name="AudioOutputHandle"/> representing the created audio job</returns>
        public static unsafe AudioOutputHandle AttachToDefaultOutput<T>(this T outputJob) where T : struct, IAudioOutput
        {
            var output = new AudioOutputHandle();
            var jobReflectionData = (void*)AudioOutputHookStructProduce<T>.Initialize();
            var structMemory = AudioMemoryManager.Internal_AllocateAudioMemory(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>());
            UnsafeUtility.CopyStructureToPtr(ref outputJob, structMemory);

            AudioOutputHookManager.Internal_CreateAudioOutputHook(out output.Handle, jobReflectionData, structMemory);

            return output;
        }

        public static void DisposeOutputHook(ref AudioOutputHandle handle)
        {
            AudioOutputHookManager.Internal_DisposeAudioOutputHook(ref handle.Handle);
        }
    }
}
