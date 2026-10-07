using Microsoft.Extensions.Caching.Memory;

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

        private readonly CompiledExpression _compiled;

        internal ExpressionOptions Options { get; set; }
        internal Dictionary<string, ExpressionFunction> ExpressionFunctions => _expressionFunctions ??= new();

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
                object cacheKey = new CacheKey(Options.CustomHash ?? text, Options.CustomHash != null,
                    Options.UseFastFloatingPointParser, Options.DefaultNullValue);

                //Check for a hit first to avoid allocating the factory closure on the common path.
                if (!Utility.PersistentCaches.TryGetValue(cacheKey, out CompiledExpression? compiled) || compiled == null)
                {
                    compiled = CreateCachedCompilation(cacheKey, text, Options);
                }

                _compiled = compiled;
            }
            else
            {
                _compiled = Compile(text, Options);
            }
        }

        private static CompiledExpression Compile(string text, ExpressionOptions options)
            => CompiledExpression.Compile(Sanitizer.Process(text.ToLowerInvariant(), options), options);

        private static CompiledExpression CreateCachedCompilation(object cacheKey, string text, ExpressionOptions options)
        {
            return Utility.PersistentCaches.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                return Compile(text, options);
            }) ?? throw new Exception("Failed to create persistent cache.");
        }

        #endregion

        #region Evaluate.

        /// <summary>
        /// The number of operations (unary operators, binary operators and function calls) that the expression
        /// performs as written, before any constant folding. For example "10 * (5 + 1000)" performs 2.
        /// </summary>
        public int OperationCount => _compiled.OperationCount;

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        public double? Evaluate()
            => _compiled.Evaluate(_definedParameters, _expressionFunctions);

        /// <summary>
        /// Evaluates the expression, processing all variables and functions.
        /// </summary>
        /// <param name="showWork">Output parameter for the operational explanation: each operation, in the order performed.</param>
        /// <returns></returns>
        public double? Evaluate(out string showWork)
            => _compiled.Evaluate(_definedParameters, _expressionFunctions, out showWork);

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
    }
}
