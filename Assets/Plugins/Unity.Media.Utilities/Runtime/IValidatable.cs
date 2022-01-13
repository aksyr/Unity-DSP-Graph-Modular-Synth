using System;
using System.Diagnostics;
using Unity.Burst;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// An interface for providing validation
    /// </summary>
    public interface IValidatable
    {
        /// <summary>
        /// Should return true when the instance is valid and false otherwise
        /// </summary>
        bool Valid { get; }
    }

    /// <summary>
    /// Extensions for <see cref="IValidatable"/>
    /// </summary>
    public static class ValidatableExtensions
    {
        /// <summary>
        /// Throw <see cref="InvalidOperationException"/> when <see cref="IValidatable.Valid"/> returns false on <paramref name="self"/>
        /// </summary>
        /// <param name="self">An instance of <typeparamref name="T"/></param>
        /// <typeparam name="T">An <see cref="IValidatable"/> implementation</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="self"/> is not valid</exception>
        public static void Validate<T>(this T self)
            where T : struct, IValidatable
        {
            ValidateMono(self);
            ValidateBurst(self);
        }

        [BurstDiscard]
        private static void ValidateMono<T>(T self)
            where T : struct, IValidatable
        {
            if (!self.Valid)
                throw new InvalidOperationException();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateBurst<T>(T self)
            where T : struct, IValidatable
        {
            if (!self.Valid)
                throw new InvalidOperationException();
        }
    }
}
