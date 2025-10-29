namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Delegate for calling a custom function.
    /// </summary>
    public delegate double? ExpressionFunction(double[] parameters);

    internal struct ComputedStepItem
    {
        public bool IsValid;
        public double? ParsedValue;
        public int BeginPosition;
        public int EndPosition;
    }

    internal struct ScanStepItem
    {
        public bool IsValid;
        public double? ParsedValue;
        public int Length;
    }

    internal struct PlaceholderCacheItem
    {
        public double? ComputedValue;
        public bool IsVariable;
        public bool IsNullValue;
    }
}
