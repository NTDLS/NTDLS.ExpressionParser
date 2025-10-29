using Microsoft.Extensions.Caching.Memory;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class ExpressionState
    {
        public VisitorCache<ScanStepItem> ScanStepCache;
        public VisitorCache<ComputedStepItem> ComputedStepCache;
        public VisitorCache<OperationStepItem> OperationStepCache;

        public string WorkingText { get; set; } = string.Empty;
        public readonly StringBuilder Buffer;

        private bool _isTemplateCacheHydrated = false;

        private PlaceholderCacheItem[] _placeholderCache = [];
        private int _nextPlaceholderCacheSlot = 0;
        private int _operationCount = 0;
        private readonly ExpressionOptions _options;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            _options = options;
            Buffer = new StringBuilder();

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            _placeholderCache = new PlaceholderCacheItem[sanitized.OperationCount];

            ScanStepCache = new(_operationCount * 3);
            ComputedStepCache = new(_operationCount * 3);
            OperationStepCache = new(_operationCount);

            for (int i = 0; i < sanitized.ConsumedPlaceholderCacheSlots; i++)
            {
                _placeholderCache[i] = new PlaceholderCacheItem()
                {
                    ComputedValue = options.DefaultNullValue,
                    IsUserVariableDerived = false,
                    IsNullValue = true
                };
            }
        }

        public ExpressionState(ExpressionOptions options, int operationCount, int preAllocation)
        {
            Buffer = new StringBuilder(preAllocation);
            ScanStepCache = new(operationCount * 3);
            ComputedStepCache = new(operationCount * 3);
            OperationStepCache = new(operationCount);
            _options = options;
        }

        #region Placeholder Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ConsumeNextPlaceholderCacheSlot(out string cacheKey)
        {
            int cacheSlot = _nextPlaceholderCacheSlot++;
            cacheKey = $"${cacheSlot}$";
            return cacheSlot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string StorePlaceholderCacheItem(double? value, bool isUserVariableDerived = false)
        {
            var cacheSlot = ConsumeNextPlaceholderCacheSlot(out var cacheKey);

            if (cacheSlot >= _placeholderCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _placeholderCache, (_placeholderCache.Length + 1) * 2);
            }

            _placeholderCache[cacheSlot] = new PlaceholderCacheItem()
            {
                IsUserVariableDerived = isUserVariableDerived,
                ComputedValue = value
            };

            return cacheKey;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        public PlaceholderCacheItem GetPlaceholderCacheItem(ReadOnlySpan<char> span)
        {
            switch (span.Length)
            {
                case 1:
                    return _placeholderCache[span[0] - '0'];
                default:
                    int index = 0;
                    for (int i = 0; i < span.Length; i++)
                        index = index * 10 + (span[i] - '0');
                    return _placeholderCache[index];
            }
        }

        #endregion

        public void HydrateTemplateCache(int expressionHash)
        {
            if (!_isTemplateCacheHydrated)
            {
                lock (this)
                {
                    if (!_isTemplateCacheHydrated)
                    {
                        if (Utility.PersistentCaches.TryGetValue(expressionHash, out CachedState? entry) && entry != null)
                        {
                            ComputedStepCache.CopyTo(entry.State.ComputedStepCache);
                            ScanStepCache.CopyTo(entry.State.ScanStepCache);
                            OperationStepCache.CopyTo(entry.State.OperationStepCache);
                        }
                        _isTemplateCacheHydrated = true;
                    }
                }
            }
        }

        public void Reset(Sanitized sanitized)
        {
            WorkingText = sanitized.Text;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            ComputedStepCache.Reset();
            ScanStepCache.Reset();
            OperationStepCache.Reset();
        }

        public ExpressionState Clone(Sanitized sanitized)
        {
            var clone = new ExpressionState(_options, sanitized.OperationCount, WorkingText.Length * 2)
            {
                WorkingText = WorkingText,
                _operationCount = _operationCount,
                _nextPlaceholderCacheSlot = _nextPlaceholderCacheSlot,
                _placeholderCache = new PlaceholderCacheItem[_placeholderCache.Length],
                _isTemplateCacheHydrated = _isTemplateCacheHydrated
            };

            ComputedStepCache.CopyTo(clone.ComputedStepCache);
            ScanStepCache.CopyTo(clone.ScanStepCache);
            OperationStepCache.CopyTo(clone.OperationStepCache);

            for (int i = 0; i < sanitized.ConsumedPlaceholderCacheSlots; i++)
            {
                //We typically do not keep placeholders, but for hard-coded NULLs in the expression we do, because they are supplied by the user.
                clone._placeholderCache[i] = _placeholderCache[i]; //Copy any pre-defined NULLs.
            }

            return clone;
        }

        public void ApplyParameters(Sanitized sanitized, Dictionary<string, double?> definedParameters)
        {
            //Swap out all of the user supplied parameters.
            foreach (var variable in sanitized.DiscoveredVariables.OrderByDescending(o => o.Length))
            {
                if (definedParameters.TryGetValue(variable, out var value))
                {
                    var cacheSlot = ConsumeNextPlaceholderCacheSlot(out var cacheKey);
                    _placeholderCache[cacheSlot] = new PlaceholderCacheItem()
                    {
                        ComputedValue = value ?? _options.DefaultNullValue,
                        IsUserVariableDerived = true
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
