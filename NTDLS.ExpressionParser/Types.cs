namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Delegate for calling a custom function.
    /// </summary>
    public delegate double? ExpressionFunction(double[] parameters);

    internal enum BinaryOperator
    {
        //Order must match Utility._binaryOperatorText.
        Multiply, Divide, Modulus, Add, Subtract, ShiftLeft, ShiftRight,
        Less, LessOrEqual, Greater, GreaterOrEqual, Equal, DoubleEqual, NotEqual, LessGreater,
        BitwiseAnd, BitwiseAndEqual, BitwiseXor, BitwiseXorEqual, BitwiseOr, BitwiseOrEqual,
        LogicalAnd, LogicalOr
    }

    internal struct ComputedStepItem
    {
        public double? ParsedValue;
        public int BeginPosition;
        public int EndPosition;
        public bool IsUserVariableDerived;
    }

    internal struct OperationStepItem
    {
        public string Operation;
        public int Index;
        public bool IsValid;
    }

    internal struct ScanStepItem
    {
        public double? Value;
        public int Length;
        public bool IsUserVariableDerived;
    }

    internal struct PlaceholderCacheItem
    {
        public double? ComputedValue;
        public bool IsUserVariableDerived;
        public bool IsNullValue;
    }
}
