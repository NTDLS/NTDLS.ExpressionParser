namespace NTDLS.ExpressionParser
{
    internal class Sanitized
    {
        public int OperationCount { get; set; }
        public int ConsumedPlaceholderCacheSlots { get; set; }
        public string Text { get; set; } = string.Empty;
        internal HashSet<string> DiscoveredVariables { get; private set; } = new();
        internal HashSet<string> DiscoveredFunctions { get; private set; } = new();

        private string[]? _variables;

        /// <summary>
        /// The discovered variables as an array, for allocation-free indexed access while evaluating.
        /// </summary>
        internal string[] Variables => _variables ??= DiscoveredVariables.ToArray();
    }
}
