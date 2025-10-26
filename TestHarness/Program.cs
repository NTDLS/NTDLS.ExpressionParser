using NTDLS.ExpressionParser;
using System.Diagnostics;

namespace TestHarness
{
    internal static class Program
    {
        static void Main()
        {
            /*
            var f = Expression.Evaluate("pow(sin(1), 2) + pow(cos(1), 2)", out string work);
            //var f = Expression.Evaluate("2 + 3 * (4 - 1 + (2))");
            //var f = Expression.Evaluate("2 + 3 * (1 + 2)");
            Console.WriteLine(f);
            Console.WriteLine(work);
            */

            /*
            //var f = Expression.Evaluate("10 + 3 * (4 - 1 + 2)");
            //var f = Expression.Evaluate("1 + (2 + 3)");
            //Console.WriteLine(f);


            var expr = new Expression("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10");

            Console.WriteLine(expr.Evaluate());
            Console.WriteLine(expr.Evaluate());

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
            */


            var timings = new List<double>();

            for (int i = 0; i < 20; i++)
            {
                var totalTime = Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);

                timings.Add(totalTime / 3);

                Console.WriteLine($"{(totalTime / 3):n6}");
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}");

            //Console.WriteLine("Press [enter] to close.");
            //Console.ReadLine();

        }

        static double Perform(string expr, int iterations)
        {
            Console.Write($"[{expr}] -> ");

            var expression = new Expression(expr);

            expression.Evaluate(); // Warm-up

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                expression.Evaluate(); //Typically takes ~3µs.
            }
            stopwatch.Stop();

            double avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
            Console.WriteLine($"{iterations:n0} iterations, {avgMs:n6} ms per iteration");

            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}