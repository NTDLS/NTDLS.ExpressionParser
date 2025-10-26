using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class CustomFunctionTests
    {
        private static double Eval(string expr)
            => Expression.EvaluateNotNull(expr);

        [Fact]
        public void Custom_Function_CustomSum()
        {
            var expression = new Expression("10 * ((5 + extra + CustomSum(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10");

            expression.SetParameter("extra", 1000);

            expression.AddFunction("CustomSum", parameters =>
            {
                double sum = 0;

                foreach (var parameter in parameters)
                {
                    sum += parameter;
                }

                return sum;
            });

            Assert.Equal(5416050, expression.Evaluate());
        }
    }
}
