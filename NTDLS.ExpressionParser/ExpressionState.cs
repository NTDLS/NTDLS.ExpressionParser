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

        private int _nextPositionStepCacheSlot = 0;
        private PlaceholderCacheItem[] _placeholderCache = [];
        private int _nextPlaceholderCacheSlot = 0;
        private int _operationCount = 0;
        private PositionStepCacheItem[] _positionStepCache = [];
        private readonly ExpressionOptions _options;
        private bool _isPositionStepCacheHydrated = false;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            _options = options;
            Buffer = new StringBuilder();

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            _placeholderCache = new PlaceholderCacheItem[_operationCount + 10];
            _positionStepCache = new PositionStepCacheItem[_operationCount + 10];
            _nextPositionStepCacheSlot = 0;

            for (int i = 0; i < sanitized.ConsumedPlaceholderCacheSlots; i++)
            {
                _placeholderCache[i] = new PlaceholderCacheItem()
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

        #region Position Step Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPositionStepCache([NotNullWhen(true)] out PositionStepCacheItem value, out int slot)
        {
            slot = _nextPositionStepCacheSlot++;

            if (slot < _nextPositionStepCacheSlot - 1)
            {
                value = _positionStepCache[slot];
                return true;
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StorePositionStepCache(int slot, PositionStepCacheItem value)
        {
            if (slot >= _positionStepCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _positionStepCache, (_positionStepCache.Length + 1) * 2);
            }
            _positionStepCache[slot] = value;
        }

        #endregion

        #region Placeholder Cache Management.

        public int ConsumeNextPlaceholderCacheSlot(out string cacheKey)
        {
            int cacheSlot = _nextPlaceholderCacheSlot++;
            cacheKey = $"${cacheSlot}$";
            return cacheSlot;
        }

        public string StorePlaceholderCacheItem(double? value, bool isVariable = false)
        {
            var cacheSlot = ConsumeNextPlaceholderCacheSlot(out var cacheKey);

            if (cacheSlot >= _placeholderCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _placeholderCache, (_placeholderCache.Length + 1) * 2);
            }

            _placeholderCache[cacheSlot] = new PlaceholderCacheItem()
            {
                IsVariable = isVariable,
                ComputedValue = value
            };

            return cacheKey;
        }

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

        /*
        public void HydrateTemplateParsedCache(int expressionHash)
        {
            if (!_isPositionStepCacheHydrated)
            {
                lock (this)
                {
                    if (!_isPositionStepCacheHydrated)
                    {
                        if (Utility.PersistentCaches.TryGetValue(expressionHash, out CachedState? entry) && entry != null)
                        {
                            entry.State.HydratePositionStepCache(_positionStepCache);
                        }
                        _isPositionStepCacheHydrated = true;
                    }
                }
            }
        }

        private void HydratePositionStepCache(PositionStepCacheItem[] populatedCache)
        {
            Interlocked.Exchange(ref _positionStepCache, populatedCache);
        }
        */

        public void Reset(Sanitized sanitized)
        {
            WorkingText = sanitized.Text;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            _nextPositionStepCacheSlot = 0;
        }

        public ExpressionState Clone()
        {
            var clone = new ExpressionState(_options, WorkingText.Length * 2)
            {
                WorkingText = WorkingText,
                _operationCount = _operationCount,
                _nextPlaceholderCacheSlot = _nextPlaceholderCacheSlot,
                _placeholderCache = new PlaceholderCacheItem[_placeholderCache.Length],
                _positionStepCache = new PositionStepCacheItem[_positionStepCache.Length],
                _nextPositionStepCacheSlot = 0,
                _isPositionStepCacheHydrated = _isPositionStepCacheHydrated
            };

            Array.Copy(_placeholderCache, clone._placeholderCache, _placeholderCache.Length); //Copy any pre-computed NULLs.
            Array.Copy(_positionStepCache, clone._positionStepCache, _positionStepCache.Length);

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
