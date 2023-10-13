namespace NTDLS.ExpressionParser
{
    public static class ExpressionParser
    {
        public static double Evaluate(string expressionText) => Evaluate(new Expression(expressionText));

        public static double Evaluate(Expression expression)
        {
            expression.ResetWorkingText();

            while (Utility.IsNumeric(expression.WorkingText) == false)
            {
                var subExpression = expression.AcquireSubexpression(out int startIndex, out int endIndex);
                var resultString = subExpression.Compute();
                expression.ReplaceRange(startIndex, endIndex, resultString);
            }

            return double.Parse(expression.WorkingText);
        }
    }
}
