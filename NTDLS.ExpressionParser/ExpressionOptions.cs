namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// Represents configuration options for controlling the behavior of an expression parser.
    /// </summary>
    public class ExpressionOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use the fast floating-point parser.
        /// </summary>
        public bool UseFastFloatingPointParser { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the parser cache is enabled.
        /// </summary>
        public bool UseParserCache { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of significant digits used in calculations.
        /// </summary>
        public ushort Precision { get; set; } = 17;

        /// <summary>
        /// Gets or sets the default value to use when a NULL is encountered in expressions.
        /// </summary>
        public double? DefaultNullValue { get; set; } = null;
    }
}
