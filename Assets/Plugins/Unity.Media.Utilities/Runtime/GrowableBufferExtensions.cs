namespace Unity.Media.Utilities
{
    public static class GrowableBufferExtensions
    {
        /// <summary>
        /// Copy a GrowableBuffer to a new array
        /// </summary>
        /// <param name="self">The GrowableBuffer to copy</param>
        /// <typeparam name="T">The element type of self</typeparam>
        /// <returns>A new array copied from self</returns>
        public static T[] ToArray<T>(this GrowableBuffer<T> self)
            where T : unmanaged
        {
            var array = new T[self.Count];
            self.CopyTo(array, 0);
            return array;
        }
    }
}
