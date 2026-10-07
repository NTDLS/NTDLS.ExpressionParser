namespace NTDLS.ExpressionParser
{
    internal class CachedState(Sanitized sanitized, ExpressionState state, CompiledExpression? compiled)
    {
        public Sanitized Sanitized { get; set; } = sanitized;
        public ExpressionState State { get; set; } = state;

        /// <summary>
        /// The compiled form of the expression, or null when it could not be compiled.
        /// </summary>
        public CompiledExpression? Compiled { get; } = compiled;
    }
}
