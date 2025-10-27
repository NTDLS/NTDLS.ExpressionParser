namespace NTDLS.ExpressionParser
{
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
    }
}
