using Unity.Mathematics;

namespace Unity.Audio
{
    internal struct DSPParameterInterpolator
    {
        public static unsafe float4 Generate(long sampleIndex, DSPParameterKey* keys, int initialKeyIndex, long dspClock, float min, float max, float4 currentValue)
        {
            long absoluteSampleIndex = sampleIndex + dspClock;
            DSPParameterKey key = keys[initialKeyIndex];
            float4 value = currentValue;
            long lastSampleIndex = dspClock;
            float4 lastValue = value;

            while (absoluteSampleIndex > key.DSPClock)
            {
                lastSampleIndex = key.DSPClock;
                lastValue = key.Value;
                if (key.NextKeyIndex == DSPParameterKey.NullIndex)
                    return lastValue;
                key = keys[key.NextKeyIndex];
            }

            if (lastSampleIndex >= key.DSPClock)
                return key.Value;
            float4 delta = (key.Value - lastValue) / (key.DSPClock - lastSampleIndex);
            value = lastValue + (delta * (absoluteSampleIndex - lastSampleIndex));
            return math.clamp(value, min, max);
        }
    }
}
