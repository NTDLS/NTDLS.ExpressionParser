using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Represents a mathematical expression.
    /// </summary>
    public class Expression
    {
        /// <summary>
        /// Delegate for calling a custom function.
        /// </summary>
        public delegate double CustomFunction(double[] parameters);

        private volatile PreParsedCacheItem?[] _preParsedCache;
        private readonly Dictionary<string, double> _definedParameters = new();
        private readonly StringBuilder _replaceRangeBuilder = new();
        private int _nextPreComputedCacheKey = 0;
        private int _operationCount = 0;

        /// <summary>
        /// Gets or sets the number of significant digits used in calculations.
        /// </summary>
        public int Precision { get; set; } = 17;
        internal int NextPreParsedCacheKey { get; set; } = 0;
        internal ulong ExpressionHash { get; private set; }
        internal string PrecisionFormat { get; set; } = "N17";
        internal string Text { get; private set; } = string.Empty;
        internal string WorkingText { get; set; } = string.Empty;
        internal HashSet<string> DiscoveredVariables { get; private set; } = new();
        internal HashSet<string> DiscoveredFunctions { get; private set; } = new();
        internal Dictionary<string, CustomFunction> CustomFunctions { get; private set; } = new();
        internal double[] PreComputedCache { get; private set; }

        internal string ConsumeNextPreComputedCacheKey(out int cacheIndex)
        {
            cacheIndex = _nextPreComputedCacheKey++;
            return $"${cacheIndex}$";
        }

        internal double CachedValue(ReadOnlySpan<char> span)
        {
            switch (span.Length)
            {
                case 1:
                    return PreComputedCache[span[0] - '0'];
                default:
                    int index = 0;
                    for (int i = 0; i < span.Length; i++)
                        index = index * 10 + (span[i] - '0');
                    return PreComputedCache[index];
            }
        }

        /// <summary>
        /// Represents a mathematical expression.
        /// </summary>
        public Expression(string text)
        {
            PrecisionFormat = $"N{Precision}";
            Text = Sanitize(text.ToLower());

            ExpressionHash = HashCombine(
                XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(Text)),
                (ulong)Precision
            );

            _preParsedCache = Utility.PersistentCaches.GetOrCreate(ExpressionHash, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                return new PreParsedCacheItem?[_operationCount];
            }) ?? throw new Exception("Failed to create persistent cache.");

            PreComputedCache = new double[_operationCount];
        }

        /// <summary>
        /// Represents a mathematical expression.
        /// </summary>
        public Expression(string text, int precision)
        {
            Precision = precision;
            PrecisionFormat = $"N{Precision}";
            Text = Sanitize(text.ToLower());

            ExpressionHash = HashCombine(
                XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(Text)),
                (ulong)Precision
            );

            _preParsedCache = Utility.PersistentCaches.GetOrCreate(ExpressionHash, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                return new PreParsedCacheItem?[_operationCount];
            }) ?? throw new Exception("Failed to create persistent cache.");

            PreComputedCache = new double[_operationCount];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong HashCombine(ulong a, ulong b)
        {
            a ^= b + 0x9e3779b97f4a7c15UL + (a << 6) + (a >> 2); //high entropy spread.
            return a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetPreParsed(int slot, [NotNullWhen(true)] out PreParsedCacheItem value)
        {
            var local = _preParsedCache;
            if ((uint)slot < (uint)local.Length)
            {
                var cached = local[slot];
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
        internal void SetPreParsed(int slot, in PreParsedCacheItem value)
        {
            var local = _preParsedCache;
            if ((uint)slot >= (uint)local.Length)
            {
                Resize(slot + 1);
                local = _preParsedCache;
            }
            local[slot] = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Resize(int requiredSize)
        {
            var current = _preParsedCache;
            int newSize = current.Length;
            while (newSize <= requiredSize)
                newSize <<= 1; // grow exponentially

            var newArray = new PreParsedCacheItem?[newSize];
            Array.Copy(current, newArray, current.Length);

            // Atomic publish, readers always see a valid array
            Interlocked.Exchange(ref _preParsedCache, newArray);
        }

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        public static double Evaluate(string expression, out string showWork)
            => new Expression(expression).Evaluate(out showWork);

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="precision">Number of significant digits used in calculations.</param>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        public static double Evaluate(string expression, int precision, out string showWork)
            => new Expression(expression, precision).Evaluate(out showWork);

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="precision">Number of significant digits used in calculations.</param>
        public static double Evaluate(string expression, int precision)
            => new Expression(expression, precision).Evaluate();

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        public static double Evaluate(string expression)
            => new Expression(expression).Evaluate();

        /// <summary>
        /// Adds a parameter to the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void AddParameter(string name, double value) => _definedParameters.Add(name, value);

        /// <summary>
        /// Removes all parameters which have been previously added to the expression.
        /// </summary>
        public void ClearParameters() => _definedParameters.Clear();

        /// <summary>
        /// Adds a function to the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the function as found in the string mathematical expression.</param>
        /// <param name="function">Delegate of the function.</param>
        public void AddFunction(string name, CustomFunction function)
            => CustomFunctions.Add(name.ToLower(), function);

        /// <summary>
        /// Removes all functions which have been previously added to the expression.
        /// </summary>
        public void ClearFunction() => CustomFunctions.Clear();

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
                    copy = copy.Replace($"${cacheKey}$", CachedValue(cacheKey).ToString(PrecisionFormat));
                }
                else
                {
                    break;
                }
            }

            return copy;
        }

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        public double Evaluate()
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
                return CachedValue(WorkingText.AsSpan()[1..^1]);
            }

            return StringToDouble(WorkingText);
        }

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <returns></returns>
        public double Evaluate(out string showWork)
        {
            ResetState();

            StringBuilder work = new();

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
                return CachedValue(WorkingText.AsSpan()[1..^1]);
            }

            showWork = work.ToString();
            return StringToDouble(WorkingText);
        }

        internal void ResetState()
        {
            NextPreParsedCacheKey = 0;
            _nextPreComputedCacheKey = 0;
            WorkingText = Text; //Start with a pre-sanitized/validated copy of the supplied expression text.

            //Swap out all of the user supplied parameters.
            foreach (var variable in DiscoveredVariables)
            {
                if (_definedParameters.TryGetValue(variable, out var value))
                {
                    WorkingText = WorkingText.Replace(variable, value.ToString(PrecisionFormat));
                }
                else
                {
                    throw new Exception($"Undefined variable: {variable}");
                }
            }
        }

        internal double ExpToDouble(string exp)
        {
            if (exp[0] == '$')
            {
                return CachedValue(exp.AsSpan()[1..^1]);
            }
            return StringToDouble(exp);
        }

        internal string ReplaceRange(string original, int startIndex, int endIndex, string replacement)
        {
            _replaceRangeBuilder.Clear();
            int i;

            for (i = 0; i < startIndex; i++)
            {
                _replaceRangeBuilder.Append(original[i]);
            }

            _replaceRangeBuilder.Append(replacement);

            for (i = endIndex + 1; i < original.Length; i++)
            {
                _replaceRangeBuilder.Append(original[i]);
            }

            return _replaceRangeBuilder.ToString();
        }

        /// <summary>
        /// Gets a sub-expression from WorkingText and replaces it with a token.
        /// </summary>
        /// <returns></returns>
        internal bool AcquireSubexpression(out int outStartIndex, out int outEndIndex, out SubExpression outSubExpression)
        {
            int lastParenIndex = WorkingText.LastIndexOf('(');

            string subExpression = string.Empty;

            if (lastParenIndex >= 0)
            {
                outStartIndex = lastParenIndex;

                int scope = 0;
                int i = lastParenIndex;

                for (; i < WorkingText.Length; i++)
                {
                    if (char.IsWhiteSpace(WorkingText[i]))
                    {
                        continue;
                    }
                    else if (WorkingText[i] == '(')
                    {
                        subExpression += WorkingText[i];
                        scope++;
                    }
                    else if (WorkingText[i] == ')')
                    {
                        subExpression += WorkingText[i];
                        scope--;

                        if (scope == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        subExpression += WorkingText[i];
                    }
                }

                if (scope != 0)
                {
                    throw new Exception($"Parenthesizes mismatch when parsing subexpression.");
                }

                if (subExpression.StartsWith('(') == false || subExpression.EndsWith(')') == false)
                {
                    throw new Exception($"Sub-expression should be enclosed in parenthesizes.");
                }

                outEndIndex = i;

                outSubExpression = new SubExpression(this, subExpression);
                return false;
            }
            else
            {
                outStartIndex = 0;
                outEndIndex = WorkingText.Length - 1;
                subExpression = WorkingText;

                outSubExpression = new SubExpression(this, subExpression);
                return true;
            }
        }

        internal string Sanitize(string expressionText)
        {
            string result = string.Empty;

            int scope = 0;
            int consecutiveMathChars = 0;

            for (int i = 0; i < expressionText.Length;)
            {
                if (char.IsWhiteSpace(expressionText[i]))
                {
                    i++;
                    continue;
                }
                else if (Utility.IsMathChar(expressionText[i]))
                {
                    _operationCount++;
                    consecutiveMathChars++;

                    // If multiple operator characters appear in a row, that's a malformed expression.
                    if (consecutiveMathChars > 2)
                    {
                        throw new Exception($"Invalid consecutive operators near position {i}: '{expressionText[i]}'");
                    }

                    result += expressionText[i++];
                    continue;
                }
                else
                {
                    // Reset the consecutive operator counter when we hit a number, variable, or parenthesis
                    consecutiveMathChars = 0;
                }

                if (char.IsWhiteSpace(expressionText[i]))
                {
                    i++;
                    continue;
                }
                else if (expressionText[i] == ',')
                {
                    if (scope == 0)
                    {
                        throw new Exception("Unexpected comma found in expression.");
                    }

                    result += expressionText[i++];
                    continue;
                }
                else if (expressionText[i] == '(')
                {
                    _operationCount++;
                    scope++;
                    result += expressionText[i++];
                    continue;
                }
                else if (expressionText[i] == ')')
                {
                    scope--;

                    if (scope < 0)
                    {
                        throw new Exception($"Scope fell below zero while sanitizing input.");
                    }

                    result += expressionText[i++];
                    continue;
                }
                else if (Utility.IsMathChar(expressionText[i]))
                {
                    _operationCount++;
                    result += expressionText[i++];
                    continue;
                }
                else if (char.IsDigit(expressionText[i]))
                {
                    string buffer = string.Empty;

                    for (; i < expressionText.Length; i++)
                    {
                        if (char.IsDigit(expressionText[i]) || expressionText[i] == '.')
                        {
                            buffer += expressionText[i];
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (Utility.IsNumeric(buffer) == false)
                    {
                        throw new Exception($"Value is not a number: {buffer}");
                    }

                    result += buffer;
                    continue;
                }
                else if (Utility.IsValidVariableChar(expressionText[i]))
                {
                    //Parse the variable/function name and determine which it is. If its a function, then we want to swap out the opening and closing parenthesizes with curly braces.

                    string functionOrVariableName = string.Empty;
                    bool isFunction = false;

                    for (; i < expressionText.Length; i++)
                    {
                        if (char.IsWhiteSpace(expressionText[i]))
                        {
                            continue;
                        }
                        else if (Utility.IsValidVariableChar(expressionText[i]))
                        {
                            functionOrVariableName += expressionText[i];
                        }
                        else if (expressionText[i] == '(')
                        {
                            isFunction = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (isFunction)
                    {
                        result += functionOrVariableName; //Append the function name to the expression.

                        _operationCount++;
                        DiscoveredFunctions.Add(functionOrVariableName);

                        string functionExpression = string.Empty;
                        //If its a function, then lets find the opening and closing parenthesizes and replace them with curly braces.

                        int functionScope = 0;

                        for (; i < expressionText.Length; i++)
                        {
                            if (char.IsWhiteSpace(expressionText[i]))
                            {
                                continue;
                            }
                            else if (expressionText[i] == '(')
                            {
                                functionExpression += expressionText[i];
                                functionScope++;
                            }
                            else if (expressionText[i] == ')')
                            {
                                functionExpression += expressionText[i];
                                functionScope--;

                                if (functionScope == 0)
                                {
                                    i++; //Consume the closing paren.
                                    break;
                                }
                            }
                            else if (expressionText[i] == ',')
                            {
                                functionExpression += expressionText[i];
                                _operationCount++;
                            }
                            else
                            {
                                functionExpression += expressionText[i];
                            }
                        }

                        if (functionScope != 0)
                        {
                            throw new Exception($"Parenthesizes mismatch when parsing function scope: {functionOrVariableName}");
                        }

                        var functionParameterString = Sanitize(functionExpression);

                        if (functionParameterString.StartsWith('(') == false || functionParameterString.EndsWith(')') == false)
                        {
                            throw new Exception($"The function scope should be enclosed in parenthesizes.");
                        }

                        result += "{" + functionParameterString.Substring(1, functionParameterString.Length - 2) + "}";
                    }
                    else
                    {
                        result += functionOrVariableName; //Append the function name to the expression.

                        _operationCount++;
                        DiscoveredVariables.Add(functionOrVariableName);
                    }
                }
                else
                {
                    throw new Exception($"Unhandled character {expressionText[i]}");
                }
            }

            if (scope != 0)
            {
                throw new Exception($"Scope mismatch while sanitizing input.");
            }

            return result;
        }

        internal double StringToDouble(ReadOnlySpan<char> span)
        {
            if (span[0] == '$')
            {
                return CachedValue(span[1..^1]);
            }

            double result = 0.0;
            int length = span.Length;
            int i = 0;

            if (length > 0 && (span[0] == '-' || span[0] == '+'))
            {
                i++;
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

                    double fraction = 0.0;
                    double multiplier = 1.0;

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

            if (length > 0 && span[0] == '-')
            {
                return -result;
            }
            return result;
        }
    }
}
