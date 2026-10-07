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
}
