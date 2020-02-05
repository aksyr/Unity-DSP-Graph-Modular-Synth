namespace Unity.Audio
{
    /// <summary>
    /// A node in a DSPGraph
    /// </summary>
    public partial struct DSPNode : IHandle<DSPNode>
    {
        public bool Valid => Handle.Valid && Graph.Valid;

        public bool Equals(DSPNode other)
        {
            return Handle.Equals(other.Handle) && Graph.Equals(other.Graph);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is DSPNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Handle.GetHashCode() * 397) ^ Graph.GetHashCode();
            }
        }
    }
}
