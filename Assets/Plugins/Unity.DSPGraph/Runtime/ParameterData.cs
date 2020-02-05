using System;
using System.Runtime.InteropServices;
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
    public unsafe struct ParameterData<P> where P : unmanaged, Enum
    {
        /// <param name="parameter">
        /// A specific enum value from the parameter enumeration specified in the
        /// audio job.
        /// <see cref="IAudioKernel{TParameters,TProviders}"/>
        /// </param>
        /// <param name="sampleOffset">
        /// The time to evaluate the parameter at.
        /// </param>
        /// <returns>The value of a parameter.</returns>
        public float GetFloat(P parameter, int sampleOffset)
        {
            return GetFloat(UnsafeUtility.EnumToInt(parameter), sampleOffset);
        }

        internal float GetFloat(int parameter, int sampleOffset)
        {
            if (parameter >= ParametersCount)
                throw new ArgumentException("Undefined parameter in ParameterData.GetValue", nameof(parameter));

            if (ParameterKeys == null || Parameters[parameter].m_KeyIndex == DSPParameterKey.NullIndex)
                return Parameters[parameter].m_Value;

            if (sampleOffset >= ReadLength)
                throw new ArgumentOutOfRangeException(nameof(sampleOffset), $"sampleOffset {sampleOffset} greater than the read length {ReadLength} of the frame");

            return DSPParameterInterpolator.Generate(sampleOffset, ParameterKeys,
                Parameters[parameter].m_KeyIndex, DSPClock, Parameters[parameter].m_Min,
                Parameters[parameter].m_Max, Parameters[parameter].m_Value)[0];
        }

        internal NativeDSPParameter* Parameters;
        internal DSPParameterKey* ParameterKeys;
        internal int ParametersCount;
        internal int ReadLength;
        internal long DSPClock;
    }
}
