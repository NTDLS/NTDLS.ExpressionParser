using NTDLS.ExpressionParser;

namespace TestApp.CSharp
{
    internal static class Program
    {
        static void Main()
        {


            //("10 * ((5 + extra + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10": 5,086,050 when "extra" is 1000
            /*
                        var expression = new Expression("10 * ((5 + extra + DoStuff(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10");

                        expression.AddParameter("extra", 1000);

                        expression.AddFunction("DoStuff", (double[] parameters) =>
                        {
                            double sum = 0;

                            foreach (var parameter in parameters)
                            {
                                sum += parameter;
                            }

                            return sum;
                        });
            */
            /*
            var expression = new Expression("10 * ((5 + 1000 + ( 10 + !0 )) + 60.5) * 10");
            //var expression = new Expression("-10+50");

            DateTime startDate = DateTime.Now;
            for (int i = 0; i < 500000; i++)
            {
                var result = expression.Evaluate();
            }
            Console.WriteLine($"NTDLS: {(DateTime.Now - startDate).TotalMilliseconds}");

            //Console.WriteLine($"Result: {result}");
            */

            var result = Expression.Evaluate("10 * 10 * -10 * 11");

            string strExpr = "10 * ((5 + 1000 + ( 10 )) *  60.5) * 10"; //Need to hit 160ms with 100000 iterations

            var ntdlsExpression = new Expression(strExpr);
            DateTime startDate = DateTime.Now;
            for (int i = 0; i < 100000; i++)
            {
                ntdlsExpression.Evaluate();
            }
            Console.WriteLine($"NTDLS: {(DateTime.Now - startDate).TotalMilliseconds}");
            Console.ReadLine();

        }
    }
}
