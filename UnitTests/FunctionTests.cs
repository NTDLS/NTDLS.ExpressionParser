using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class FunctionTests
    {
        private static double Eval(string expr)
            => Expression.Evaluate(expr);

        // === Built-in Function Specific Tests =======================================

        [Fact]
        public void Func_Not()
            => Assert.Equal(1, Eval("not(0)"));

        [Fact]
        public void Func_Acos()
            => Assert.Equal(Math.Acos(0.5), Eval("acos(0.5)"), 5);

        [Fact]
        public void Func_Asin()
            => Assert.Equal(Math.Asin(0.5), Eval("asin(0.5)"), 5);

        [Fact]
        public void Func_Atan2()
            => Assert.Equal(Math.Atan2(1, 1), Eval("atan2(1,1)"), 5);

        [Fact]
        public void Func_Pow()
            => Assert.Equal(Math.Pow(3, 4), Eval("pow(3,4)"));

        [Fact]
        public void Func_Tan()
            => Assert.Equal(Math.Tan(1), Eval("tan(1)"), 5);

        [Fact]
        public void Func_Sin()
            => Assert.Equal(Math.Sin(1), Eval("sin(1)"), 5);

        [Fact]
        public void Func_Cos()
            => Assert.Equal(Math.Cos(1), Eval("cos(1)"), 5);

        [Fact]
        public void Func_Atan()
            => Assert.Equal(Math.Atan(1), Eval("atan(1)"), 5);

        [Fact]
        public void Func_Abs()
            => Assert.Equal(Math.Abs(-123.45), Eval("abs(-123.45)"));

        [Fact]
        public void Func_Sqrt()
            => Assert.Equal(Math.Sqrt(81), Eval("sqrt(81)"));

        [Fact]
        public void Func_ModPow()
            => Assert.Equal(
                (double)System.Numerics.BigInteger.ModPow(5, 3, 13),
                Eval("modpow(5,3,13)")
            );

        [Fact]
        public void Func_Sinh()
            => Assert.Equal(Math.Sinh(1.2), Eval("sinh(1.2)"), 5);

        [Fact]
        public void Func_Cosh()
            => Assert.Equal(Math.Cosh(1.2), Eval("cosh(1.2)"), 5);

        [Fact]
        public void Func_Tanh()
            => Assert.Equal(Math.Tanh(1.2), Eval("tanh(1.2)"), 2);

        [Fact]
        public void Func_Log()
            => Assert.Equal(Math.Log(10), Eval("log(10)"), 5);

        [Fact]
        public void Func_Log10()
            => Assert.Equal(Math.Log10(1000), Eval("log10(1000)"), 5);

        [Fact]
        public void Func_Exp()
            => Assert.Equal(Math.Exp(2), Eval("exp(2)"), 5);

        [Fact]
        public void Func_Floor()
            => Assert.Equal(Math.Floor(5.9), Eval("floor(5.9)"));

        [Fact]
        public void Func_Ceil()
            => Assert.Equal(Math.Ceiling(5.1), Eval("ceil(5.1)"));

        [Fact]
        public void Func_Sum()
            => Assert.Equal(10, Eval("sum(1,2,3,4)"));

        [Fact]
        public void Func_Avg()
            => Assert.Equal(2.5, Eval("avg(1,2,3,4)"));

        // === Mixed & Nested Function Checks ========================================

        [Fact]
        public void Nested_Trigonometric_Identity()
        {
            // sin^2(x) + cos^2(x) == 1
            double expected = Math.Pow(Math.Sin(0.75), 2) + Math.Pow(Math.Cos(0.75), 2);
            Assert.Equal(expected, Eval("sin(0.75)*sin(0.75)+cos(0.75)*cos(0.75)"), 5);
        }

        [Fact]
        public void Complex_Chained_Functions()
        {
            // floor(log10(pow(10,3))) == 3
            Assert.Equal(3, Eval("floor(log10(pow(10,3)))"));
        }

        [Fact]
        public void Exponential_Logarithmic_Roundtrip()
        {
            // log(exp(x)) ≈ x
            double x = 2.25;
            double expected = Math.Log(Math.Exp(x));
            Assert.Equal(expected, Eval($"log(exp({x}))"), 5);
        }

        [Fact]
        public void Hyperbolic_Identity()
        {
            // cosh^2(x) - sinh^2(x) == 1
            double x = 1.5;
            double expected = Math.Pow(Math.Cosh(x), 2) - Math.Pow(Math.Sinh(x), 2);
            Assert.Equal(expected, Eval($"cosh({x})*cosh({x}) - sinh({x})*sinh({x})"), 5);
        }

        [Fact]
        public void Mix_Sum_And_Avg()
            => Assert.Equal(3, Eval("sum(1,2,3) / avg(1,2,3)"));
    }
}
