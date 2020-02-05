using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A List<T>-like blittable data structure backed by native allocations
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    public unsafe struct GrowableBuffer<T>: IDisposable, IEquatable<GrowableBuffer<T>>, IReadOnlyList<T>, IList<T>, IValidatable
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private readonly T** m_Array;
        [NativeDisableUnsafePtrRestriction]
        private readonly int* m_Count;
        [NativeDisableUnsafePtrRestriction]
        private readonly int* m_Capacity;
        private readonly Allocator m_Allocator;

        /// <summary>
        /// Gets the unmanaged description for the GrowableBuffer
        /// </summary>
        public GrowableBufferDescription Description
        {
            get
            {
                Validate();
                return new GrowableBufferDescription
                {
                    Allocator = m_Allocator,
                    Data = m_Array,
                    ElementTypeHash = UnsafeUtility.SizeOf<T>(),
                };
            }
        }

        /// <summary>
        /// Get the pointer to the start of the buffer data
        /// </summary>
        public T* UnsafeDataPointer => *m_Array;

        /// <summary>
        /// Create a new GrowableBuffer
        /// </summary>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="initialCapacity">The initial buffer capacity</param>
        /// <exception cref="ArgumentOutOfRangeException">If initialCapacity &lt;= 0</exception>
        public GrowableBuffer(Allocator allocator, int initialCapacity = 16)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            m_Allocator = allocator;
            m_Array = (T**)Utility.AllocateUnsafe<IntPtr>(3, allocator);
            m_Count = (int*)(m_Array + 1);
            m_Capacity = (int*)(m_Array + 2);
            *m_Array = Utility.AllocateUnsafe<T>(initialCapacity,  allocator);
            *m_Count = 0;
            *m_Capacity = initialCapacity;
        }

        /// <summary>
        /// Create a new GrowableBuffer
        /// </summary>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="source">A source buffer to copy</param>
        public GrowableBuffer(Allocator allocator, GrowableBuffer<T> source) : this(allocator, source.Count)
        {
            UnsafeUtility.MemCpy(*m_Array, *source.m_Array, source.Count * UnsafeUtility.SizeOf<T>());
            *m_Count = source.Count;
        }

        /// <summary>
        /// Create a new GrowableBuffer
        /// </summary>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="source">A source array to copy</param>
        public GrowableBuffer(Allocator allocator, T[] source) : this(allocator, source.Length)
        {
            fixed(T* buffer = source)
            {
                UnsafeUtility.MemCpy(*m_Array, buffer, source.Length * UnsafeUtility.SizeOf<T>());
            }
            *m_Count = source.Length;
        }

        /// <summary>
        /// Create a new GrowableBuffer
        /// </summary>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="source">A source list to copy</param>
        public GrowableBuffer(Allocator allocator, IReadOnlyList<T> source) : this(allocator, source.Count)
        {
            for (int i = 0; i < source.Count; ++i)
                (*m_Array)[i] = source[i];
            *m_Count = source.Count;
        }

        private GrowableBuffer(Allocator allocator, T** array)
        {
            m_Allocator = allocator;
            m_Array = array;
            m_Count = (int*)(array + 1);
            m_Capacity = (int*)(array + 2);
        }

        /// <summary>
        /// Inflate a GrowableBuffer from an unmanaged description
        /// </summary>
        /// <param name="description">A description created by calling GrowableBuffer<T>.Description</param>
        /// <returns>A GrowableBuffer inflated from the provided description</returns>
        public static GrowableBuffer<T> FromDescription(GrowableBufferDescription description)
        {
            ValidateDescription(description);
            return new GrowableBuffer<T>(description.Allocator, (T**)description.Data);
        }

        private static void ValidateDescription(GrowableBufferDescription description)
        {
            ValidateDescriptionWithMeaningfulMessages(description);
            if (description.Data == null || description.Allocator == Allocator.Invalid || description.Allocator == Allocator.None)
                throw new ArgumentException("description");
        }

        [BurstDiscard]
        private static void ValidateDescriptionWithMeaningfulMessages(GrowableBufferDescription description)
        {
            if (description.Data == null || description.Allocator == Allocator.Invalid || description.Allocator == Allocator.None)
                throw new ArgumentException(nameof(description));
        }

        // Interface implementations
        public void Dispose()
        {
            Validate();
            Utility.FreeUnsafe(*m_Array, m_Allocator);
            Utility.FreeUnsafe(m_Array, m_Allocator);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Equals(GrowableBuffer<T> other)
        {
            return m_Array == other.m_Array;
        }

        public override bool Equals(object obj)
        {
            return obj is GrowableBuffer<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)m_Array;
        }

        public void Add(T item)
        {
            CheckCapacity(*m_Count + 1);
            (*m_Array)[*m_Count] = item;
            ++*m_Count;
        }

        public void AddRange(IList<T> items)
        {
            CheckCapacity(*m_Count + items.Count);
            foreach (var item in items)
                Add(item);
        }

        public void AddRange(IEnumerable<T> items)
        {
            // Prechecking capacity requires us to iterate items twice - worth it?
            foreach (var item in items)
                Add(item);
        }

        public void Clear()
        {
            *m_Count = 0;
        }

        public bool Contains(T item)
        {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (arrayIndex < 0 || arrayIndex + *m_Count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            fixed(T* buffer = array)
            {
                UnsafeUtility.MemCpy(buffer + arrayIndex, *m_Array, *m_Count * UnsafeUtility.SizeOf<T>());
            }
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public int Count
        {
            get
            {
                Validate();
                return *m_Count;
            }
        }

        public bool IsReadOnly => false;

        public int IndexOf(T item)
        {
#if ENABLE_IL2CPP
            // This version works with il2cpp but not burst
            for (int index = 0; index < *m_Count; ++index)
                if (item.Equals((*m_Array)[index]))
                    return index;
#else
            // This version works with burst but not il2cpp
            // https://github.cds.internal.unity3d.com/unity/il2cpp/issues/733
            var hash = item.GetHashCode();
            for (int index = 0; index < *m_Count; ++index)
                if (hash == (*m_Array)[index].GetHashCode())
                    return index;
#endif
            return -1;
        }

        public void Insert(int index, T item)
        {
            ValidateIndex(index);
            CheckCapacity(*m_Count + 1);
            for (int i = *m_Count - 1; i >= index; --i)
                (*m_Array)[i + 1] = (*m_Array)[i];
            (*m_Array)[index] = item;
            ++*m_Count;
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index > *m_Count)
                throw new IndexOutOfRangeException();
        }

        public void RemoveAt(int index)
        {
            ValidateIndex(index);
            for (int i = index; i < *m_Count - 1; ++i)
                (*m_Array)[i] = (*m_Array)[i + 1];
            --*m_Count;
        }

        public T this[int index]
        {
            get
            {
                Validate();
                ValidateIndex(index);
                return (*m_Array)[index];
            }
            set
            {
                Validate();
                ValidateIndex(index);
                (*m_Array)[index] = value;
            }
        }

        public bool Valid => m_Array != null;

        private void Validate()
        {
            if (!Valid)
                throw new InvalidOperationException();
        }

        /// <summary>
        /// Ensure that the buffer has at least the given capacity
        /// </summary>
        /// <param name="newCapacity">The required capacity</param>
        public void CheckCapacity(int newCapacity)
        {
            if (newCapacity <= Capacity)
                return;
            *m_Capacity = Math.Max(Capacity * 2, newCapacity);
            var newArray = Utility.AllocateUnsafe<T>(Capacity,  m_Allocator);
            UnsafeUtility.MemCpy(newArray, *m_Array, Count * UnsafeUtility.SizeOf<T>());
            Utility.FreeUnsafe(*m_Array, m_Allocator);
            *m_Array = newArray;
        }

        public int Capacity
        {
            get
            {
                Validate();
                return *m_Capacity;
            }
        }

        private struct Enumerator : IEnumerator<T>
        {
            readonly GrowableBuffer<T> m_List;
            int m_Index;
            const int kInvalidIndex = -1;

            public Enumerator(ref GrowableBuffer<T> list)
            {
                m_List = list;
                m_Index = kInvalidIndex;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                ++m_Index;
                return m_Index < m_List.Count;
            }

            public void Reset()
            {
                m_Index = kInvalidIndex;
            }

            public T Current => m_List[m_Index];

            object IEnumerator.Current => Current;
        }
    }

    /// <summary>
    /// An unmanaged data structure meant to describe a GrowableBuffer<T>
    /// </summary>
    public unsafe struct GrowableBufferDescription
    {
        [NativeDisableUnsafePtrRestriction]
        public void* Data;
        public long ElementTypeHash;
        public Allocator Allocator;
    }
}
