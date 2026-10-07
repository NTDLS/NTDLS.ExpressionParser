using System.Diagnostics;

namespace PerfTest
{
    internal class Program
    {
        /// <summary>
        /// Evaluations per timed run.
        /// </summary>
        const int Iterations = 100000;

        /// <summary>
        /// Uses variables so that neither parser can compute the result ahead of time (e.g. by constant folding),
        /// meaning every evaluation performs every operation.
        /// </summary>
        const string TestExpression = "10 * ((a + 1000 + ( b )) *  c) * 10";

        static readonly Dictionary<string, double> TestParameters = new() { ["a"] = 5, ["b"] = 10, ["c"] = 60.5 };

        const double ExpectedResult = 6140750;

        /// <summary>
        /// Math operations performed by each evaluation of the test expression, so that op/μs counts operations
        /// rather than evaluations. The same expression is given to both parsers, so the count applies to both.
        /// </summary>
        static readonly int OperationsPerEvaluation = new NTDLS.ExpressionParser.Expression(TestExpression).OperationCount;

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; //For the "μ" in op/μs.

            Console.WriteLine($"Expression: {TestExpression} with {string.Join(", ", TestParameters.Select(p => $"{p.Key}={p.Value}"))}"
                + $" ({OperationsPerEvaluation} operations per evaluation)");

            NTDLS();
            NCALC();
            //NTDLSPerCalc();
            //NCALCPerCalc();
        }

        static void NTDLS()
        {
            var timings = new List<double>();

            for (int i = 0; i < 100; i++)
            {
                var totalTime = Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NTDLS       : Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}, op/μs: {(double)Iterations * OperationsPerEvaluation / (avg * 1000):n2}");

            static double Perform(string expr, int iterations)
            {
                var expression = new NTDLS.ExpressionParser.Expression(expr);
                foreach (var parameter in TestParameters)
                    expression.SetParameter(parameter.Key, parameter.Value);

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    if (expression.Evaluate() != ExpectedResult)
                        throw new Exception("Unexpected result");

                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        static void NCALC()
        {
            var timings = new List<double>();

            for (int i = 0; i < 100; i++)
            {
                var totalTime = Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NCALC       : Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}, op/μs: {(double)Iterations * OperationsPerEvaluation / (avg * 1000):n2}");

            static double Perform(string expr, int iterations)
            {
                var expression = new NCalc.Expression(expr);
                foreach (var parameter in TestParameters)
                    expression.Parameters[parameter.Key] = parameter.Value;

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    if (((double?)expression.Evaluate()) != ExpectedResult)
                        throw new Exception("Unexpected result");
                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        static void NTDLSPerCalc()
        {
            var timings = new List<double>();

            for (int i = 0; i < 20; i++)
            {
                var totalTime = Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NTDLSPerCalc: Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}, op/μs: {(double)Iterations * OperationsPerEvaluation / (avg * 1000):n2}");

            static double Perform(string expr, int iterations)
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    var expression = new NTDLS.ExpressionParser.Expression(expr);
                    foreach (var parameter in TestParameters)
                        expression.SetParameter(parameter.Key, parameter.Value);
                    if (expression.Evaluate() != ExpectedResult)
                        throw new Exception("Unexpected result");

                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        static void NCALCPerCalc()
        {
            var timings = new List<double>();

            for (int i = 0; i < 20; i++)
            {
                var totalTime = Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);
                totalTime += Perform(TestExpression, Iterations);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NCALCPerCalc: Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}, op/μs: {(double)Iterations * OperationsPerEvaluation / (avg * 1000):n2}");

            static double Perform(string expr, int iterations)
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    var expression = new NCalc.Expression(expr);
                    foreach (var parameter in TestParameters)
                        expression.Parameters[parameter.Key] = parameter.Value;
                    if (((double?)expression.Evaluate()) != ExpectedResult)
                        throw new Exception("Unexpected result");
                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

    }
}
