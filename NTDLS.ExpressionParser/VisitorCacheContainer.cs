namespace NTDLS.ExpressionParser
{
    internal struct VisitorCacheContainer<T>(T value, bool isValid) where T : struct
    {
        public T Value = value;
        public bool IsValid = isValid;
    }
}
