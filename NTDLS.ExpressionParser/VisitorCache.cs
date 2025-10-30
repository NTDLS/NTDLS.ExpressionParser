using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    internal class VisitorCache<T>(int initialCapacity) where T : struct
    {
        public bool AllowGrowth { get; private set; } = true;
        private int _consumed = 0;
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
                if (index < 0 || index >= _consumed)
                    throw new IndexOutOfRangeException();

                return ref _items[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(VisitorCache<T> target)
        {
            for (int i = 0; i < _consumed; i++)
            {
                target.Store(_items[i]);
            }
            target.AllowGrowth = AllowGrowth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyToNoFurtherGrowth(VisitorCache<T> target)
        {
            for (int i = 0; i < _consumed; i++)
            {
                target.Store(_items[i]);
            }
            target.AllowGrowth = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet([NotNullWhen(true)] out T value)
        {
            if(!AllowGrowth && _next > _consumed)
            {
                throw new InvalidOperationException("VisitorCache cannot grow further.");
            }

            if (_next < _consumed)
            {
                value = _items[_next].Value;
                _next++;
                return _items[_next].IsValid;
            }
            _next++;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(T value, bool isValid)
        {
            if (!AllowGrowth && _next > _consumed)
            {
                throw new InvalidOperationException("VisitorCache cannot grow further.");
            }
            if (_consumed >= _items.Length)
            {
                Array.Resize(ref _items, (_items.Length + 1) * 2);
            }
            _items[_consumed++] = new VisitorCacheContainer<T>(value, isValid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(VisitorCacheContainer<T> value)
        {
            if (!AllowGrowth && _next > _consumed)
            {
                throw new InvalidOperationException("VisitorCache cannot grow further.");
            }
            if (_consumed >= _items.Length)
            {
                Array.Resize(ref _items, (_items.Length + 1) * 2);
            }
            _items[_consumed++] = value;
        }
    }
}
