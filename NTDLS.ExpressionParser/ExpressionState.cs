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

        //private int _nextPreParsedCacheSlot = 0;
        //private PreParsedCacheItem[] _preParsedCache = [];

        private int _nextParsedResultCacheSlot = 0;
        private PreComputedCacheItem[] _preComputedCache = [];
        private int _nextPreComputedCacheSlot = 0;
        private int _operationCount = 0;
        private ParsedResultCacheItem[] _parsedResultCache = [];
        private readonly ExpressionOptions _options;
        private bool _isParsedResultCacheHydrated = false;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            _options = options;
            Buffer = new StringBuilder();

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPreComputedCacheSlot = sanitized.ConsumedPreComputedCacheSlots;
            _preComputedCache = new PreComputedCacheItem[_operationCount + 10];
            _parsedResultCache = new ParsedResultCacheItem[_operationCount + 10];
            _nextParsedResultCacheSlot = 0;

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

        #region Parsed Result Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetParsedResultCache([NotNullWhen(true)] out ParsedResultCacheItem value, out int slot)
        {
            slot = _nextParsedResultCacheSlot++;

            if (slot < _nextParsedResultCacheSlot - 1)
            {
                value = _parsedResultCache[slot];
                return true;
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StoreParsedResultCache(int slot, ParsedResultCacheItem value)
        {
            if (slot >= _parsedResultCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _parsedResultCache, (_parsedResultCache.Length + 1) * 2);
            }
            _parsedResultCache[slot] = value;
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
            if (!_isParsedResultCacheHydrated)
            {
                lock (this)
                {
                    if (!_isParsedResultCacheHydrated)
                    {
                        if (Utility.PersistentCaches.TryGetValue(expressionHash, out CachedState? entry) && entry != null)
                        {
                            entry.State.HydrateParsedResultCache(_parsedResultCache);
                        }
                        _isParsedResultCacheHydrated = true;
                    }
                }
            }
        }

        private void HydrateParsedResultCache(ParsedResultCacheItem[] populatedCache)
        {
            Interlocked.Exchange(ref _parsedResultCache, populatedCache);
        }

        public void Reset(Sanitized sanitized)
        {
            WorkingText = sanitized.Text;
            _nextPreComputedCacheSlot = sanitized.ConsumedPreComputedCacheSlots;
            _nextParsedResultCacheSlot = 0;
        }

        public ExpressionState Clone()
        {
            var clone = new ExpressionState(_options, WorkingText.Length * 2)
            {
                WorkingText = WorkingText,
                _operationCount = _operationCount,
                _nextPreComputedCacheSlot = _nextPreComputedCacheSlot,
                _preComputedCache = new PreComputedCacheItem[_preComputedCache.Length],
                _parsedResultCache = new ParsedResultCacheItem[_parsedResultCache.Length],
                _nextParsedResultCacheSlot = 0,
                _isParsedResultCacheHydrated = _isParsedResultCacheHydrated
            };

            Array.Copy(_preComputedCache, clone._preComputedCache, _preComputedCache.Length); //Copy any pre-computed NULLs.
            Array.Copy(_parsedResultCache, clone._parsedResultCache, _parsedResultCache.Length);

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
