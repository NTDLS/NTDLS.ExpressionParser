using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class ExpressionState
    {
        public string WorkingText { get; set; } = string.Empty;

        public readonly StringBuilder _buffer = new();

        public int _nextPreParsedCacheSlot = 0;
        public readonly int _expressionHash;
        public PreComputedCacheItem[] _preComputedCache;

        public int _nextPreComputedCacheSlot = 0;
        public int _operationCount = 0;
        public PreParsedCacheItem?[] _preParsedCache;

        public ExpressionOptions Options;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            Options = options;

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPreComputedCacheSlot = sanitized.ConsumedPreComputedCacheSlots;
            _preComputedCache = new PreComputedCacheItem[_operationCount];
            _preParsedCache = new PreParsedCacheItem?[_operationCount];
        }

        #region Pre-Parsed Cache Management.

        public int ConsumeNextPreParsedCacheSlot()
        {
            return _nextPreParsedCacheSlot++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPreParsedCache(int slot, [NotNullWhen(true)] out PreParsedCacheItem value)
        {
            if (slot < _preParsedCache.Length)
            {
                var cached = _preParsedCache[slot];
                if (cached != null)
                {
                    value = cached.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StorePreParsedCache(int slot, PreParsedCacheItem value)
        {
            if (slot >= _preParsedCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _preParsedCache, (_preParsedCache.Length + 1) * 2);
            }
            _preParsedCache[slot] = value;
        }

        #endregion

        #region Pre-Computed Cache Management.

        public int ConsumeNextPreComputedCacheSlot(out string cacheKey)
        {
            int cacheSlot = _nextPreComputedCacheSlot++;
            cacheKey = $"${cacheSlot}$";
            return cacheSlot;
        }

        public string StorePreComputedCacheItem(double? value, bool isVariable = false)
        {
            var cacheSlot = ConsumeNextPreComputedCacheSlot(out var cacheKey);

            if (cacheSlot >= _preComputedCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _preComputedCache, (_preComputedCache.Length + 1) * 2);
            }

            _preComputedCache[cacheSlot] = new PreComputedCacheItem()
            {
                IsVariable = isVariable,
                ComputedValue = value
            };

            return cacheKey;
        }

        public PreComputedCacheItem GetPreComputedCacheItem(ReadOnlySpan<char> span)
        {
            switch (span.Length)
            {
                case 1:
                    return _preComputedCache[span[0] - '0'];
                default:
                    int index = 0;
                    for (int i = 0; i < span.Length; i++)
                        index = index * 10 + (span[i] - '0');
                    return _preComputedCache[index];
            }
        }

        #endregion
    }

}
