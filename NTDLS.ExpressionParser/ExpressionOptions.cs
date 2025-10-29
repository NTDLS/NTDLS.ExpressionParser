namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Represents configuration options for controlling the behavior of an expression parser.
    /// </summary>
    public class ExpressionOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether or not to cache and reuse the sanitized expression and state.
        /// </summary>
        public bool UseCompileCache { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use the fast floating-point parser.
        /// </summary>
        public bool UseFastFloatingPointParser { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of significant digits used in calculations.
        /// </summary>
        public ushort Precision { get; set; } = 17;

        /// <summary>
        /// Gets or sets the default value to use when a NULL is encountered in expressions.
        /// </summary>
        public double? DefaultNullValue { get; set; } = null;

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
