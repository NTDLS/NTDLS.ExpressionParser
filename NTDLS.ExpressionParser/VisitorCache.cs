using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    internal class VisitorCache<T>(int initialCapacity) where T : struct
    {
        private int _utilized = 0;
        private int _next = 0;
        private VisitorCacheContainer<T>[] _items = new VisitorCacheContainer<T>[initialCapacity];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _next = 0;
        }

        public ref VisitorCacheContainer<T> this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= _utilized)
                    throw new IndexOutOfRangeException();

                return ref _items[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(VisitorCache<T> target)
        {
            for (int i = 0; i < _utilized; i++)
            {
                target.Store(i,_items[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet([NotNullWhen(true)] out T value, out int cacheIndex)
        {
            if (_next < _utilized)
            {
                cacheIndex = _next++;

                if (cacheIndex > _utilized)
                {
                    _utilized = cacheIndex;
                }
                if (cacheIndex >= _items.Length)
                {
                    Array.Resize(ref _items, (cacheIndex + 1) * 2);
                }

                value = _items[cacheIndex].Value;
                return _items[cacheIndex].IsValid;
            }
            cacheIndex = _next++;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T StoreInvalid(int cacheIndex)
        {
            if (cacheIndex > _utilized)
            {
                _utilized = cacheIndex;
            }
            if (cacheIndex >= _items.Length)
            {
                Array.Resize(ref _items, (cacheIndex + 1) * 2);
            }

            _items[cacheIndex] = default;
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Store(int cacheIndex, T value, bool isValid)
        {
            if (cacheIndex > _utilized)
            {
                _utilized = cacheIndex;
            }
            if (cacheIndex >= _items.Length)
            {
                Array.Resize(ref _items, (cacheIndex + 1) * 2);
            }

            _items[cacheIndex] = new VisitorCacheContainer<T>(value, isValid);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VisitorCacheContainer<T> Store(int cacheIndex, VisitorCacheContainer<T> value)
        {
            if (cacheIndex > _utilized)
            {
                _utilized = cacheIndex;
            }
            if (cacheIndex >= _items.Length)
            {
                Array.Resize(ref _items, (cacheIndex+ 1) * 2);
            }
            _items[cacheIndex] = value;
            return value;
        }
    }
}
