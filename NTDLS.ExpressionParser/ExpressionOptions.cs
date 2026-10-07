namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Represents configuration options for controlling the behavior of an expression parser.
    /// </summary>
    public class ExpressionOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether or not to cache and reuse the compiled expression, so that creating
        /// another Expression with the same text (and options) skips parsing. Entries expire after 5 minutes unused.
        /// </summary>
        public bool UseCompileCache { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use the fast floating-point parser for number literals.
        /// It is correctly rounded, and falls back to double.Parse for numbers it can not parse exactly.
        /// </summary>
        public bool UseFastFloatingPointParser { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of significant digits used to format numbers when showing the work.
        /// Calculations always use full double precision.
        /// </summary>
        public ushort Precision { get; set; } = 17;

        /// <summary>
        /// Gets or sets the value to use in place of NULL: for null literals, variables set to null, and custom
        /// functions that return null. When not set, NULL propagates through operations and the result is NULL.
        /// </summary>
        public double? DefaultNullValue { get; set; } = null;

        /// <summary>
        /// A key to cache the compiled expression under, used instead of the expression text.
        /// Every expression given the same CustomHash (and options) shares one compiled expression, so it must
        /// only be reused for identical expression text.
        /// </summary>
        public string? CustomHash { get; set; }

        /// <summary>
        /// Returns a hash code for the current object based on its configuration properties.
        /// </summary>
        public int OptionsHash()
        {
            int hash = 17;
            hash = hash * 31 + (UseCompileCache ? 1 : 0);
            hash = hash * 31 + (UseFastFloatingPointParser ? 1 : 0);
            hash = hash * 31 + Precision;
            hash = hash * 31 + (DefaultNullValue.HasValue ? 1 : 2);
            hash = hash * 31 + (int)(DefaultNullValue ?? 0);
            return hash;
        }
    }
}
