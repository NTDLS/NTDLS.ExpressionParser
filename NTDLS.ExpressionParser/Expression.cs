using Microsoft.Extensions.Caching.Memory;
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
        /// Shared options for expressions created without any. Never exposed, so it can never be modified.
        /// </summary>
        private static readonly ExpressionOptions _defaultOptions = new();

        private Dictionary<string, double?>? _definedParameters;
        private Dictionary<string, ExpressionFunction>? _expressionFunctions;

        private readonly CachedState? _template;
        private readonly CompiledExpression? _compiled;
        private ExpressionState? _state;

        internal Sanitized Sanitized { get; set; }
        internal ExpressionOptions Options { get; set; }

        /// <summary>
        /// State for the string based evaluator, created on first use. It is not needed when the expression
        /// was compiled, unless the work is being shown.
        /// </summary>
        internal ExpressionState State => _state ??= _template != null
            ? _template.State.Clone(_template.Sanitized)
            : new ExpressionState(Sanitized, Options);
        internal Dictionary<string, ExpressionFunction> ExpressionFunctions => _expressionFunctions ??= new();

        /// <summary>
        /// The boxed CacheKey, boxed once so that each cache lookup does not box it again.
        /// </summary>
        private readonly object? _cacheKey;

        /// <summary>
        /// Identifies a compiled expression in the persistent cache. Includes every option that is baked into the
        /// cached state so that expressions with differing options can never share an entry.
        /// </summary>
        private readonly record struct CacheKey(string Text, bool IsCustomHash, bool UseFastFloatingPointParser, double? DefaultNullValue);

        #region ~/ctor and Sanitize.

        /// <summary>
        /// Represents a mathematical expression.
        /// </summary>
        public Expression(string text, ExpressionOptions? options = null)
        {
            Options = options ?? _defaultOptions;

            if (Options.UseCompileCache)
            {
                _cacheKey = new CacheKey(Options.CustomHash ?? text, Options.CustomHash != null,
                    Options.UseFastFloatingPointParser, Options.DefaultNullValue);

                //Check for a hit first to avoid allocating the factory closure on the common path.
                if (!Utility.PersistentCaches.TryGetValue(_cacheKey, out CachedState? cached) || cached == null)
                {
                    cached = CreateCachedState(_cacheKey, text, Options);
                }

                _template = cached;
                _compiled = cached.Compiled;
                Sanitized = cached.Sanitized;
            }
            else
            {
                Sanitized = Sanitizer.Process(text.ToLowerInvariant(), Options);
                _compiled = CompiledExpression.TryCompile(Sanitized, Options);
            }
        }

        private static CachedState CreateCachedState(object cacheKey, string text, ExpressionOptions options)
        {
            return Utility.PersistentCaches.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);

                var sanitized = Sanitizer.Process(text.ToLowerInvariant(), options);
                var state = new ExpressionState(sanitized, options);
                return new CachedState(sanitized, state, CompiledExpression.TryCompile(sanitized, options));
            }) ?? throw new Exception("Failed to create persistent cache.");
        }

        #endregion

        #region Evaluate.

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        public double? Evaluate()
        {
            if (_compiled != null)
                return _compiled.Evaluate(_definedParameters, _expressionFunctions, Options.DefaultNullValue);

            return EvaluateText();
        }

        /// <summary>
        /// Evaluates the expression using the string based evaluator.
        /// </summary>
        private double? EvaluateText()
        {
            State.Reset(Sanitized);
            State.ApplyParameters(Sanitized, _definedParameters);

            bool isComplete;
            do
            {
                //Get a sub-expression from the whole expression.
                isComplete = AcquireSubexpression(out int startIndex, out int endIndex, out var subExpression);
                //Compute the sub-expression.
                var resultString = subExpression.Compute();
                //Replace the sub-expression in the whole expression with the result from the sub-expression computation.
                State.WorkingText = ReplaceRange(State.WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            if (_cacheKey != null)
                State.HydrateTemplateCache(_cacheKey);

            if (Utility.IsSinglePlaceholder(State.WorkingText))
                return State.GetPlaceholderCacheItem(State.WorkingText.AsSpan()[1..^1]).ComputedValue;

            return StringToDouble(State.WorkingText, out _);
        }

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        /// <param name="showWork">Output parameter for the operational explanation.</param>
        /// <returns></returns>
        public double? Evaluate(out string showWork)
        {
            State.Reset(Sanitized);
            State.ApplyParameters(Sanitized, _definedParameters);

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
                State.WorkingText = ReplaceRange(State.WorkingText, startIndex, endIndex, resultString);
            } while (!isComplete);

            work.AppendLine($"}} = {SwapInCacheValues(State.WorkingText)}");

            showWork = work.ToString();
            if (_cacheKey != null)
                State.HydrateTemplateCache(_cacheKey);

            if (Utility.IsSinglePlaceholder(State.WorkingText))
                return State.GetPlaceholderCacheItem(State.WorkingText.AsSpan()[1..^1]).ComputedValue;

            return StringToDouble(State.WorkingText, out _);
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
            var result = new Expression(expression, options).Evaluate(out showWork);
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
            var result = new Expression(expression, options).Evaluate();
            outResultWasNull = result == null;
            return result ?? 0;
        }

        /// <summary>
        /// Evaluates a mathematical expression.
        /// </summary>
        /// <param name="expression">Mathematical expression in string form.</param>
        /// <param name="options">Expression evaluation options.</param>
        public static double EvaluateNotNull(string expression, ExpressionOptions? options = null)
             => new Expression(expression, options).Evaluate() ?? 0;

        #endregion

        #region Set/Get/Clear Parameters.

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, double? value) => (_definedParameters ??= new())[name.ToLowerInvariant()] = value;

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, int? value) => (_definedParameters ??= new())[name.ToLowerInvariant()] = value;

        /// <summary>
        /// Sets a parameter in the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        /// <param name="value">Value of the variable.</param>
        public void SetParameter(string name, bool? value) => (_definedParameters ??= new())[name.ToLowerInvariant()] = value == null ? null : value == true ? 1 : 0;

        /// <summary>
        /// Removed a parameter from the mathematical expression.
        /// </summary>
        /// <param name="name">Name of the variable as found in the string mathematical expression.</param>
        public void RemoveParameter(string name) => _definedParameters?.Remove(name.ToLowerInvariant());

        /// <summary>
        /// Removes all parameters which have been previously added to the expression.
        /// </summary>
        public void ClearParameters() => _definedParameters?.Clear();

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
            => _expressionFunctions?.Remove(name.ToLowerInvariant());

        /// <summary>
        /// Removes all functions which have been previously added to the expression.
        /// </summary>
        public void ClearFunctions() => _expressionFunctions?.Clear();

        #endregion

        /// <summary>
        /// Replaces placeholders in the input text with their corresponding precomputed cache values.
        /// This function is only used when showing work, so performance is not critical.
        /// </summary>
        private string SwapInCacheValues(string text)
        {
            var precisionFormat = $"G{Options.Precision}";
            var copy = text;

            while (true)
            {
                int begIndex = copy.IndexOf('$');
                int endIndex = copy.IndexOf('$', begIndex + 1);

                if (begIndex >= 0 && endIndex > begIndex)
                {
                    var cacheKey = copy.Substring(begIndex + 1, (endIndex - begIndex) - 1);
                    copy = copy.Replace($"${cacheKey}$", State.GetPlaceholderCacheItem(cacheKey).ComputedValue?.ToString(precisionFormat) ?? "null");
                }
                else
                {
                    break;
                }
            }

            return copy;
        }

        internal string ReplaceRange(string original, int startIndex, int endIndex, string replacement)
        {
            return string.Concat(original.AsSpan(0, startIndex), replacement, original.AsSpan(endIndex + 1));
        }

        /// <summary>
        /// Gets a sub-expression from WorkingText and replaces it with a token.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AcquireSubexpression(out int outStartIndex, out int outEndIndex, out SubExpression outSubExpression)
        {
            int lastParenIndex = State.WorkingText.LastIndexOf('(');

            if (lastParenIndex >= 0)
            {
                outStartIndex = lastParenIndex;

                int scope = 0;
                int i = lastParenIndex;

                for (; i < State.WorkingText.Length; i++)
                {
                    char c = State.WorkingText[i];

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

                var subExprSpan = State.WorkingText.AsSpan(outStartIndex, outEndIndex - outStartIndex + 1);

                if (subExprSpan[0] != '(' || subExprSpan[^1] != ')')
                    throw new Exception("Sub-expression should be enclosed in parentheses.");

                outSubExpression = new SubExpression(this, subExprSpan.ToString());
                return false;
            }
            else
            {
                outStartIndex = 0;
                outEndIndex = State.WorkingText.Length - 1;
                outSubExpression = new SubExpression(this, State.WorkingText);
                return true;
            }
        }

        /// <summary>
        /// Converts a string or value of a stored cache key into a double.
        /// </summary>
        internal double? StringToDouble(ReadOnlySpan<char> span, out bool isUserVariableDerived)
        {
            if (span.Length == 0)
            {
                isUserVariableDerived = false;
                return null;
            }
            else if (span[0] == '$')
            {
                var placeholder = State.GetPlaceholderCacheItem(span[1..^1]);
                isUserVariableDerived = placeholder.IsUserVariableDerived;
                return placeholder.ComputedValue;
            }
            else if (span.Length > 1 && span[1] == '$' && (span[0] == '-' || span[0] == '+'))
            {
                //Explicitly signed placeholder, such as the result of "-(2+3)" or "-x".
                var placeholder = State.GetPlaceholderCacheItem(span[2..^1]);
                isUserVariableDerived = placeholder.IsUserVariableDerived;
                return span[0] == '-' ? -placeholder.ComputedValue : placeholder.ComputedValue;
            }

            isUserVariableDerived = false;
            return Utility.ParseNumber(span, Options.UseFastFloatingPointParser);
        }
    }
}
