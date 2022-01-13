namespace Unity.Media.Utilities
{
    /// <summary>
    /// Modes for writing samples to a buffer
    /// </summary>
    public enum BufferWriteMode
    {
        /// <summary>
        /// Overwrite current values with new values
        /// </summary>
        Overwrite = 0,

        /// <summary>
        /// Add new values to current values
        /// </summary>
        Additive,
    }
}
