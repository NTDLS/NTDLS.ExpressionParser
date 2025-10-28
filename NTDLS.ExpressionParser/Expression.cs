using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Represents a mathematical expression.
    /// </summary>
    public partial class Expression
    {
        /// <summary>
        /// Delegate for calling a custom function.
        /// </summary>
        public delegate double? ExpressionFunction(double[] parameters);

        private int _nextPreParsedCacheSlot = 0;
        private readonly int _expressionHash;
        private PreComputedCacheItem[] _preComputedCache;
        private readonly string _text = string.Empty;
        private readonly string _precisionFormat;
        private PreParsedCacheItem?[] _preParsedCache;
        private readonly Dictionary<string, double?> _definedParameters = new();
        private readonly StringBuilder _buffer = new();
        private int _nextPreComputedCacheSlot = 0;
        private readonly int _originalNextPreComputedCacheSlot = 0;
        private int _operationCount = 0;

        internal ExpressionOptions Options { get; set; }
        internal string WorkingText { get; set; } = string.Empty;
        internal HashSet<string> DiscoveredVariables { get; private set; } = new();
        internal HashSet<string> DiscoveredFunctions { get; private set; } = new();
        internal Dictionary<string, ExpressionFunction> ExpressionFunctions { get; private set; } = new();

        #region ~/ctor and Sanitize.

        /// <summary>
        /// Represents a mathematical expression.
        /// </summary>
        public Expression(string text, ExpressionOptions? options = null)
        {
            Options = options ?? new ExpressionOptions();
            _precisionFormat = $"G{Options.Precision}";
            _text = Sanitize(text.ToLowerInvariant());
            _originalNextPreComputedCacheSlot = _nextPreComputedCacheSlot;
            _preComputedCache = new PreComputedCacheItem[_operationCount];
            _preParsedCache = new PreParsedCacheItem?[_operationCount];
        }

        internal string Sanitize(string expressionText)
        {
            var result = new StringBuilder();

            int scope = 0;
            int consecutiveMathChars = 0;

            var regex = CompiledRegEx.RegExNullCheck();

            //Find and replace all NULL literals with cache keys.
            //These cache entries contain NULL by default, so no need to set them.
            while (true)
            {
                var match = regex.Match(expressionText);
                if (!match.Success)
                    break;

                _ = ConsumeNextPreComputedCacheSlot(out var cacheKey);
                expressionText = ReplaceRange(expressionText, match.Index, (match.Index + match.Length) - 1, cacheKey);

                _operationCount++;
            }

            var expressionSpan = expressionText.AsSpan();

            for (int i = 0; i < expressionSpan.Length;)
            {
                char c = expressionSpan[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (Utility.IsMathChar(c))
                {
                    _operationCount++;
                    consecutiveMathChars++;

                    // If multiple operator characters appear in a row, that's a malformed expression.
                    if (consecutiveMathChars > 2)
                    {
                        throw new Exception($"Invalid consecutive operators near position {i}: '{c}'");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else
                {
                    // Reset the consecutive operator counter when we hit a number, variable, or parenthesis
                    consecutiveMathChars = 0;
                }

                if (c == ',')
                {
                    if (scope == 0)
                    {
                        throw new Exception("Unexpected comma found in expression.");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == '(')
                {
                    _operationCount++;
                    scope++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == ')')
                {
                    scope--;

                    if (scope < 0)
                    {
                        throw new Exception($"Scope fell below zero while sanitizing input.");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (Utility.IsMathChar(c))
                {
                    _operationCount++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (char.IsDigit(c))
                {
                    _buffer.Clear();

                    for (; i < expressionSpan.Length; i++)
                    {
                        c = expressionSpan[i];

                        if (char.IsDigit(c) || c == '.')
                        {
                            _buffer.Append(c);
                        }
                        else
                        {
                            break;
                        }
                    }

                    var strBuffer = _buffer.ToString();

                    if (Utility.IsNumeric(strBuffer) == false)
                    {
                        throw new Exception($"Value is not a number: {strBuffer}");
                    }

                    result.Append(strBuffer);
                    continue;
                }
                else if (Utility.IsValidVariableChar(c))
                {
                    //Parse the variable/function name and determine which it is. If its a function,
                    //then we want to swap out the opening and closing parenthesizes with curly braces.

                    _buffer.Clear();
                    bool isFunction = false;

                    for (; i < expressionSpan.Length; i++)
                    {
                        c = expressionSpan[i];

                        if (char.IsWhiteSpace(c))
                        {
                            continue;
                        }
                        else if (Utility.IsValidVariableChar(c))
                        {
                            _buffer.Append(c);
                        }
                        else if (c == '(')
                        {
                            isFunction = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    var functionOrVariableName = _buffer.ToString();

                    if (isFunction)
                    {
                        result.Append(functionOrVariableName); //Append the function name to the expression.
                        _operationCount++;
                        DiscoveredFunctions.Add(functionOrVariableName);

                        _buffer.Clear();

                        //If its a function, then lets find the opening and closing parenthesizes and replace them with curly braces.
                        int functionScope = 0;

                        for (; i < expressionSpan.Length; i++)
                        {
                            c = expressionSpan[i];

                            if (char.IsWhiteSpace(c))
                            {
                                continue;
                            }

                            if (c == '(')
                            {
                                functionScope++;
                            }
                            else if (c == ')')
                            {
                                functionScope--;

                                if (functionScope == 0)
                                {
                                    _buffer.Append(c);
                                    i++; //Consume the closing paren.
                                    break;
                                }
                            }
                            else if (c == ',')
                            {
                                _operationCount++;
                            }

                            _buffer.Append(c);
                        }

                        if (functionScope != 0)
                        {
                            throw new Exception($"Parenthesizes mismatch when parsing function scope: {functionOrVariableName}");
                        }

                        var functionParameterString = Sanitize(_buffer.ToString());

                        if (functionParameterString.StartsWith('(') == false || functionParameterString.EndsWith(')') == false)
                        {
                            throw new Exception($"The function scope should be enclosed in parenthesizes.");
                        }

                        result.AppendFormat("{{{0}}}", functionParameterString[1..^1]);
                    }
                    else
                    {
                        result.Append(functionOrVariableName); //Append the function name to the expression.

                        _operationCount++;
                        DiscoveredVariables.Add(functionOrVariableName);
                    }
                }
                else if (c == '$')
                {
                    _operationCount++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else
                {
                    throw new Exception($"Unhandled character {c}");
                }
            }

            if (scope != 0)
            {
                throw new Exception($"Scope mismatch while sanitizing input.");
            }

            if (_operationCount == 0)
            {
                _operationCount++;
            }

            return result.ToString();
        }

        #endregion

        #region Pre-Parsed Cache Management.

        internal int ConsumeNextPreParsedCacheSlot()
        {
            return _nextPreParsedCacheSlot++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetPreParsedCache(int slot, [NotNullWhen(true)] out PreParsedCacheItem value)
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
        internal void StorePreParsedCache(int slot, PreParsedCacheItem value)
        {
            if (slot >= _preParsedCache.Length) //Resize the cache if needed.
            {
                Array.Resize(ref _preParsedCache, (_preParsedCache.Length + 1) * 2);
            }
            _preParsedCache[slot] = value;
        }

        #endregion

        #region Pre-Computed Cache Management.

        private int ConsumeNextPreComputedCacheSlot(out string cacheKey)
        {
            int cacheSlot = _nextPreComputedCacheSlot++;
            cacheKey = $"${cacheSlot}$";
            return cacheSlot;
        }

        internal string StorePreComputedCacheItem(double? value, bool isVariable = false)
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

        internal PreComputedCacheItem GetPreComputedCacheItem(ReadOnlySpan<char> span)
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

        #region Evaluate.

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        public double? Evaluate()
        {
            ResetState();

            bool isComplete;
            do
            {
                //Get a sub-expression from the whole expression.
                isComplete = AcquireSubexpression(out int startIndex, out int endIndex, out var subExpression);
                //Compute the sub-expression.
                var resultString = subExpression.Compute();
                //Replace the sub-expression in the whole expression with the result from the sub-expression computation.
                WorkingText = ReplaceRange(WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            if (WorkingText[0] == '$')
            {
                return GetPreComputedCacheItem(WorkingText.AsSpan()[1..^1]).ComputedValue;
            }

            return StringToDouble(WorkingText);
        }

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <returns></returns>
        public double? Evaluate(out string showWork)
        {
            ResetState();

            var work = new StringBuilder();

            work.AppendLine("{");

            bool isComplete;
            do
            {
                //Get a sub-expression from the whole expression.
                isComplete = AcquireSubexpression(out int startIndex, out int endIndex, out var subExpression);

                string friendlySubExpression = SwapInCacheValues(subExpression.Text);
                work.Append("    " + friendlySubExpression);

                //Compute the sub-expression.
                var resultString = subExpression.Compute();

                work.AppendLine($" = {SwapInCacheValues(resultString)}");

                //Replace the sub-expression in the whole expression with the result from the sub-expression computation.
                WorkingText = ReplaceRange(WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            work.AppendLine($"}} = {SwapInCacheValues(WorkingText)}");

            if (WorkingText[0] == '$')
            {
                showWork = work.ToString();
                return GetPreComputedCacheItem(WorkingText.AsSpan()[1..^1]).ComputedValue;
            }

            showWork = work.ToString();
            return StringToDouble(WorkingText);
        }

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double? Evaluate(string expression, out string showWork, ExpressionOptions? options = null)
            => new Expression(expression, options).Evaluate(out showWork);

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double? Evaluate(string expression, ExpressionOptions? options = null)
            => new Expression(expression, options).Evaluate();

        #endregion

        #region Evaluate Not Null.

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <param name="outResultWasNull">Is true when the result was NULL.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double EvaluateNotNull(string expression, out string showWork, out bool outResultWasNull, ExpressionOptions? options = null)
        {
            var result = new Expression(expression).Evaluate(out showWork);
            outResultWasNull = result == null;
            return result ?? 0;
        }

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="outResultWasNull">Is true when the result was NULL.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double EvaluateNotNull(string expression, out bool outResultWasNull, ExpressionOptions? options = null)
        {
            var result = new Expression(expression).Evaluate();
            outResultWasNull = result == null;
            return result ?? 0;
        }

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double EvaluateNotNull(string expression, ExpressionOptions? options = null)
             => new Expression(expression).Evaluate() ?? 0;

        #endregion

        #region Set/Get/Clear Parameters.

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, double? value) => _definedParameters[name.ToLowerInvariant()] = value;

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, int? value) => _definedParameters[name.ToLowerInvariant()] = value;

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, bool? value) => _definedParameters[name.ToLowerInvariant()] = value == null ? null : value == true ? 1 : 0;

        /// <summary>
        /// Removed a parameter from the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        public void RemoveParameter(string name) => _definedParameters.Remove(name.ToLowerInvariant());

        /// <summary>
        /// Removes all parameters which have been previously added to the expression.
        /// </summary>
        public void ClearParameters() => _definedParameters.Clear();

        #endregion

        #region Add/Get/Clear Parameters.

        /// <summary>
        /// Adds a function to the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the function as found in the string mathematical expression.</param>
        /// <param name="function">Delegate of the function.</param>
        public void AddFunction(string name, ExpressionFunction function)
            => ExpressionFunctions.Add(name.ToLowerInvariant(), function);

        /// <summary>
        /// Removes a function from the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the function as found in the string mathematical expression.</param>
        public void RemoveFunction(string name)
            => ExpressionFunctions.Remove(name.ToLowerInvariant());

        /// <summary>
        /// Removes all functions which have been previously added to the expression.
        /// </summary>
        public void ClearFunctions() => ExpressionFunctions.Clear();

        #endregion

        /// <summary>
        /// Replaces placeholders in the input text with their corresponding precomputed cache values.
        /// This function is only used when showing work, so performance is not critical.
        /// </summary>
        private string SwapInCacheValues(string text)
        {
            var copy = new string(text);

            while (true)
            {
                int begIndex = copy.IndexOf('$');
                int endIndex = copy.IndexOf('$', begIndex + 1);

                if (begIndex >= 0 && endIndex > begIndex)
                {
                    var cacheKey = copy.Substring(begIndex + 1, (endIndex - begIndex) - 1);
                    copy = copy.Replace($"${cacheKey}$", GetPreComputedCacheItem(cacheKey).ComputedValue?.ToString(_precisionFormat) ?? "null");
                }
                else
                {
                    break;
                }
            }

            return copy;
        }

        internal void ResetState()
        {
            _nextPreParsedCacheSlot = 0;
            WorkingText = _text; //Start with a pre-sanitized/validated copy of the supplied expression text.

            _nextPreComputedCacheSlot = _originalNextPreComputedCacheSlot; //To account for the NULL replacements during sanitization.

            for (int i = 0; i < _originalNextPreComputedCacheSlot; i++)
            {
                _preComputedCache[i] = new PreComputedCacheItem()
                {
                    ComputedValue = Options.DefaultNullValue,
                    IsVariable = false,
                    IsNullValue = true
                };
            }

            //Swap out all of the user supplied parameters.
            foreach (var variable in DiscoveredVariables.OrderByDescending(o => o.Length))
            {
                if (_definedParameters.TryGetValue(variable, out var value))
                {
                    var cacheSlot = ConsumeNextPreComputedCacheSlot(out var cacheKey);
                    _preComputedCache[cacheSlot] = new PreComputedCacheItem()
                    {
                        ComputedValue = value ?? Options.DefaultNullValue,
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

        internal string ReplaceRange(string original, int startIndex, int endIndex, string replacement)
        {
            _buffer.Clear();
            _buffer.Append(original.AsSpan(0, startIndex));
            _buffer.Append(replacement);
            _buffer.Append(original.AsSpan(endIndex + 1));
            return _buffer.ToString();
        }

        /// <summary>
        /// Gets a sub-expression from WorkingText and replaces it with a token.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AcquireSubexpression(out int outStartIndex, out int outEndIndex, out SubExpression outSubExpression)
        {
            int lastParenIndex = WorkingText.LastIndexOf('(');

            if (lastParenIndex >= 0)
            {
                outStartIndex = lastParenIndex;

                int scope = 0;
                int i = lastParenIndex;

                for (; i < WorkingText.Length; i++)
                {
                    char c = WorkingText[i];

                    //if (char.IsWhiteSpace(c)) //Sanitization step should have already removed whitespace.
                    //    continue;

                    if (c == '(')
                    {
                        scope++;
                    }
                    else if (c == ')')
                    {
                        scope--;
                        if (scope == 0)
                            break;
                    }
                }

                if (scope != 0)
                    throw new Exception("Parentheses mismatch when parsing subexpression.");

                outEndIndex = i;

                var subExprSpan = WorkingText.AsSpan(outStartIndex, outEndIndex - outStartIndex + 1);

                if (subExprSpan[0] != '(' || subExprSpan[^1] != ')')
                    throw new Exception("Sub-expression should be enclosed in parentheses.");

                outSubExpression = new SubExpression(this, subExprSpan.ToString());
                return false;
            }
            else
            {
                outStartIndex = 0;
                outEndIndex = WorkingText.Length - 1;
                outSubExpression = new SubExpression(this, WorkingText);
                return true;
            }
        }

        /// <summary>
        /// Converts a string or value of a stored cache key into a double.
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        internal double? StringToDouble(ReadOnlySpan<char> span)
        {
            if (span.Length == 0)
            {
                return null;
            }
            else if (span[0] == '$')
            {
                return GetPreComputedCacheItem(span[1..^1]).ComputedValue;
            }

            if (Options.UseFastFloatingPointParser)
            {
                double result = 0.0;
                int length = span.Length;
                int i = 0;
                double fraction;
                double multiplier;
                bool isNegative = false;

                if (length > 0 && (span[0] == '-' || span[0] == '+'))
                {
                    isNegative = span[0] == '-';
                    i++; //Skip the explicit sign.
                }

                for (; i < length; i++)
                {
                    if ((span[i] - '0') >= 0 && (span[i] - '0') <= 9)
                    {
                        result = result * 10.0 + (span[i] - '0');
                    }
                    else if (span[i] == '.')
                    {
                        i++; //Skip the decimal point.

                        fraction = 0.0;
                        multiplier = 1.0;

                        for (; i < length; i++)
                        {
                            if ((span[i] - '0') >= 0 && (span[i] - '0') <= 9)
                            {
                                fraction = fraction * 10.0 + (span[i] - '0');
                                multiplier *= 0.1;
                            }
                            else throw new FormatException("Invalid character in input string.");
                        }

                        result += fraction * multiplier;
                    }
                    else throw new FormatException("Invalid character in input string.");
                }

                return isNegative ? -result : result;
            }

            return double.Parse(span, CultureInfo.InvariantCulture);
        }
    }
}
