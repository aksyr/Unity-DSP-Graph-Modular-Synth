using Unity.Mathematics;

namespace Unity.Audio
{
    /// <summary>
    /// A handle representing a connection between two DSPNodes
    /// </summary>
    public struct DSPConnection : IHandle<DSPConnection>
    {
        /// <summary>
        /// An value to indicate that a port index is invalid
        /// </summary>
        public const int InvalidIndex = -1;

        /// <summary>
        /// The minimum possible attenuation value
        /// </summary>
        public const float MinimumAttenuation = 0.0f;

        /// <summary>
        /// The maximum possible attenuation value
        /// </summary>
        public const float MaximumAttenuation = float.MaxValue;

        /// <summary>
        /// The default attenuation value (no attenuation)
        /// </summary>
        public const float DefaultAttenuation = 1.0f;

        /// <summary>
        /// Whether the connection handle is valid
        /// </summary>
        public bool Valid => Handle.Valid && Graph.Valid;

        /// <summary>
        /// Whether this connection is the same as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(DSPConnection other)
        {
            return Handle.Equals(other.Handle) && Graph.Equals(other.Graph);
        }

        /// <summary>
        /// Whether this connection is the same as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPConnection other && Equals(other);
        }

        /// <summary>
        /// Returns a unique hash for this connection
        /// </summary>
        /// <returns></returns>
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
