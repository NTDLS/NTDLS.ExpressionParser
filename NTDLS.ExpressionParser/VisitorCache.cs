using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    internal class VisitorCache<T>(int initialCapacity) where T : struct
    {
        private int _utilized = 0;
        private int _next = 0;
        private T[] _items = new T[initialCapacity];

        public int Utilized => _utilized;
        public int Allocated => _items.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _next = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(VisitorCache<T> source)
        {
            _items = new T[source.Utilized];

            for (int i = 0; i < source.Utilized; i++)
            {
                _items[i] = source._items[i];
            }
            _utilized = source._utilized;
            _next = source._next;
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
        public T StoreInvalid(int cacheIndex)
        {
            _utilized++;
            if (cacheIndex >= _items.Length)
            {
                Array.Resize(ref _items, cacheIndex + 1);
            }
            _items[cacheIndex] = default;
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Store(int cacheIndex, T value)
        {
            _utilized++;
            if (cacheIndex >= _items.Length)
            {
                Array.Resize(ref _items, cacheIndex + 1);
            }
            _items[cacheIndex] = value;
            return value;
        }
    }
}
