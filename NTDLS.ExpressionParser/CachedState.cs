namespace NTDLS.ExpressionParser
{
    internal class CachedState(Sanitized sanitized, ExpressionState state)
    {
        public Sanitized Sanitized { get; set; } = sanitized;
        public ExpressionState State { get; set; } = state;
    }
}
