using System;

namespace Unity.Audio
{
    /// <summary>
    /// A handle type representing an <typeparamref name="IAudioOutput"/>
    /// </summary>
    public struct AudioOutputHandle : IDisposable, IHandle<AudioOutputHandle>
    {
        internal Handle Handle;

        public bool Valid => Handle.Valid;

        public void Dispose()
        {
            AudioOutputExtensions.DisposeOutputHook(ref this);
        }

        public bool Equals(AudioOutputHandle other)
        {
            return Handle.Equals(other.Handle);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is AudioOutputHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Handle.GetHashCode();
        }
    }
}
