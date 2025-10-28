using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class RepeatedUseTests
    {
        [Fact]
        public void Simple()
        {
            var expr = new Expression("v1");

            expr.SetParameter("v1", 12345);
            Console.WriteLine(expr.Evaluate());
            expr.SetParameter("v1", 12345);
            Console.WriteLine(expr.Evaluate());
            expr.SetParameter("v1", 12345);
            Console.WriteLine(expr.Evaluate());
            expr.SetParameter("v1", 12345);
            Console.WriteLine(expr.Evaluate());
        }
    }
}
