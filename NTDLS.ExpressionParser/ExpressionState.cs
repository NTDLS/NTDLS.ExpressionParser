using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class ExpressionState
    {
        public string WorkingText { get; set; } = string.Empty;
        public readonly StringBuilder Buffer;

        private int _nextComputedStepSlot = 0;
        private int _consumedComputedStepSlots = 0;
        private ComputedStepItem[] _computedStep = [];

        private int _nextScanStepSlot = 0;
        private int _consumedScanStepSlots = 0;
        private ScanStepItem[] _scanStep = [];

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
            _computedStep = new ComputedStepItem[_operationCount];
            _nextComputedStepSlot = 0;

            _scanStep = new ScanStepItem[_operationCount * 3];
            _nextScanStepSlot = 0;

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

        public ExpressionState(ExpressionOptions options, int preAllocation)
        {
            Buffer = new StringBuilder(preAllocation);

            _options = options;
        }

        #region Scan Step Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetScanStep([NotNullWhen(true)] out ScanStepItem value)
        {
            if (_nextScanStepSlot < _consumedScanStepSlots)
            {
                value = _scanStep[_nextScanStepSlot];
                _nextScanStepSlot++;
                return value.IsValid;
            }
            _nextScanStepSlot++;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StoreScanStep(ScanStepItem value)
        {
            if (_consumedScanStepSlots >= _scanStep.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _scanStep, (_scanStep.Length + 1) * 2);
            }
            _scanStep[_consumedScanStepSlots++] = value;
        }

        #endregion

        #region Computed Step Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetComputedStep([NotNullWhen(true)] out ComputedStepItem value)
        {
            if (_nextComputedStepSlot < _consumedComputedStepSlots)
            {
                value = _computedStep[_nextComputedStepSlot];
                _nextComputedStepSlot++;
                return value.IsValid;
            }
            _nextComputedStepSlot++;
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StoreComputedStep(ComputedStepItem value)
        {
            if (_consumedComputedStepSlots >= _computedStep.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _computedStep, (_computedStep.Length + 1) * 2);
            }
            value.IsValid = true;
            _computedStep[_consumedComputedStepSlots++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IncrementComputedStep()
        {
            if (_consumedComputedStepSlots >= _computedStep.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _computedStep, (_computedStep.Length + 1) * 2);
            }
            _computedStep[_consumedComputedStepSlots++] = new ComputedStepItem() { IsValid = false };
        }

        #endregion

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
                            entry.State.HydrateComputedStepCache(_computedStep, _consumedComputedStepSlots);
                            entry.State.HydrateScanStepCache(_scanStep, _consumedScanStepSlots);
                        }
                        _isTemplateCacheHydrated = true;
                    }
                }
            }
        }

        private void HydrateScanStepCache(ScanStepItem[] populatedCache, int consumedScanStepSlots)
        {
            _consumedScanStepSlots = consumedScanStepSlots;
            Interlocked.Exchange(ref _scanStep, populatedCache);
        }

        private void HydrateComputedStepCache(ComputedStepItem[] populatedCache, int consumedComputedStepSlots)
        {
            _consumedComputedStepSlots = consumedComputedStepSlots;
            Interlocked.Exchange(ref _computedStep, populatedCache);
        }

        public void Reset(Sanitized sanitized)
        {
            WorkingText = sanitized.Text;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            _nextComputedStepSlot = 0;
            _nextScanStepSlot = 0;
        }

        public ExpressionState Clone(Sanitized sanitized)
        {
            var clone = new ExpressionState(_options, WorkingText.Length * 2)
            {
                WorkingText = WorkingText,
                _operationCount = _operationCount,
                _nextPlaceholderCacheSlot = _nextPlaceholderCacheSlot,
                _placeholderCache = new PlaceholderCacheItem[_placeholderCache.Length],
                _isTemplateCacheHydrated = _isTemplateCacheHydrated,

                _computedStep = new ComputedStepItem[_computedStep.Length],
                _nextComputedStepSlot = 0,
                _consumedComputedStepSlots = _consumedComputedStepSlots,

                _scanStep = new ScanStepItem[_scanStep.Length],
                _nextScanStepSlot = 0,
                _consumedScanStepSlots = _consumedScanStepSlots

            };

            for (int i = 0; i < clone._consumedComputedStepSlots; i++)
            {
                clone._computedStep[i] = _computedStep[i];
            }
            for (int i = 0; i < clone._consumedScanStepSlots; i++)
            {
                clone._scanStep[i] = _scanStep[i];
            }
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
