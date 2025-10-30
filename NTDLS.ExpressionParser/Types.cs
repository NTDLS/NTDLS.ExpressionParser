namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Delegate for calling a custom function.
    /// </summary>
    public delegate double? ExpressionFunction(double[] parameters);

    internal struct ComputedStepItem
    {
        public double? ParsedValue;
        public int BeginPosition;
        public int EndPosition;
    }

    internal struct OperationStepItem
    {
        public string Operation;
        public int Index;
        public bool Found;
    }

    internal struct ScanStepItem
    {
        public double? Value;
        public int Length;
    }

    internal struct PlaceholderCacheItem
    {
        public double? ComputedValue;
        public bool IsUserVariableDerived;
        public bool IsNullValue;
    }
}
