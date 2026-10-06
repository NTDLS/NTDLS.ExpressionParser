using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Records the result of each step of an evaluation, in visit order, so that later evaluations can replay them.
    /// This is a mutable struct: it must only be used through the field that holds it, never through a copy.
    /// </summary>
    internal struct VisitorCache<T>(int initialCapacity) where T : struct
    {
        private int _utilized = 0;
        private int _next = 0;
        private T[] _items = initialCapacity == 0 ? [] : new T[initialCapacity];

        /// <summary>
        /// True when _items belongs to another cache (the shared template) and must be copied before it is written to.
        /// </summary>
        private bool _isShared = false;

        public readonly int Utilized => _utilized;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _next = 0;
        }

        /// <summary>
        /// Takes a private copy of the source's recorded steps.
        /// </summary>
        public void CopyFrom(in VisitorCache<T> source)
        {
            _items = source._items.AsSpan(0, source._utilized).ToArray();
            _utilized = source._utilized;
            _next = source._next;
            _isShared = false;
        }

        /// <summary>
        /// References the source's recorded steps without copying them. The array is only copied if this cache is
        /// later written to, so the source must not modify its array in place afterwards (it may only replace it).
        /// </summary>
        public void ShareFrom(in VisitorCache<T> source)
        {
            _items = source._items;
            _utilized = source._utilized;
            _next = source._next;
            _isShared = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet([NotNullWhen(true)] out T value, out int cacheIndex)
        {
            if (_next < _utilized)
            {
                cacheIndex = _next++;
                value = _items[cacheIndex];
                return true;
            }
            cacheIndex = _next++;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T StoreInvalid(int cacheIndex) => Store(cacheIndex, default);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Store(int cacheIndex, T value)
        {
            _utilized++;
            if (_isShared || cacheIndex >= _items.Length)
            {
                EnsureWritable(cacheIndex);
            }
            _items[cacheIndex] = value;
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureWritable(int cacheIndex)
        {
            int capacity = Math.Max(_items.Length, 4);
            while (capacity <= cacheIndex)
                capacity *= 2;

            var items = new T[capacity];
            _items.AsSpan().CopyTo(items);
            _items = items;
            _isShared = false;
        }
    }
}
