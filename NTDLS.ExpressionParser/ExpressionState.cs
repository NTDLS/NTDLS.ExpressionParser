using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace NTDLS.ExpressionParser
{
    internal class ExpressionState
    {
        public string WorkingText { get; set; } = string.Empty;
        public readonly StringBuilder Buffer;

        private int _nextPlaceholderCacheSlot = 0;
        private PreComputedCacheItem[] _preComputedCache = [];
        private int _nextPreComputedCacheSlot = 0;
        private int _operationCount = 0;
        private PlaceholderCacheItem?[] _placeholderCache = [];
        private readonly ExpressionOptions _options;
        private bool _isPlaceholderCacheHydrated = false;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            _options = options;
            Buffer = new StringBuilder();

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPreComputedCacheSlot = sanitized.ConsumedPreComputedCacheSlots;
            _preComputedCache = new PreComputedCacheItem[sanitized.OperationCount];
            _placeholderCache = new PlaceholderCacheItem?[_operationCount];
            _nextPlaceholderCacheSlot = 0;

            for (int i = 0; i < sanitized.ConsumedPreComputedCacheSlots; i++)
            {
                _preComputedCache[i] = new PreComputedCacheItem()
                {
                    ComputedValue = options.DefaultNullValue,
                    IsVariable = false,
                    IsNullValue = true
                };
            }
        }

        public ExpressionState(ExpressionOptions options, int preAllocation)
        {
            Buffer = new StringBuilder(preAllocation);

            _options = options;
        }

        #region Pre-Parsed Cache Management.

        public int ConsumeNextPlaceholderCacheSlot()
        {
            return _nextPlaceholderCacheSlot++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPlaceholderCache(int slot, [NotNullWhen(true)] out PlaceholderCacheItem value)
        {
            if (slot < _placeholderCache.Length)
            {
                var cached = _placeholderCache[slot];
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
        public void StorePlaceholderCache(int slot, PlaceholderCacheItem value)
        {
            if (slot >= _placeholderCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _placeholderCache, (_placeholderCache.Length + 1) * 2);
            }
            _placeholderCache[slot] = value;
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

        public void HydrateTemplateParsedCache(int expressionHash)
        {
            if (!_isPlaceholderCacheHydrated)
            {
                lock (this)
                {
                    if (!_isPlaceholderCacheHydrated)
                    {
                        if (Utility.PersistentCaches.TryGetValue(expressionHash, out CachedState? entry) && entry != null)
                        {
                            entry.State.HydratePlaceholderCache(_placeholderCache);
                        }
                        _isPlaceholderCacheHydrated = true;
                    }
                }
            }
        }

        private void HydratePlaceholderCache(PlaceholderCacheItem?[] populatedCache)
        {
            Interlocked.Exchange(ref _placeholderCache, populatedCache);
        }

        public void Reset(Sanitized sanitized)
        {
            _nextPreComputedCacheSlot = sanitized.ConsumedPreComputedCacheSlots;
            _nextPlaceholderCacheSlot = 0;
        }

        public ExpressionState Clone()
        {
            var clone = new ExpressionState(_options, WorkingText.Length * 2)
            {
                WorkingText = WorkingText,
                _operationCount = _operationCount,
                _nextPreComputedCacheSlot = _nextPreComputedCacheSlot,
                _preComputedCache = new PreComputedCacheItem[_preComputedCache.Length],
                _placeholderCache = new PlaceholderCacheItem?[_placeholderCache.Length],
                _nextPlaceholderCacheSlot = 0,
                _isPlaceholderCacheHydrated = _isPlaceholderCacheHydrated
            };

            Array.Copy(_preComputedCache, clone._preComputedCache, _preComputedCache.Length); //Copy any pre-computed NULLs.
            Array.Copy(_placeholderCache, clone._placeholderCache, _placeholderCache.Length);

            return clone;
        }

        public void ApplyParameters(Sanitized sanitized, Dictionary<string, double?> definedParameters)
        {
            //Swap out all of the user supplied parameters.
            foreach (var variable in sanitized.DiscoveredVariables.OrderByDescending(o => o.Length))
            {
                if (definedParameters.TryGetValue(variable, out var value))
                {
                    var cacheSlot = ConsumeNextPreComputedCacheSlot(out var cacheKey);
                    _preComputedCache[cacheSlot] = new PreComputedCacheItem()
                    {
                        ComputedValue = value ?? _options.DefaultNullValue,
                        IsVariable = true
                    };
                    WorkingText = WorkingText.Replace(variable, cacheKey);
                }
                else
                {
                    throw new Exception($"Undefined variable: {variable}");
                }
            }
        }
    }
}
