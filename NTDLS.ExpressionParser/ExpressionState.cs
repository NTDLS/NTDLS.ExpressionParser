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
        private StringBuilder? _buffer;

        /// <summary>
        /// Scratch buffer, created on first use since expressions without variables never need it.
        /// </summary>
        public StringBuilder Buffer => _buffer ??= new StringBuilder();

        private bool _isTemplateCacheHydrated = false;

        private PlaceholderCacheItem[] _placeholderCache = [];
        private int _nextPlaceholderCacheSlot = 0;
        private int _operationCount = 0;
        private readonly ExpressionOptions _options;

        public ExpressionState(Sanitized sanitized, ExpressionOptions options)
        {
            _options = options;

            WorkingText = sanitized.Text;
            _operationCount = sanitized.OperationCount;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            _placeholderCache = new PlaceholderCacheItem[sanitized.OperationCount];

            ScanStepCache = new(_operationCount);
            ComputedStepCache = new(_operationCount);
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

        /// <summary>
        /// Creates an empty state for Clone() to populate.
        /// </summary>
        private ExpressionState(ExpressionOptions options)
        {
            _options = options;
        }

        #region Placeholder Cache Management.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ConsumeNextPlaceholderCacheSlot(out string cacheKey)
        {
            int cacheSlot = _nextPlaceholderCacheSlot++;
            cacheKey = Utility.PlaceholderKey(cacheSlot);

            if (cacheSlot >= _placeholderCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _placeholderCache, (_placeholderCache.Length + 1) * 2);
            }

            return cacheSlot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string StorePlaceholderCacheItem(double? value, bool isUserVariableDerived = false)
        {
            var cacheSlot = ConsumeNextPlaceholderCacheSlot(out var cacheKey);

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
            int index = 0;
            for (int i = 0; i < span.Length; i++)
            {
                int digit = span[i] - '0';
                if ((uint)digit > 9)
                    throw new Exception($"Invalid placeholder key: '{span.ToString()}'. Expected a numeric cache index.");
                index = index * 10 + digit;
            }
            return _placeholderCache[index];
        }

        #endregion

        /// <summary>
        /// Copies the step caches from this (evaluated) state into the shared template state so that
        /// subsequent Expression instances of the same text start out warm. Only the first evaluation of
        /// a template does this, after which the template is marked as hydrated and the copy is skipped.
        /// </summary>
        public void HydrateTemplateCache(object cacheKey)
        {
            if (_isTemplateCacheHydrated)
                return;

            if (Utility.PersistentCaches.TryGetValue(cacheKey, out CachedState? entry) && entry != null)
            {
                lock (entry.State)
                {
                    if (!entry.State._isTemplateCacheHydrated)
                    {
                        entry.State.ComputedStepCache.CopyFrom(ComputedStepCache);
                        entry.State.ScanStepCache.CopyFrom(ScanStepCache);
                        entry.State.OperationStepCache.CopyFrom(OperationStepCache);
                        entry.State._isTemplateCacheHydrated = true;
                    }
                }
            }
            _isTemplateCacheHydrated = true;
        }

        public void Reset(Sanitized sanitized)
        {
            WorkingText = sanitized.Text;
            _nextPlaceholderCacheSlot = sanitized.ConsumedPlaceholderCacheSlots;
            ComputedStepCache.Reset();
            ScanStepCache.Reset();
            OperationStepCache.Reset();
        }

        /// <summary>
        /// Creates a per-instance state from this shared template state. The step caches are shared copy-on-write
        /// (the template only ever replaces its arrays, under this lock, and never writes into them), so a clone of
        /// a hydrated template allocates nothing for them unless its evaluation diverges.
        /// </summary>
        public ExpressionState Clone(Sanitized sanitized)
        {
            lock (this)
            {
                var clone = new ExpressionState(_options)
                {
                    WorkingText = WorkingText,
                    _operationCount = _operationCount,
                    _nextPlaceholderCacheSlot = _nextPlaceholderCacheSlot,
                    _placeholderCache = new PlaceholderCacheItem[_placeholderCache.Length],
                    _isTemplateCacheHydrated = _isTemplateCacheHydrated
                };

                clone.ComputedStepCache.ShareFrom(ComputedStepCache);
                clone.ScanStepCache.ShareFrom(ScanStepCache);
                clone.OperationStepCache.ShareFrom(OperationStepCache);

                //We typically do not keep placeholders, but for hard-coded NULLs in the expression we do, because they are supplied by the user.
                _placeholderCache.AsSpan(0, sanitized.ConsumedPlaceholderCacheSlots).CopyTo(clone._placeholderCache); //Copy any pre-defined NULLs.

                return clone;
            }
        }

        /// <summary>
        /// Replaces each user variable in the working text with a placeholder holding its value.
        /// Only whole identifiers are replaced, so a variable is never substituted inside a function name,
        /// another variable name, or a number.
        /// </summary>
        public void ApplyParameters(Sanitized sanitized, Dictionary<string, double?>? definedParameters)
        {
            var variables = sanitized.Variables;
            if (variables.Length == 0)
                return;

            foreach (var variable in variables)
            {
                if (definedParameters == null || !definedParameters.ContainsKey(variable))
                    throw new Exception($"Undefined variable: {variable}");
            }

            //Placeholder slot assigned to each variable, allocated on first occurrence.
            Span<int> variableSlots = variables.Length <= 64 ? stackalloc int[variables.Length] : new int[variables.Length];
            variableSlots.Fill(-1);

            var text = WorkingText.AsSpan();
            Buffer.Clear();

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int start = i;

                if (char.IsAsciiDigit(c) || c == '.')
                {
                    //Numbers (and placeholder indexes) are copied verbatim - this mirrors how the sanitizer tokenizes.
                    while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                        i++;
                    Buffer.Append(text[start..i]);
                }
                else if (Utility.IsValidVariableChar(c))
                {
                    while (i < text.Length && Utility.IsValidVariableChar(text[i]))
                        i++;

                    var identifier = text[start..i];
                    int variableIndex = -1;

                    if (i >= text.Length || text[i] != '{') //Identifiers followed by '{' are function names.
                    {
                        for (int v = 0; v < variables.Length; v++)
                        {
                            if (identifier.SequenceEqual(variables[v]))
                            {
                                variableIndex = v;
                                break;
                            }
                        }
                    }

                    if (variableIndex < 0)
                    {
                        Buffer.Append(identifier);
                        continue;
                    }

                    if (variableSlots[variableIndex] < 0)
                    {
                        var cacheSlot = ConsumeNextPlaceholderCacheSlot(out _);
                        _placeholderCache[cacheSlot] = new PlaceholderCacheItem()
                        {
                            ComputedValue = definedParameters![variables[variableIndex]] ?? _options.DefaultNullValue,
                            IsUserVariableDerived = true
                        };
                        variableSlots[variableIndex] = cacheSlot;
                    }

                    Buffer.Append(Utility.PlaceholderKey(variableSlots[variableIndex]));
                }
                else
                {
                    Buffer.Append(c);
                    i++;
                }
            }

            WorkingText = Buffer.ToString();
        }
    }
}
