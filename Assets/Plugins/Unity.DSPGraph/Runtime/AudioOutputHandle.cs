using System;

namespace Unity.Audio
{
    /// <summary>
    /// A handle type representing an <see cref="IAudioOutput"/>
    /// </summary>
    public struct AudioOutputHandle : IDisposable, IHandle<AudioOutputHandle>
    {
        internal Handle Handle;

        /// <summary>
        /// Whether this handle is valid
        /// </summary>
        public bool Valid => Handle.Valid;

        /// <summary>
        /// Disposes the native output hook within Unity
        /// </summary>
        public void Dispose()
        {
            AudioOutputExtensions.DisposeOutputHook(ref this);
        }

        /// <summary>
        /// Whether this is the same handle as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(AudioOutputHandle other)
        {
            return Handle.Equals(other.Handle);
        }

        /// <summary>
        /// Whether this is the same handle as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is AudioOutputHandle other && Equals(other);
        }

        /// <summary>
        /// Returns a unique hash code for this handle
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Handle.GetHashCode();
        }
    }
}
