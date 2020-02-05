using System;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Audio
{
    internal static class AudioKernelUpdateExtensions
    {
        public unsafe struct AudioKernelUpdateJobStructProduce<TUpdate, TParameters, TProviders, TKernel>
            where TParameters : unmanaged, Enum
            where TProviders  : unmanaged, Enum
            where TKernel     : struct, IAudioKernel<TParameters, TProviders>
            where TUpdate     : struct, IAudioKernelUpdate<TParameters, TProviders, TKernel>
        {
            // These structures are allocated but never freed at program termination
            static void* s_JobReflectionData;

            public static void Initialize(out void* jobReflectionData)
            {
                if (s_JobReflectionData == null)
                    s_JobReflectionData = (void*)JobsUtility.CreateJobReflectionData(typeof(TUpdate), JobType.Single, (ExecuteJobFunction)Execute);

                jobReflectionData = s_JobReflectionData;
            }

            delegate void ExecuteJobFunction(ref TUpdate updateJobData, ref TKernel jobData, IntPtr unused1, IntPtr unused2, ref JobRanges ranges, int ignored2);

            public static void Execute(ref TUpdate updateJobData, ref TKernel jobData, IntPtr dspNodePtr, IntPtr unused2, ref JobRanges ranges, int ignored2)
            {
                updateJobData.Update(ref jobData);
            }
        }

        public static unsafe void GetReflectionData<TUpdater, TParameters, TProviders, TKernel>(out void* jobReflectionData)
            where TParameters : unmanaged, Enum
            where TProviders  : unmanaged, Enum
            where TKernel     : struct, IAudioKernel<TParameters, TProviders>
            where TUpdater    : struct, IAudioKernelUpdate<TParameters, TProviders, TKernel>
        {
            AudioKernelUpdateJobStructProduce<TUpdater, TParameters, TProviders, TKernel>.Initialize(out jobReflectionData);
        }
    }
}
