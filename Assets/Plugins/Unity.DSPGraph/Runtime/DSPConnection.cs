using Unity.Mathematics;

namespace Unity.Audio
{
    /// <summary>
    /// A handle representing a connection between two DSPNodes
    /// </summary>
    public struct DSPConnection : IHandle<DSPConnection>
    {
        public const int InvalidIndex = -1;
        public const float MinimumAttenuation = 0.0f;
        public const float MaximumAttenuation = float.MaxValue;
        public const float DefaultAttenuation = 1.0f;

        public bool Valid => Handle.Valid && Graph.Valid;

        public bool Equals(DSPConnection other)
        {
            return Handle.Equals(other.Handle) && Graph.Equals(other.Graph);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPConnection other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Handle.GetHashCode() * 397) ^ Graph.GetHashCode();
            }
        }

        internal Handle Handle;
        internal Handle Graph;

        // FIXME: Just use key index?
        internal DSPNode.Parameter Attenuation;
        internal int OutputNodeIndex;
        internal int OutputPort;
        internal int NextOutputConnectionIndex;

        internal int InputNodeIndex;
        internal int InputPort;
        internal int NextInputConnectionIndex;

        /// <summary>
        /// Whether the connection's attenuation actually affects the signal
        /// </summary>
        internal bool HasAttenuation => Attenuation.KeyIndex != DSPParameterKey.NullIndex || math.any(Attenuation.Value != DefaultAttenuation);
    }
}
