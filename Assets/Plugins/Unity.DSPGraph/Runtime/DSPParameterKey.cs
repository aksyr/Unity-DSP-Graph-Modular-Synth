using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Unity.Audio
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DSPParameterKey
    {
        public const int NullIndex = -1;
        public static DSPParameterKey Default => new DSPParameterKey {NextKeyIndex = NullIndex};

        public int NextKeyIndex;
        public bool InUse;

        public long DSPClock;
        public float4 Value;
    }
}
