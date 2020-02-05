using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Media.Utilities;
using UnityEngine;

namespace Unity.Audio
{
    internal static class AudioKernelExtensions
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DSPParameterDescription
        {
            public float Min;
            public float Max;
            public float DefaultValue;
        }

        public unsafe struct ParameterDescriptionData
        {
            [NativeDisableUnsafePtrRestriction]
            public DSPParameterDescription* Descriptions; //This can be not void
            public int ParameterCount;

            public bool Validate(uint parameterIndex)
            {
                if (parameterIndex < ParameterCount)
                    return true;

                LogUnknownParameterIndex(parameterIndex);
                return false;
            }

            [BurstDiscard]
            static void LogUnknownParameterIndex(uint parameterIndex)
            {
                Debug.LogError($"Unknown parameter {parameterIndex}");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DSPSampleProviderDescription
        {
            internal bool m_IsArray;
            internal int  m_Size;
        }

        public unsafe struct SampleProviderDescriptionData
        {
            [NativeDisableUnsafePtrRestriction]
            public DSPSampleProviderDescription* Descriptions;
            public int SampleProviderCount;

            public bool Validate(int sampleProviderIndex)
            {
                if (sampleProviderIndex >= 0 && sampleProviderIndex < SampleProviderCount)
                    return true;

                LogUnknownSampleProviderIndex(sampleProviderIndex);
                return false;
            }

            [BurstDiscard]
            static void LogUnknownSampleProviderIndex(int sampleProviderIndex)
            {
                Debug.LogError($"Unknown sample provider {sampleProviderIndex}");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct NativeAudioData
        {
            public ulong DSPClock;
            public uint DSPBufferSize;
            public uint SampleRate;

            public uint SampleReadCount;
            public uint InputBufferCount;
            [NativeDisableUnsafePtrRestriction]
            public NativeSampleBuffer* InputBuffers;

            public uint OutputBufferCount;
            [NativeDisableUnsafePtrRestriction]
            public NativeSampleBuffer* OutputBuffers;

            public uint ParameterCount;
            [NativeDisableUnsafePtrRestriction]
            public NativeDSPParameter* Parameters;
            [NativeDisableUnsafePtrRestriction]
            public DSPParameterKey* ParameterKeys;

            public uint SampleProviderIndexCount;
            [NativeDisableUnsafePtrRestriction]
            public int* SampleProviderIndices;

            public uint SampleProviderCount;
            [NativeDisableUnsafePtrRestriction]
            public uint* SampleProviders;

            [NativeDisableUnsafePtrRestriction]
            public void* GraphRegistry;

            public int GraphIndex;
            public int NodeIndex;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public AtomicSafetyHandle ReadOnlySafetyHandle;
            public AtomicSafetyHandle ReadWriteSafetyHandle;
#endif
        }

        public unsafe struct AudioKernelJobStructProduce<TAudioKernel, TParameters, TProviders>
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
        {
            // These structures are allocated but never freed at program termination
            static void* s_JobReflectionData;
            static ParameterDescriptionData s_ParameterDescriptionData;
            static SampleProviderDescriptionData s_SampleProviderDescriptionData;

            static ParameterDescriptionData CreateParameterDescription()
            {
                var pType = typeof(TParameters);
                if (!pType.IsEnum)
                    throw new ArgumentException("CreateDSPNode<Parameters, Providers, T> must have Parameters as an enum.");

                var pValues = Enum.GetValues(pType);
                for (var i = 0; i < pValues.Length; i++)
                {
                    if ((int)pValues.GetValue(i) != i)
                        throw new ArgumentException("CreateDSPNode<Parameters, Providers, T> enum values must start at 0 and be consecutive");
                }

                var paramsDescriptions = Utility.AllocateUnsafe<DSPParameterDescription>(pValues.Length, Allocator.Persistent, true);
                for (var i = 0; i < pValues.Length; i++)
                {
                    paramsDescriptions[i].Min = float.MinValue;
                    paramsDescriptions[i].Max = float.MaxValue;
                    paramsDescriptions[i].DefaultValue = 0.0f;
                    var field = pType.GetField(Enum.GetName(pType, pValues.GetValue(i)));

                    var rangeAttr = (ParameterRangeAttribute)Attribute.GetCustomAttribute(field, typeof(ParameterRangeAttribute));
                    if (rangeAttr != null)
                    {
                        paramsDescriptions[i].Min = rangeAttr.Min;
                        paramsDescriptions[i].Max = rangeAttr.Max;
                    }

                    var defValAttr = (ParameterDefaultAttribute)Attribute.GetCustomAttribute(field, typeof(ParameterDefaultAttribute));
                    if (defValAttr != null)
                    {
                        paramsDescriptions[i].DefaultValue = defValAttr.DefaultValue;
                    }
                }

                var data = new ParameterDescriptionData
                {
                    Descriptions = paramsDescriptions,
                    ParameterCount = pValues.Length
                };

                return data;
            }

            static SampleProviderDescriptionData CreateSampleProviderDescription()
            {
                var pType = typeof(TProviders);
                if (!pType.IsEnum)
                    throw new ArgumentException("CreateDSPNode<Parameters, Providers, T> must have Providers as an enum.");

                var pValues = Enum.GetValues(pType);
                for (var i = 0; i < pValues.Length; i++)
                {
                    if ((int)pValues.GetValue(i) != i)
                        throw new ArgumentException("CreateDSPNode<Parameters, Providers, T> enum values must start at 0 and be consecutive");
                }

                var provsDescriptions = Utility.AllocateUnsafe<DSPSampleProviderDescription>(pValues.Length, Allocator.Persistent, true);
                for (var i = 0; i < pValues.Length; i++)
                {
                    provsDescriptions[i].m_IsArray = false;
                    provsDescriptions[i].m_Size = -1;

                    var field = pType.GetField(Enum.GetName(pType, pValues.GetValue(i)));

                    var arrayAttr = (SampleProviderArrayAttribute)Attribute.GetCustomAttribute(field, typeof(SampleProviderArrayAttribute));
                    if (arrayAttr != null)
                    {
                        provsDescriptions[i].m_IsArray = true;
                        provsDescriptions[i].m_Size = arrayAttr.Size;
                    }
                }

                return new SampleProviderDescriptionData
                {
                    Descriptions = provsDescriptions,
                    SampleProviderCount = pValues.Length
                };
            }

            public static void Initialize(
                out void* jobReflectionData, out ParameterDescriptionData parameterDescriptionData,
                out SampleProviderDescriptionData sampleProviderDescriptionData)
            {
                if (s_JobReflectionData == null)
                    s_JobReflectionData = (void*)JobsUtility.CreateJobReflectionData(typeof(TAudioKernel), JobType.Single, (ExecuteKernelFunction)Execute);

                if (s_ParameterDescriptionData.Descriptions == null)
                    s_ParameterDescriptionData = CreateParameterDescription();

                if (s_SampleProviderDescriptionData.Descriptions == null)
                    s_SampleProviderDescriptionData = CreateSampleProviderDescription();

                jobReflectionData = s_JobReflectionData;
                parameterDescriptionData = s_ParameterDescriptionData;
                sampleProviderDescriptionData = s_SampleProviderDescriptionData;
            }

            delegate void ExecuteKernelFunction(ref TAudioKernel jobData, IntPtr audioInfoPtr, IntPtr functionIndex, ref JobRanges ranges, int ignored2);

            // Needs to be public so that Burst can pick this up during its compilation. Otherwise
            // should be private.
            public static void Execute(ref TAudioKernel kernel, IntPtr audioDataPtr, IntPtr functionIndex, ref JobRanges ranges, int ignored2)
            {
                switch (functionIndex.ToInt32())
                {
                    case 0:
                        ExecuteKernel(ref kernel, audioDataPtr);
                        break;
                    case 1:
                        InitializeKernel(ref kernel);
                        break;
                    case 2:
                        DisposeKernel(ref kernel);
                        break;
                    case 3:
                        ExecuteKernelWithWrapper(audioDataPtr);
                        break;
                    default:
                        throw new ArgumentException($"Invalid function index {functionIndex}", nameof(functionIndex));
                }
            }

            private static void ExecuteKernelWithWrapper(IntPtr audioDataPtr)
            {
                var audioData = (NativeAudioData*)audioDataPtr;
                var graph = ((DSPGraph*)audioData->GraphRegistry)[audioData->GraphIndex];
                graph.RunJobForNode(graph.LookupNode(audioData->NodeIndex));
            }

            private static void ExecuteKernel(ref TAudioKernel kernel, IntPtr audioDataPtr)
            {
                var audioData = (NativeAudioData*)audioDataPtr.ToPointer();
                var context = new ExecuteContext<TParameters, TProviders>
                {
                    DSPClock = (long)audioData->DSPClock,
                    DSPBufferSize = (int)audioData->DSPBufferSize,
                    SampleRate = (int)audioData->SampleRate,
                    GraphBuffer = (DSPGraph*)audioData->GraphRegistry,
                    GraphIndex = audioData->GraphIndex,
                    NodeIndex = audioData->NodeIndex,
                    Inputs = new SampleBufferArray
                    {
                        Count = (int)audioData->InputBufferCount,
                        Buffers = audioData->InputBuffers,
                        SampleCount = (int)audioData->SampleReadCount,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        Safety = audioData->ReadOnlySafetyHandle
#endif
                    },
                    Outputs = new SampleBufferArray
                    {
                        Count = (int)audioData->OutputBufferCount,
                        Buffers = audioData->OutputBuffers,
                        SampleCount = (int)audioData->SampleReadCount,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        Safety = audioData->ReadWriteSafetyHandle
#endif
                    },
                    Parameters = new ParameterData<TParameters>
                    {
                        Parameters = audioData->Parameters,
                        ParametersCount = (int)audioData->ParameterCount,
                        ReadLength = (int)audioData->SampleReadCount,
                        ParameterKeys = audioData->ParameterKeys,
                        DSPClock = (long)audioData->DSPClock,
                    },
                    Providers = new SampleProviderContainer<TProviders>
                    {
                        Count = (int)audioData->SampleProviderIndexCount,
                        SampleProviderIndices = audioData->SampleProviderIndices,
                        SampleProvidersCount = (int)audioData->SampleProviderCount,
                        SampleProviders = audioData->SampleProviders,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        Safety = audioData->ReadOnlySafetyHandle
#endif
                    }
                };

                kernel.Execute(ref context);

                // Set final parameter values for readback to native
                for (int i = 0; i < context.Parameters.ParametersCount; ++i)
                    context.Parameters.Parameters[i].m_Value = context.Parameters.GetFloat(i, context.DSPBufferSize - 1);
            }

            private static void InitializeKernel(ref TAudioKernel kernel)
            {
                kernel.Initialize();
            }

            private static void DisposeKernel(ref TAudioKernel kernel)
            {
                kernel.Dispose();
            }
        }

        public static unsafe void GetReflectionData<TAudioKernel, TParameters, TProviders>(
            out void* jobReflectionData, out ParameterDescriptionData parameterDescriptionData,
            out SampleProviderDescriptionData sampleProviderDescriptionData)
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
        {
            AudioKernelJobStructProduce<TAudioKernel, TParameters, TProviders>.Initialize(
                out jobReflectionData, out parameterDescriptionData, out sampleProviderDescriptionData);
        }

        public static unsafe void GetReflectionData<TAudioKernel, TParameters, TProviders>(
            out void* jobReflectionData, out ParameterDescriptionData parameterDescriptionData)
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
        {
            AudioKernelJobStructProduce<TAudioKernel, TParameters, TProviders>.Initialize(
                out jobReflectionData, out parameterDescriptionData, out SampleProviderDescriptionData ignored);
        }

        public static unsafe void GetReflectionData<TAudioKernel, TParameters, TProviders>(
            out SampleProviderDescriptionData sampleProviderDescriptionData)
            where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
            where TParameters  : unmanaged, Enum
            where TProviders   : unmanaged, Enum
        {
            AudioKernelJobStructProduce<TAudioKernel, TParameters, TProviders>.Initialize(
                out void* ignoredJobReflectionData, out ParameterDescriptionData ignoredParameterDescriptionData,
                out sampleProviderDescriptionData);
        }
    }
}
