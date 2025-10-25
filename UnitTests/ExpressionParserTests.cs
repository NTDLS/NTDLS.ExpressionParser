using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class ExpressionParserTests
    {
        private static double Eval(string expr)
            => Expression.Evaluate(expr);

        private static double Eval(string expr, out string showWork)
            => Expression.Evaluate(expr, out showWork);

        [Fact]
        public void Basic_Addition()
            => Assert.Equal(7, Eval("3+4"));

        [Fact]
        public void Basic_Subtraction()
            => Assert.Equal(5, Eval("8-3"));

        [Fact]
        public void Basic_Multiplication()
            => Assert.Equal(24, Eval("8*3"));

        [Fact]
        public void Basic_Division()
            => Assert.Equal(2, Eval("8/4"));

        [Fact]
        public void Operator_Precedence()
            => Assert.Equal(14, Eval("2+3*4"));

        [Fact]
        public void Parenthesis_Priority()
            => Assert.Equal(20, Eval("(2+3)*4"));

        [Fact]
        public void Nested_Parentheses()
            => Assert.Equal(17, Eval("(2+(3*(4+1)))"));

        [Fact]
        public void Logical_AND_OR()
        {
            Assert.Equal(1, Eval("(1 && 1)"));
            Assert.Equal(0, Eval("(1 && 0)"));
            Assert.Equal(1, Eval("(1 || 0)"));
        }

        [Fact]
        public void Logical_NOT()
        {
            Assert.Equal(1, Eval("!0"));
            Assert.Equal(0, Eval("!1"));
        }

        [Fact]
        public void Comparison_Operators()
        {
            Assert.Equal(1, Eval("3 > 2"));
            Assert.Equal(0, Eval("3 < 2"));
            Assert.Equal(1, Eval("3 >= 3"));
            Assert.Equal(1, Eval("3 <= 3"));
            Assert.Equal(1, Eval("3 = 3"));
            Assert.Equal(1, Eval("3 != 4"));
            Assert.Equal(0, Eval("3 <> 3"));
        }

        [Fact]
        public void Bitwise_Operators()
        {
            Assert.Equal(6, Eval("2 | 4"));  // 010 | 100 = 110
            Assert.Equal(0, Eval("2 & 4"));  // 010 & 100 = 000
            Assert.Equal(6, Eval("2 ^ 4"));  // 010 ^ 100 = 110
            Assert.Equal(8, Eval("4 << 1"));
            Assert.Equal(2, Eval("4 >> 1"));
        }

        [Fact]
        public void Modulus_Operator()
            => Assert.Equal(1, Eval("7 % 3"));

        [Fact]
        public void Complex_Expression()
            => Assert.Equal(17, Eval("2 + 3 * (4 - 1 + (2))"));

        [Fact]
        public void Explicit_Unary()
            => Assert.Equal(-49.5, Eval("+10+-5++7*-8-+9/-6++4+-4/+1"));

        [Fact]
        public void Explicit_Unary_Oper()
            => Assert.Equal(-52.5, Eval("+10+-5+(+7*-8)+9/-6++4+-4/+1"));

        [Fact]
        public void Nested_Functions()
        {
            Assert.Equal(Math.Sqrt(Math.Sin(1) * Math.Sin(1) + Math.Cos(1) * Math.Cos(1)),
                         Eval("sqrt(sin(1)*sin(1)+cos(1)*cos(1))"));
        }

        [Fact]
        public void Builtin_Functions()
        {
            Assert.Equal(Math.Sin(0), Eval("sin(0)"));
            Assert.Equal(Math.Cos(Math.PI), Eval("cos(3.1415926535)"), 5);
            Assert.Equal(Math.Pow(2, 10), Eval("pow(2,10)"));
            Assert.Equal(Math.Abs(-5), Eval("abs(-5)"));
            Assert.Equal(Math.Ceiling(3.4), Eval("ceil(3.4)"));
            Assert.Equal(Math.Floor(3.9), Eval("floor(3.9)"));
            Assert.Equal(Math.Sqrt(16), Eval("sqrt(16)"));
            Assert.Equal(Math.Log(100), Eval("log(100)"));
            Assert.Equal(Math.Log10(1000), Eval("log10(1000)"));
        }

        [Fact]
        public void Multi_Parameter_Functions()
        {
            Assert.Equal(3.0, Eval("avg(1,2,3,4,5)"));
            Assert.Equal(15.0, Eval("sum(1,2,3,4,5)"));
        }

        [Fact]
        public void Custom_Function()
        {
            var exp = new Expression("doubleIt(4)");
            exp.AddFunction("doubleIt", p => p[0] * 2);
            Assert.Equal(8, exp.Evaluate());
        }

        [Fact]
        public void Custom_Variable()
        {
            var exp = new Expression("a*b + c");
            exp.AddParameter("a", 2);
            exp.AddParameter("b", 3);
            exp.AddParameter("c", 4);
            Assert.Equal(10, exp.Evaluate());
        }

        [Fact]
        public void Division_By_Zero_Throws()
        {
            Assert.Throws<Exception>(() => Eval("10/0"));
        }

        [Fact]
        public void Mod_By_Zero_Throws()
        {
            Assert.Throws<Exception>(() => Eval("10%0"));
        }

        [Fact]
        public void Undefined_Variable_Throws()
        {
            var exp = new Expression("x+1");
            Assert.Throws<Exception>(() => exp.Evaluate());
        }

        [Fact]
        public void Undefined_Function_Throws()
        {
            Assert.Throws<Exception>(() => Eval("foobar(1)"));
        }

        [Fact]
        public void Complex_Combined_Logic()
        {
            // ((3+5)*2 > 10) && (sqrt(16)=4)
            Assert.Equal(1, Eval("((3+5)*2 > 10) && (sqrt(16)=4)"));
        }

        [Fact]
        public void ShowWork_Output_Includes_Each_Step()
        {
            double result = Eval("(2+3)*4", out string showWork);
            Assert.Contains("2+3", showWork);
            Assert.Contains("= 5", showWork);
            Assert.Contains("}", showWork);
            Assert.Equal(20, result);
        }

        [Fact]
        public void Chained_Multiplication_Addition_Precision()
            => Assert.Equal(26, Eval("2 + 3 * 4 * 2"));

        [Fact]
        public void Mixed_Signs_Precedence()
            => Assert.Equal(-1, Eval("1 - 2 + 0"));

        [Fact]
        public void Nested_Unary_Mix()
            => Assert.Equal(-9, Eval("( -10 + -1 * -1 )"));

        [Fact]
        public void Right_Associative_Exponentiation()
            => Assert.Equal(Math.Pow(2, Math.Pow(3, 2)), Eval("pow(2,pow(3,2))")); // 2^(3^2)=512

        [Fact]
        public void Chained_Modulus()
            => Assert.Equal(1, Eval("19 % 5 % 3")); // left-associative

        [Fact]
        public void Nested_Unary_In_Parentheses()
            => Assert.Equal(7, Eval("(+10 - -3 - +6)"));

        [Fact]
        public void Consecutive_Signs_Chaos()
            => Assert.Throws<Exception>(() => Eval("+-++---8"));

        [Fact]
        public void Multiple_Parentheses_Layers()
            => Assert.Equal(5, Eval("((((((((5))))))))"));

        [Fact]
        public void Function_Composition()
            => Assert.Equal(Math.Abs(Math.Sin(-3.14)), Eval("abs(sin(-3.14))"));

        [Fact]
        public void Logarithmic_Roundtrip()
        {
            var result = Eval("exp(log(100))");
            Assert.True(Math.Abs(result - 100) < 1e-10);
        }

        [Fact]
        public void Average_Of_Sum()
            => Assert.Equal(6, Eval("avg(sum(1,2,3))"));

        [Fact]
        public void Missing_Closing_Parenthesis_Throws()
            => Assert.Throws<Exception>(() => Eval("(2+3*4"));

        [Fact]
        public void Invalid_Character_Throws()
            => Assert.Throws<Exception>(() => Eval("2 + 3a"));

        [Fact]
        public void Double_Decimal_Throws()
            => Assert.Throws<Exception>(() => Eval("1.2.3"));

        [Fact]
        public void Trig_Identity()
        {
            var result = Eval("pow(sin(1), 2) + pow(cos(1), 2)");
            Assert.True(Math.Abs(result - 1.0) < 1e-10);
        }

        [Fact]
        public void Hyperbolic_Identity()
        {
            var result = Eval("cosh(0)^2 - sinh(0)^2");
            Assert.True(Math.Abs(result - 1.0) < 1e-10);
        }

        [Fact]
        public void Nested_Sqrt_And_Pow()
            => Assert.Equal(16, Eval("pow(sqrt(16),2)"));

        [Fact]
        public void Left_Associative_Subtraction()
    => Assert.Equal(-4, Eval("10 - 5 - 9 - 0"));

        [Fact]
        public void Left_Associative_Division()
        {
            var result = Eval("100 / 10 / 2");
            Assert.Equal(5, result); // (100/10)=10, 10/2=5
        }

        [Fact]
        public void Large_Number_Pow()
        {
            var result = Eval("pow(10,6)");
            Assert.Equal(1_000_000, result);
        }

        [Fact]
        public void Small_Fraction_Division()
        {
            var result = Eval("1/1000");
            Assert.True(result > 0 && result < 0.002);
        }
    }
}
