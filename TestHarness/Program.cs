using NTDLS.ExpressionParser;
using System.Diagnostics;

namespace TestHarness
{
    internal static class Program
    {
        static void EvalPrint(string exp)
        {
            var expr = new Expression(exp);
            var result = expr.Evaluate();
            Console.WriteLine($"{exp}: {result?.ToString() ?? "{NULL}"}");
        }

        static void Baseline()
        {
            var expression = new Expression("(2+3)*4"); //71.8829

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 100000; i++)
            {
                expression.Evaluate(); //522.17

            }
            stopwatch.Stop();

            Console.WriteLine(stopwatch.Elapsed.TotalMilliseconds);
        }

        static void Main()
        {
            //Baseline();

            //Environment.Exit(0);

            //var expression = new Expression("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10");
            //Console.WriteLine("---FIRST---");
            //Console.WriteLine(expression.Evaluate()); //20
            //Console.WriteLine("---SECOND---");
            //Console.WriteLine(expression.Evaluate()); //20
            //Console.WriteLine(expression.Evaluate()); //20

            //expression.SetParameter("extra", 10);
            //Console.WriteLine(expression.Evaluate()); //151250

            //expression.SetParameter("extra", 20);
            //Console.WriteLine(expression.Evaluate()); //211750

//#if !DEBUG
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
//#endif
        }

        static double Perform(string expr, int iterations)
        {
            Console.Write($"[{expr}] -> ");

            var expression = new Expression(expr);

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                if (expression.Evaluate() != 6140750)//Typically takes ~0.9µs.
                {
                    throw new Exception("Unexpected result");
                }

            }
            stopwatch.Stop();

            double avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
            Console.WriteLine($"{iterations:n0} iterations, {avgMs:n6} ms per iteration");

            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}