namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Delegate for calling a custom function.
    /// </summary>
    public delegate double? ExpressionFunction(double[] parameters);

    internal struct PreParsedCacheItem
    {
        public double? ParsedValue;
        public int BeginPosition;
        public int EndPosition;
    }

    internal struct PreComputedCacheItem
    {
        public double? ComputedValue;
        public bool IsVariable;
        public bool IsNullValue;
    }
}
