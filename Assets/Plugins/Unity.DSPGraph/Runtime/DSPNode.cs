namespace Unity.Audio
{
    /// <summary>
    /// A node in a DSPGraph
    /// </summary>
    public partial struct DSPNode : IHandle<DSPNode>, Media.Utilities.IValidatable
    {
        /// <summary>
        /// Whether this is a valid node in a valid graph
        /// </summary>
        public bool Valid => Handle.Valid && Graph.Valid;

        /// <summary>
        /// Whether this node is the same as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(DSPNode other)
        {
            return Handle.Equals(other.Handle) && Graph.Equals(other.Graph);
        }

        /// <summary>
        /// Whether this node is the same as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPNode other && Equals(other);
        }

        /// <summary>
        /// Returns a unique hash for this node
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Handle.GetHashCode() * 397) ^ Graph.GetHashCode();
            }
        }
    }
}
