using NTDLS.ExpressionParser;

namespace TestApp.CSharp
{
    internal static class Program
    {
        static void Main()
        {
            var result = Expression.Evaluate("10 * ((5 + 1000 + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10", out string work);
            Console.WriteLine(work);

            //("10 * ((5 + extra + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10": 5,086,050 when "extra" is 1000
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

            expression.Evaluate();

            Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10");

            Console.WriteLine("Press [enter] to close.");
            Console.ReadLine();
        }

        static void Perform(string expr)
        {
            Console.Write($"[{expr}] -> ");

            var expression = new Expression(expr);

            int iterations = 100000;

            DateTime startDate = DateTime.Now;
            for (int i = 0; i < iterations; i++)
            {
                expression.Evaluate();
            }

            Console.WriteLine($"{iterations:n0} iterations, {(((DateTime.Now - startDate).TotalMilliseconds) / iterations):n6}ms");
        }
    }
}
