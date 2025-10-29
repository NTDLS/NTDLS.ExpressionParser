using System.Diagnostics;

namespace PerfTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //NTDLS();
            //NCALC();
            NTDLSPerCalc();
            //NCALCPerCalc();
        }

        static void NTDLS()
        {
            var timings = new List<double>();

            for (int i = 0; i < 20; i++)
            {
                var totalTime = Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NTDLS       : Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}");

            static double Perform(string expr, int iterations)
            {
                var expression = new NTDLS.ExpressionParser.Expression(expr);

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    if (expression.Evaluate() != 6140750)
                        throw new Exception("Unexpected result");

                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        static void NCALC()
        {
            var timings = new List<double>();

            for (int i = 0; i < 20; i++)
            {
                var totalTime = Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NCALC       : Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}");

            static double Perform(string expr, int iterations)
            {
                var expression = new NCalc.Expression(expr);

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    if (((double?)expression.Evaluate()) != 6140750)
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
                var totalTime = Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NTDLSPerCalc: Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}");

            static double Perform(string expr, int iterations)
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    var expression = new NTDLS.ExpressionParser.Expression(expr);
                    if (expression.Evaluate() != 6140750)
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
                var totalTime = Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);
                totalTime += Perform("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 100000);

                timings.Add(totalTime / 3);
            }

            double avg = timings.Average();
            double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
            Console.WriteLine($"NCALCPerCalc: Best: {timings.Min():n2}, Worst: {timings.Max():n2}, Avg: {avg:n2}, StdDev: {stdDev:n2}");

            static double Perform(string expr, int iterations)
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    var expression = new NCalc.Expression(expr);
                    if (((double?)expression.Evaluate()) != 6140750)
                        throw new Exception("Unexpected result");
                }
                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds;
            }
        }

    }
}
