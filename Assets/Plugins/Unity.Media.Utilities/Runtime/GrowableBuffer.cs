using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Media.Utilities
{
    /// <summary>
    /// A List&lt;T&gt;-like blittable data structure backed by native allocations
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
        /// Get a pointer to the buffer data
        /// </summary>
        public T** UnsafeDataPointer => m_Array;

        /// <summary>
        /// Create a new GrowableBuffer
        /// </summary>
        /// <param name="allocator">The native allocator to use</param>
        /// <param name="initialCapacity">The initial buffer capacity</param>
        /// <exception cref="ArgumentOutOfRangeException">If initialCapacity &lt;= 0</exception>
        public GrowableBuffer(Allocator allocator, int initialCapacity = 16)
        {
            VerifyInitialCapacity(initialCapacity);
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

        // Interface implementations
        /// <summary>
        /// Dispose buffer storage
        /// </summary>
        public void Dispose()
        {
            this.Validate();
            Utility.FreeUnsafe(*m_Array, m_Allocator);
            Utility.FreeUnsafe(m_Array, m_Allocator);
        }

        /// <summary>
        /// See System.Collections.Generic.IEnumerable<T>
        /// </summary>
        /// <returns></returns>
        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        /// <summary>
        /// See System.Collections.IEnumerable
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Whether this is the same buffer as another instance
        /// </summary>
        /// <param name="other">The other instance to compare</param>
        /// <returns></returns>
        public bool Equals(GrowableBuffer<T> other)
        {
            return m_Array == other.m_Array;
        }

        /// <summary>
        /// Whether this is the same buffer as another instance
        /// </summary>
        /// <param name="obj">The other instance to compare</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is GrowableBuffer<T> other && Equals(other);
        }

        /// <summary>
        /// Returns a unique hash code for this buffer
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return (int)m_Array;
        }

        /// <summary>
        /// Append an item to the end of the buffer
        /// </summary>
        /// <param name="item">The item to add</param>
        public void Add(T item)
        {
            CheckCapacity(*m_Count + 1);
            (*m_Array)[*m_Count] = item;
            ++*m_Count;
        }

        /// <summary>
        /// Append a list of items to the end of the buffer
        /// </summary>
        /// <param name="items">The items to add</param>
        /// <typeparam name="TList">An unmanaged IList<T> implementation</typeparam>
        public void AddRange<TList>(TList items)
            where TList : unmanaged, IList<T>
        {
            CheckCapacity(*m_Count + items.Count);
            for (int i = 0; i < items.Count; ++i)
                Add(items[i]);
        }

        /// <summary>
        /// Append a list of items to the end of the buffer
        /// </summary>
        /// <param name="items">The items to add</param>
        public void AddRange(IList<T> items)
        {
            CheckCapacity(*m_Count + items.Count);
            for (int i = 0; i < items.Count; ++i)
                Add(items[i]);
        }

        /// <summary>
        /// Append a group of items to the end of the buffer
        /// </summary>
        /// <param name="items">The items to add</param>
        public void AddRange(IEnumerable<T> items)
        {
            // Prechecking capacity requires us to iterate items twice - worth it?
            foreach (var item in items)
                Add(item);
        }

        /// <summary>
        /// Mark the buffer as empty
        /// </summary>
        /// <remarks>This does not trigger deallocation</remarks>
        public void Clear()
        {
            *m_Count = 0;
        }

        /// <summary>
        /// Whether a given item is contained in the buffer
        /// </summary>
        /// <param name="item">The item to find</param>
        /// <returns></returns>
        public bool Contains(T item)
        {
            return IndexOf(item) >= 0;
        }

        /// <summary>
        /// Copy the buffer to an array
        /// </summary>
        /// <param name="array">The destination array</param>
        /// <param name="arrayIndex">The array index at which to begin writing</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there is insufficient space in the array, considering <paramref name="arrayIndex"/></exception>
        public void CopyTo(T[] array, int arrayIndex)
        {
            ValidateCopyArguments(array, arrayIndex);

            fixed(T* buffer = array)
            {
                UnsafeUtility.MemCpy(buffer + arrayIndex, *m_Array, *m_Count * UnsafeUtility.SizeOf<T>());
            }
        }

        private void ValidateCopyArguments(T[] array, int arrayIndex)
        {
            ValidateCopyArgumentsMono(array, arrayIndex);
            ValidateCopyArgumentsBurst(array, arrayIndex);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateCopyArgumentsBurst(T[] array, int arrayIndex)
        {
            if (arrayIndex < 0 || arrayIndex + *m_Count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        [BurstDiscard]
        private void ValidateCopyArgumentsMono(T[] array, int arrayIndex)
        {
            if (arrayIndex < 0 || arrayIndex + *m_Count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        /// <summary>
        /// Remove an item from the buffer
        /// </summary>
        /// <param name="item">The item to remove</param>
        /// <returns>Whether the item was removed from the buffer</returns>
        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        /// <summary>
        /// The number of items in the buffer
        /// </summary>
        /// <remarks><seealso cref="Capacity"/></remarks>
        public int Count
        {
            get
            {
                this.Validate();
                return *m_Count;
            }
        }

        /// <summary>
        /// Whether the buffer is read-only
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Find an item in the buffer
        /// </summary>
        /// <param name="item">The item to be found</param>
        /// <returns>The index of the found item, or -1</returns>
        public int IndexOf(T item)
        {
            var hash = item.GetHashCode();
            for (int index = 0; index < *m_Count; ++index)
            {
                // Assigning a reference to the item here to work around
                // https://github.cds.internal.unity3d.com/unity/il2cpp/issues/733
                ref T thing = ref (*m_Array)[index];
                if (hash == thing.GetHashCode())
                    return index;
            }

            return -1;
        }

        /// <summary>
        /// Insert an item into the buffer
        /// </summary>
        /// <param name="index">The position at which to insert the item</param>
        /// <param name="item">The item to insert</param>
        /// <remarks>
        /// <see cref="GrowableBuffer{T}.Count"/> can be provided as the index, in which case it will act like <see cref="GrowableBuffer{T}.Add"/>
        /// </remarks>
        /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> is out of range</exception>
        public void Insert(int index, T item)
        {
            ValidateIndexForInsertion(index);
            CheckCapacity(*m_Count + 1);
            for (int i = *m_Count - 1; i >= index; --i)
                (*m_Array)[i + 1] = (*m_Array)[i];
            (*m_Array)[index] = item;
            ++*m_Count;
        }

        // We specifically want to support inserting to index *m_Count here,
        // giving the same result as calling Add()
        private void ValidateIndexForInsertion(int index)
        {
            ValidateIndexForInsertionMono(index);
            ValidateIndexForInsertionBurst(index);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateIndexForInsertionBurst(int index)
        {
            if (index < 0 || index > *m_Count)
                throw new IndexOutOfRangeException();
        }

        [BurstDiscard]
        private void ValidateIndexForInsertionMono(int index)
        {
            if (index < 0 || index > *m_Count)
                throw new IndexOutOfRangeException();
        }

        private void ValidateIndex(int index)
        {
            ValidateIndexMono(index);
            ValidateIndexBurst(index);
        }

        [BurstDiscard]
        private void ValidateIndexMono(int index)
        {
            if ((uint)index >= *m_Count)
                throw new IndexOutOfRangeException($"Index is out of range of size {*m_Count}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateIndexBurst(int index)
        {
            if ((uint)index >= *m_Count)
                throw new IndexOutOfRangeException($"Index is out of range of size {*m_Count}");
        }

        /// <summary>
        /// Remove the item at <paramref name="index"/>
        /// </summary>
        /// <param name="index">The index of the item to remove</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> is out of range</exception>
        public void RemoveAt(int index)
        {
            ValidateIndex(index);
            for (int i = index; i < *m_Count - 1; ++i)
                (*m_Array)[i] = (*m_Array)[i + 1];
            --*m_Count;
        }

        /// <summary>
        /// Get an item in the buffer
        /// </summary>
        /// <param name="index">The index of the item to retrieve</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> is out of range</exception>
        public T this[int index]
        {
            get
            {
                this.Validate();
                ValidateIndex(index);
                return (*m_Array)[index];
            }
            set
            {
                this.Validate();
                ValidateIndex(index);
                (*m_Array)[index] = value;
            }
        }

        /// <summary>
        /// Whether the buffer is valid
        /// </summary>
        public bool Valid => m_Array != null;

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

        /// <summary>
        /// The allocated capacity of the buffer
        /// </summary>
        /// <remarks><seealso cref="Count"/></remarks>
        public int Capacity
        {
            get
            {
                this.Validate();
                return *m_Capacity;
            }
        }

        static void VerifyInitialCapacity(int initialCapacity)
        {
            VerifyInitialCapacityMono(initialCapacity);
            VerifyInitialCapacityBurst(initialCapacity);
        }

        [BurstDiscard]
        static void VerifyInitialCapacityMono(int initialCapacity)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void VerifyInitialCapacityBurst(int initialCapacity)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
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
}
