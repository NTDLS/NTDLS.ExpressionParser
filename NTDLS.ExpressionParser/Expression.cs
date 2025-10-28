using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class CachedState
    {
        public Sanitized Sanitized { get; set; }
        public ExpressionState State { get; set; }

        public CachedState(Sanitized sanitized, ExpressionState state)
        {
            Sanitized = sanitized;
            State = state;
        }
    }

    /// <summary>
    /// Represents a mathematical expression.
    /// </summary>
    public class Expression
    {
        /// <summary>
        /// Delegate for calling a custom function.
        /// </summary>
        public delegate double? ExpressionFunction(double[] parameters);

        internal Sanitized _sanitized;

        private readonly string _text = string.Empty;
        private readonly string _precisionFormat;
        private readonly Dictionary<string, double?> _definedParameters = new();

        internal ExpressionOptions Options { get; set; }
        internal Dictionary<string, ExpressionFunction> ExpressionFunctions { get; private set; } = new();

        #region ~/ctor and Sanitize.

        /// <summary>
        /// Represents a mathematical expression.
        /// </summary>
        public Expression(string text, ExpressionOptions? options = null)
        {
            Options = options ?? new ExpressionOptions();
            _precisionFormat = $"G{Options.Precision}";

            Utility.PersistentCaches.GetOrCreate(Options.GetHashCode(), () =>
            {
                var defaultState = new ExpressionState(string.Empty, Options);
                return new CachedState(new Sanitized(string.Empty), defaultState);
            });

            _sanitized = Sanitizer.Process(text.ToLowerInvariant(), Options);

            var templateState = new ExpressionState(_sanitized, Options);
            var state = templateState.Clone();


        }

        #endregion


        #region Evaluate.

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        public double? Evaluate()
        {
            var templateState = new ExpressionState(_sanitized, Options);
            var state = templateState.Clone();

            state.ApplyParameters(_sanitized, _definedParameters);

            bool isComplete;
            do
            {
                //Get a sub-expression from the whole expression.
                isComplete = AcquireSubexpression(state, out int startIndex, out int endIndex, out var subExpression);
                //Compute the sub-expression.
                var resultString = subExpression.Compute();
                //Replace the sub-expression in the whole expression with the result from the sub-expression computation.
                state.WorkingText = ReplaceRange(state, state.WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            if (state.WorkingText[0] == '$')
            {
                return state.GetPreComputedCacheItem(state.WorkingText.AsSpan()[1..^1]).ComputedValue;
            }

            return StringToDouble(state, state.WorkingText);
        }

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <returns></returns>
        public double? Evaluate(out string showWork)
        {
            var templateState = new ExpressionState(_sanitized, Options);
            var state = templateState.Clone();

            state.ApplyParameters(_sanitized, _definedParameters);

            var work = new StringBuilder();

            work.AppendLine("{");

            bool isComplete;
            do
            {
                //Get a sub-expression from the whole expression.
                isComplete = AcquireSubexpression(state, out int startIndex, out int endIndex, out var subExpression);

                string friendlySubExpression = SwapInCacheValues(state, subExpression.Text);
                work.Append("    " + friendlySubExpression);

                //Compute the sub-expression.
                var resultString = subExpression.Compute();

                work.AppendLine($" = {SwapInCacheValues(state, resultString)}");

                //Replace the sub-expression in the whole expression with the result from the sub-expression computation.
                state.WorkingText = ReplaceRange(state, state.WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            work.AppendLine($"}} = {SwapInCacheValues(state, state.WorkingText)}");

            if (state.WorkingText[0] == '$')
            {
                showWork = work.ToString();
                return state.GetPreComputedCacheItem(state.WorkingText.AsSpan()[1..^1]).ComputedValue;
            }

            showWork = work.ToString();
            return StringToDouble(state, state.WorkingText);
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
        private string SwapInCacheValues(ExpressionState state, string text)
        {
            var copy = new string(text);

            while (true)
            {
                int begIndex = copy.IndexOf('$');
                int endIndex = copy.IndexOf('$', begIndex + 1);

                if (begIndex >= 0 && endIndex > begIndex)
                {
                    var cacheKey = copy.Substring(begIndex + 1, (endIndex - begIndex) - 1);
                    copy = copy.Replace($"${cacheKey}$", state.GetPreComputedCacheItem(cacheKey).ComputedValue?.ToString(_precisionFormat) ?? "null");
                }
                else
                {
                    break;
                }
            }

            return copy;
        }

        internal string ReplaceRange(ExpressionState state, string original, int startIndex, int endIndex, string replacement)
        {
            state._buffer.Clear();
            state._buffer.Append(original.AsSpan(0, startIndex));
            state._buffer.Append(replacement);
            state._buffer.Append(original.AsSpan(endIndex + 1));
            return state._buffer.ToString();
        }

        /// <summary>
        /// Gets a sub-expression from WorkingText and replaces it with a token.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AcquireSubexpression(ExpressionState state, out int outStartIndex, out int outEndIndex, out SubExpression outSubExpression)
        {
            int lastParenIndex = state.WorkingText.LastIndexOf('(');

            if (lastParenIndex >= 0)
            {
                outStartIndex = lastParenIndex;

                int scope = 0;
                int i = lastParenIndex;

                for (; i < state.WorkingText.Length; i++)
                {
                    char c = state.WorkingText[i];

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

                var subExprSpan = state.WorkingText.AsSpan(outStartIndex, outEndIndex - outStartIndex + 1);

                if (subExprSpan[0] != '(' || subExprSpan[^1] != ')')
                    throw new Exception("Sub-expression should be enclosed in parentheses.");

                outSubExpression = new SubExpression(this, state, subExprSpan.ToString());
                return false;
            }
            else
            {
                outStartIndex = 0;
                outEndIndex = state.WorkingText.Length - 1;
                outSubExpression = new SubExpression(this, state, state.WorkingText);
                return true;
            }
        }

        /// <summary>
        /// Converts a string or value of a stored cache key into a double.
        /// </summary>
        internal double? StringToDouble(ExpressionState state, ReadOnlySpan<char> span)
        {
            if (span.Length == 0)
            {
                return null;
            }
            else if (span[0] == '$')
            {
                return state.GetPreComputedCacheItem(span[1..^1]).ComputedValue;
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
