using System.Diagnostics;

namespace PerfTest
{
    internal class Program
    {
        /// <summary>
        /// Evaluations per timed run when the expression is parsed once and evaluated repeatedly.
        /// </summary>
        const int EvaluateIterations = 100000;

        /// <summary>
        /// Evaluations per timed run when the expression is parsed for every evaluation. Lower, as parsing (and for
        /// some libraries, compiling) is far slower than evaluating.
        /// </summary>
        const int ParseIterations = 1000;

        /// <summary>
        /// Each library is measured for up to this many rounds (of 3 runs each)...
        /// </summary>
        const int MaxRounds = 100;

        /// <summary>
        /// ...but stops early once it has used this much time, so that slower libraries do not take minutes.
        /// </summary>
        static readonly TimeSpan TimeBudgetPerLibrary = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Uses variables so that no parser can compute the result ahead of time (e.g. by constant folding),
        /// meaning every evaluation performs every operation.
        /// </summary>
        const string TestExpression = "10 * ((a + 1000 + ( b )) *  c) * 10";

        static readonly Dictionary<string, double> TestParameters = new() { ["a"] = 5, ["b"] = 10, ["c"] = 60.5 };

        const double ExpectedResult = 6140750;

        /// <summary>
        /// Math operations performed by each evaluation of the test expression, so that op/μs counts operations
        /// rather than evaluations. The same expression is given to every parser, so the count applies to all.
        /// </summary>
        static readonly int OperationsPerEvaluation = new NTDLS.ExpressionParser.Expression(TestExpression).OperationCount;

        /// <summary>
        /// Every library is measured two ways. Each setup is untimed and returns the function that is timed:
        ///
        /// Evaluate: the expression is parsed (or compiled) and the parameters set once, in the setup. Only
        ///     evaluation is timed - the case of one expression evaluated many times.
        ///
        /// Parse + evaluate: the setup only creates reusable objects which hold no expression (engines, contexts).
        ///     Every timed call parses the expression, sets the parameters and evaluates it - the case of many
        ///     different expressions, each evaluated once. Every cache of parsed expressions is disabled, so that
        ///     no library can skip the parse because it has seen the same text before.
        ///
        /// Each library uses its fastest form for each case.
        /// </summary>
        static readonly (string Name, Func<Func<double>> Evaluate, Func<Func<double>> ParseAndEvaluate)[] Libraries =
        [
            ("NTDLS", SetupNtdls, SetupNtdlsParse),
            ("NCalc", SetupNCalc, SetupNCalcParse),
            ("Flee", SetupFlee, SetupFleeParse),
            ("Jace", SetupJace, SetupJaceParse),
            //("mXparser", SetupMXparser, SetupMXparserParse),
            ("MathEvaluator", SetupMathEvaluator, SetupMathEvaluatorParse),
            ("xFunc", SetupXFunc, SetupXFuncParse),
            ("NoStringEvaluating", SetupNoStringEvaluating, SetupNoStringEvaluatingParse),
            ("CalcExpr.NET", SetupCalcExpr, SetupCalcExprParse),
            ("Mathos Parser", SetupMathos, SetupMathosParse),
        ];

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; //For the "μ" in op/μs.

            Console.WriteLine($"Expression: {TestExpression} with {string.Join(", ", TestParameters.Select(p => $"{p.Key}={p.Value}"))}"
                + $" ({OperationsPerEvaluation} operations per evaluation)");

            Console.WriteLine();
            Console.WriteLine($"Evaluate (parsed once) - times are in ms per {EvaluateIterations:n0} evaluations:");
            foreach (var library in Libraries)
                Measure(library.Name, library.Evaluate, EvaluateIterations, minRounds: 5);

            Console.WriteLine();
            Console.WriteLine($"Parse + evaluate (parsed every time, no caching) - times are in ms per {ParseIterations:n0} evaluations:");
            foreach (var library in Libraries)
                Measure(library.Name, library.ParseAndEvaluate, ParseIterations, minRounds: 2);
        }

        static void Measure(string name, Func<Func<double>> setup, int iterations, int minRounds)
        {
            try
            {
                var timings = new List<double>();
                var budget = Stopwatch.StartNew();

                for (int round = 0; round < MaxRounds && (round < minRounds || budget.Elapsed < TimeBudgetPerLibrary); round++)
                {
                    var totalTime = Perform(setup, iterations);
                    totalTime += Perform(setup, iterations);
                    totalTime += Perform(setup, iterations);

                    timings.Add(totalTime / 3);
                }

                double avg = timings.Average();
                double stdDev = Math.Sqrt(timings.Select(t => Math.Pow(t - avg, 2)).Average());
                double perEvaluationMicroseconds = avg * 1000 / iterations;
                Console.WriteLine($"{name,-20}: Best: {timings.Min(),9:n2}, Worst: {timings.Max(),9:n2}, Avg: {avg,9:n2}, StdDev: {stdDev,7:n2}, "
                    + $"μs/eval: {perEvaluationMicroseconds,9:n3}, op/μs: {OperationsPerEvaluation / perEvaluationMicroseconds,7:n2}, Rounds: {timings.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name,-20}: failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        static double Perform(Func<Func<double>> setup, int iterations)
        {
            var evaluate = setup();

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                double result = evaluate();
                if (Math.Abs(result - ExpectedResult) > 1e-6)
                    throw new Exception($"Unexpected result: {result}");
            }
            stopwatch.Stop();

            return stopwatch.Elapsed.TotalMilliseconds;
        }

        #region NTDLS.

        static NTDLS.ExpressionParser.Expression NewNtdls(NTDLS.ExpressionParser.ExpressionOptions? options = null)
        {
            var expression = new NTDLS.ExpressionParser.Expression(TestExpression, options);
            foreach (var parameter in TestParameters)
                expression.SetParameter(parameter.Key, parameter.Value);
            return expression;
        }

        static Func<double> SetupNtdls()
        {
            var expression = NewNtdls();
            return () => expression.Evaluate() ?? double.NaN;
        }

        static Func<double> SetupNtdlsParse()
        {
            var options = new NTDLS.ExpressionParser.ExpressionOptions { UseCompileCache = false };
            return () => NewNtdls(options).Evaluate() ?? double.NaN;
        }

        #endregion

        #region NCalc.

        static NCalc.Expression NewNCalc(NCalc.ExpressionOptions options = NCalc.ExpressionOptions.None)
        {
            var expression = new NCalc.Expression(TestExpression, options);
            foreach (var parameter in TestParameters)
                expression.Parameters[parameter.Key] = parameter.Value;
            return expression;
        }

        static Func<double> SetupNCalc()
        {
            var expression = NewNCalc();
            return () => Convert.ToDouble(expression.Evaluate());
        }

        static Func<double> SetupNCalcParse()
            => () => Convert.ToDouble(NewNCalc(NCalc.ExpressionOptions.NoCache).Evaluate());

        #endregion

        #region Flee.

        /// <summary>
        /// Flee compiles the expression to IL; it has no interpreter.
        /// </summary>
        static Func<double> SetupFlee()
        {
            var context = new Flee.PublicTypes.ExpressionContext();
            foreach (var parameter in TestParameters)
                context.Variables[parameter.Key] = parameter.Value;

            var expression = context.CompileGeneric<double>(TestExpression);
            return () => expression.Evaluate();
        }

        /// <summary>
        /// Expect ~2 ms per call: compiling emits IL (~0.3 ms), and the first Evaluate() of that IL makes the .NET JIT
        /// compile it to machine code (~1.9 ms). Flee has no interpreter, so a one-off expression always pays both.
        /// </summary>
        static Func<double> SetupFleeParse()
        {
            var context = new Flee.PublicTypes.ExpressionContext();
            return () =>
            {
                foreach (var parameter in TestParameters)
                    context.Variables[parameter.Key] = parameter.Value;
                return context.CompileGeneric<double>(TestExpression).Evaluate();
            };
        }

        #endregion

        #region Jace.

        /// <summary>
        /// Jace compiles the expression to a delegate.
        /// </summary>
        static Func<double> SetupJace()
        {
            var engine = new Jace.CalculationEngine();
            var formula = engine.Build(TestExpression);
            var variables = new Dictionary<string, double>(TestParameters);
            return () => formula(variables);
        }

        /// <summary>
        /// Jace caches built formulas by default. With the cache disabled, its compiled mode is still faster
        /// than its interpreted mode for a single evaluation.
        /// </summary>
        static Func<double> SetupJaceParse()
        {
            var engine = new Jace.CalculationEngine(new Jace.JaceOptions { CacheEnabled = false });
            return () => engine.Build(TestExpression)(new Dictionary<string, double>(TestParameters));
        }

        #endregion

        #region mXparser.

        static Func<double> SetupMXparser()
        {
            var arguments = TestParameters.Select(p => new org.mariuszgromada.math.mxparser.Argument(p.Key, p.Value)).ToArray();
            var expression = new org.mariuszgromada.math.mxparser.Expression(TestExpression, arguments);
            return () => expression.calculate();
        }

        static Func<double> SetupMXparserParse()
            => () => new org.mariuszgromada.math.mxparser.Expression(TestExpression,
                TestParameters.Select(p => new org.mariuszgromada.math.mxparser.Argument(p.Key, p.Value)).ToArray()).calculate();

        #endregion

        #region MathEvaluator.

        /// <summary>
        /// MathEvaluator compiles the expression to a delegate.
        /// </summary>
        static Func<double> SetupMathEvaluator()
        {
            var variables = new Dictionary<string, double>(TestParameters);
            var function = new MathEvaluation.MathExpression(TestExpression, new MathEvaluation.Context.DotNetStandardMathContext())
                .Compile(variables);
            return () => function(variables);
        }

        /// <summary>
        /// For a single evaluation MathEvaluator's interpreter is far faster than compiling (~3 μs vs ~230 μs).
        /// </summary>
        static Func<double> SetupMathEvaluatorParse()
        {
            var context = new MathEvaluation.Context.DotNetStandardMathContext();
            return () => new MathEvaluation.MathExpression(TestExpression, context)
                .Evaluate(new Dictionary<string, double>(TestParameters));
        }

        #endregion

        #region xFunc.

        static xFunc.Maths.Expressions.Parameters.ExpressionParameters NewXFuncParameters()
        {
            var parameters = new xFunc.Maths.Expressions.Parameters.ExpressionParameters();
            foreach (var parameter in TestParameters)
                parameters.Add(parameter.Key, parameter.Value);
            return parameters;
        }

        static Func<double> SetupXFunc()
        {
            var processor = new xFunc.Maths.Processor();
            var expression = processor.Parse(TestExpression);
            var parameters = NewXFuncParameters();
            return () => ((xFunc.Maths.Expressions.NumberValue)expression.Execute(parameters)).Number;
        }

        static Func<double> SetupXFuncParse()
        {
            var processor = new xFunc.Maths.Processor();
            return () => ((xFunc.Maths.Expressions.NumberValue)processor.Parse(TestExpression).Execute(NewXFuncParameters())).Number;
        }

        #endregion

        #region NoStringEvaluating.

        static Dictionary<string, NoStringEvaluating.Models.Values.EvaluatorValue> NewNoStringVariables()
            => TestParameters.ToDictionary(p => p.Key, p => (NoStringEvaluating.Models.Values.EvaluatorValue)p.Value);

        static Func<double> SetupNoStringEvaluating()
        {
            var facade = NoStringEvaluating.NoStringEvaluator.CreateFacade(_ => { });
            var formula = facade.FormulaParser.Parse(TestExpression);
            var variables = NewNoStringVariables();
            return () => facade.Evaluator.CalcNumber(formula, variables);
        }

        /// <summary>
        /// Parses with the FormulaParser directly, which bypasses the evaluator's formula cache.
        /// </summary>
        static Func<double> SetupNoStringEvaluatingParse()
        {
            var facade = NoStringEvaluating.NoStringEvaluator.CreateFacade(_ => { });
            return () => facade.Evaluator.CalcNumber(facade.FormulaParser.Parse(TestExpression), NewNoStringVariables());
        }

        #endregion

        #region CalcExpr.NET.

        static CalcExpr.Context.ExpressionContext NewCalcExprContext()
        {
            var context = new CalcExpr.Context.ExpressionContext();
            foreach (var parameter in TestParameters)
                context.SetVariable(parameter.Key, new CalcExpr.Expressions.Terminals.Number(parameter.Value));
            return context;
        }

        static Func<double> SetupCalcExpr()
        {
            var expression = new CalcExpr.Parsing.Parser().Parse(TestExpression);
            var context = NewCalcExprContext();
            return () => ((CalcExpr.Expressions.Terminals.Number)expression.Evaluate(context)).Value;
        }

        /// <summary>
        /// The parser caches parsed expressions by text, so its cache is cleared before every parse.
        /// </summary>
        static Func<double> SetupCalcExprParse()
        {
            var parser = new CalcExpr.Parsing.Parser();
            return () =>
            {
                parser.ClearCache();
                return ((CalcExpr.Expressions.Terminals.Number)parser.Parse(TestExpression).Evaluate(NewCalcExprContext())).Value;
            };
        }

        #endregion

        #region Mathos Parser.

        /// <summary>
        /// Mathos Parser tokenizes once, then evaluates the tokens.
        /// </summary>
        static Func<double> SetupMathos()
        {
            var parser = new Mathos.Parser.MathParser();
            foreach (var parameter in TestParameters)
                parser.LocalVariables[parameter.Key] = parameter.Value;
            var tokens = parser.GetTokens(TestExpression);
            return () => parser.Parse(tokens);
        }

        static Func<double> SetupMathosParse()
        {
            var parser = new Mathos.Parser.MathParser();
            return () =>
            {
                foreach (var parameter in TestParameters)
                    parser.LocalVariables[parameter.Key] = parameter.Value;
                return parser.Parse(TestExpression);
            };
        }

        #endregion
    }
}
