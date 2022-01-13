using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeDSPParameter
    {
        internal float m_Value;
        internal int m_KeyIndex;
        internal float m_Min;
        internal float m_Max;
    }

    /// <summary>
    /// Apply to parameter enumerations to restrict the valid values a parameter can have.
    /// <see cref="IAudioKernel{TParameters,TProviders}"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ParameterRangeAttribute : Attribute
    {
        internal readonly float Min;
        internal readonly float Max;

        /// <summary></summary>
        /// <param name="min">The minimum value</param>
        /// <param name="max">The maximum value</param>
        public ParameterRangeAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// Apply to parameter enumerations to initialize a parameter to a default value.
    /// <see cref="IAudioKernel{TParameters,TProviders}"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ParameterDefaultAttribute : Attribute
    {
        internal readonly float DefaultValue;

        /// <summary></summary>
        /// <param name="defaultVal">The default value for this parameter</param>
        public ParameterDefaultAttribute(float defaultVal)
        {
            DefaultValue = defaultVal;
        }
    }

    /// <summary>
    /// Provides methods for retrieving parameter values at a specific time
    /// inside a mix.
    /// <see cref="ExecuteContext{TParameters,TProviders}"/>
    /// <seealso cref="IAudioKernel{TParameters,TProviders}"/>
    /// </summary>
    /// <typeparam name="TParameter">The enum type for the parameter</typeparam>
    public unsafe struct ParameterData<TParameter> where TParameter : unmanaged, Enum
    {
        /// <summary>
        /// Get the value of a parameter at a sample offset
        /// </summary>
        /// <param name="parameter">
        /// A specific enum value from the parameter enumeration specified in the
        /// audio job.
        /// <see cref="IAudioKernel{TParameters,TProviders}"/>
        /// </param>
        /// <param name="sampleOffset">
        /// The time to evaluate the parameter at.
        /// </param>
        /// <returns>The value of a parameter.</returns>
        public float GetFloat(TParameter parameter, int sampleOffset)
        {
            return GetFloat(UnsafeUtility.EnumToInt(parameter), sampleOffset);
        }

        internal float GetFloat(int parameter, int sampleOffset)
        {
            if (parameter >= ParametersCount)
                ThrowUndefinedParameterError(parameter);

            if (ParameterKeys == null || Parameters[parameter].m_KeyIndex == DSPParameterKey.NullIndex)
                return Parameters[parameter].m_Value;

            if (sampleOffset >= ReadLength)
                ThrowInvalidSampleOffsetError(sampleOffset);

            return DSPParameterInterpolator.Generate(sampleOffset, ParameterKeys,
                Parameters[parameter].m_KeyIndex, DSPClock, Parameters[parameter].m_Min,
                Parameters[parameter].m_Max, Parameters[parameter].m_Value)[0];
        }

        private static void ThrowUndefinedParameterError(int parameter)
        {
            ThrowUndefinedParameterErrorMono(parameter);
            ThrowUndefinedParameterErrorBurst(parameter);
        }

        [BurstDiscard]
        private static void ThrowUndefinedParameterErrorMono(int parameter)
        {
            throw new ArgumentException("Undefined parameter in ParameterData.GetValue", nameof(parameter));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowUndefinedParameterErrorBurst(int parameter)
        {
            throw new ArgumentException("Undefined parameter in ParameterData.GetValue", nameof(parameter));
        }

        private void ThrowInvalidSampleOffsetError(int sampleOffset)
        {
            ThrowInvalidSampleOffsetErrorMono(sampleOffset);
            ThrowInvalidSampleOffsetErrorBurst(sampleOffset);
        }

        [BurstDiscard]
        private void ThrowInvalidSampleOffsetErrorMono(int sampleOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleOffset), $"sampleOffset {sampleOffset} greater than the read length {ReadLength} of the frame");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ThrowInvalidSampleOffsetErrorBurst(int sampleOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleOffset), $"sampleOffset {sampleOffset} greater than the read length {ReadLength} of the frame");
        }

        internal NativeDSPParameter* Parameters;
        internal DSPParameterKey* ParameterKeys;
        internal int ParametersCount;
        internal int ReadLength;
        internal long DSPClock;
    }
}
