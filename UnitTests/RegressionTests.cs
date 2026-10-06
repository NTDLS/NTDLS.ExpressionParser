using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class RegressionTests
    {
        private static double? Eval(string expr, ExpressionOptions? options = null)
            => Expression.Evaluate(expr, options);

        [Fact]
        public void Variable_Name_Inside_Function_Name()
        {
            //Variable "a" must not be substituted inside the function name "abs".
            var expr = new Expression("abs(a)");
            expr.SetParameter("a", -3);
            Assert.Equal(3, expr.Evaluate());
        }

        [Fact]
        public void Variable_Same_Name_As_Function()
        {
            var expr = new Expression("max(max, 1)");
            expr.SetParameter("max", 5);
            Assert.Equal(5, expr.Evaluate());
        }

        [Fact]
        public void Function_Name_Prefix_Of_Another_Function()
        {
            //"e" is a prefix of "exp" - the parser must not confuse the two.
            Assert.Equal(Math.E + Math.Exp(1), Eval("e() + exp(1)"));
            Assert.Equal(Math.Exp(1) + Math.E, Eval("exp(1) + e()"));
        }

        [Fact]
        public void Zero_Parameter_Functions()
        {
            Assert.Equal(Math.PI, Eval("pi()"));
            Assert.Equal(Math.PI * 2, Eval("pi() * 2"));
        }

        [Fact]
        public void Negated_Parenthesis()
        {
            Assert.Equal(-5, Eval("-(2+3)"));
            Assert.Equal(-1, Eval("4 + -(2+3)"));
        }

        [Fact]
        public void Negated_Variable()
        {
            var expr = new Expression("-x + 1");
            expr.SetParameter("x", 3);
            Assert.Equal(-2, expr.Evaluate());
        }

        [Fact]
        public void Signed_Operand_After_Comparison()
        {
            Assert.Equal(1, Eval("2 > -1"));
            Assert.Equal(0, Eval("2 < -1"));
            Assert.Equal(1, Eval("-1 = -1"));
            Assert.Equal(1, Eval("2 >= -1"));
        }

        [Fact]
        public void Comparison_Binds_Tighter_Than_Logical()
        {
            Assert.Equal(1, Eval("1 = 2 || 3 = 3"));
            Assert.Equal(0, Eval("1 = 1 && 2 = 3"));
            Assert.Equal(1, Eval("1 < 2 && 3 > 2"));
        }

        [Fact]
        public void Fast_Parser_Decimal_Precision()
        {
            Assert.Equal(0.3, Eval("0.3"));
            Assert.Equal(123.456, Eval("123.456"));
            Assert.Equal(0.3 + 0.7, Eval("0.3 + 0.7"));
        }

        [Fact]
        public void Compile_Cache_Respects_DefaultNullValue()
        {
            Assert.Equal(1.5, Eval("null", new ExpressionOptions { DefaultNullValue = 1.5 }));
            Assert.Equal(1.2, Eval("null", new ExpressionOptions { DefaultNullValue = 1.2 }));
        }

        [Fact]
        public void Fast_Parser_Falls_Back_For_Long_Numbers()
        {
            Assert.Equal(12345678901234567890.0, Eval("12345678901234567890"));
            Assert.Equal(0.1234567890123456789, Eval("0.1234567890123456789"));
            Assert.Equal(1.5, Eval("1.50000000000000000000000000"));
        }

        [Fact]
        public void Bitwise_Not()
        {
            Assert.Equal(-6, Eval("~5"));
            Assert.Equal(0, Eval("1 + ~0"));
            Assert.Equal(5, Eval("~~5"));
        }

        [Fact]
        public void Stacked_Logical_Not()
        {
            Assert.Equal(1, Eval("!!1"));
            Assert.Equal(0, Eval("!!0"));
        }

        [Fact]
        public void Double_Equals()
        {
            Assert.Equal(1, Eval("3 == 3"));
            Assert.Equal(0, Eval("3 == 4"));
        }

        [Fact]
        public void Signed_Operand_After_Two_Char_Operator()
        {
            Assert.Equal(1, Eval("2 >= -1"));
            Assert.Equal(1, Eval("-1 <= -1"));
            Assert.Equal(1, Eval("2 != -2"));
        }

        [Fact]
        public void Bitwise_And_Shift_Precedence()
        {
            Assert.Equal(1, Eval("1 << 2 = 4")); //Shift before equality.
            Assert.Equal(0, Eval("6 & 3 = 2")); //Equality before bitwise AND (C precedence): 6 & (3 = 2).
            Assert.Equal(1, Eval("(6 & 3) = 2"));
        }

        [Fact]
        public void Negated_Group_With_Changing_Variable()
        {
            //The sign applied to a variable-derived placeholder must not be cached as a constant.
            var expr = new Expression("(-x) * 2");
            expr.SetParameter("x", 3);
            Assert.Equal(-6, expr.Evaluate());
            expr.SetParameter("x", 5);
            Assert.Equal(-10, expr.Evaluate());
        }

        [Fact]
        public void Variables_Across_Instances_Sharing_Template()
        {
            for (int i = 0; i < 5; i++)
            {
                var expr = new Expression("-(x + 1) * 2 > -y && y != 0");
                expr.SetParameter("x", i);
                expr.SetParameter("y", 3);
                Assert.Equal(-(i + 1) * 2 > -3 ? 1 : 0, expr.Evaluate());
            }
        }

        [Fact]
        public void Custom_Function_Name_Prefix()
        {
            var expr = new Expression("f(2) + foo(3)");
            expr.AddFunction("f", p => p[0] * 10);
            expr.AddFunction("foo", p => p[0] * 100);
            Assert.Equal(320, expr.Evaluate());
        }

        [Theory]
        [InlineData("a b")]
        [InlineData("2 3")]
        [InlineData("a 2")]
        [InlineData("2 a")]
        [InlineData("1 + max(a b, 1)")]
        [InlineData("ma x(1)")]
        [InlineData("1 null")]
        [InlineData("(1) 2")]
        public void Whitespace_Between_Operands_Is_Rejected(string text)
        {
            var ex = Assert.ThrowsAny<Exception>(() =>
            {
                var expr = new Expression(text, new ExpressionOptions { UseCompileCache = false });
                expr.SetParameter("a", 1);
                expr.SetParameter("b", 2);
                expr.SetParameter("ab", 3);
                expr.Evaluate();
            });
            Assert.Contains("Missing operator", ex.Message);
        }

        [Fact]
        public void Whitespace_Still_Allowed_Where_Valid()
        {
            var expr = new Expression("  sin (0) + max ( a , b )  * 2 ");
            expr.SetParameter("a", 1);
            expr.SetParameter("b", 2);
            Assert.Equal(4, expr.Evaluate());
            Assert.Equal(1, Eval("2 > - 1"));
            Assert.Equal(6, Eval("( 1 + 2 ) * 2"));
        }

        [Fact]
        public void Repeated_Static_Evaluation_Is_Stable()
        {
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(14, Eval("2+3*4"));
                Assert.Equal(-5, Eval("-(2+3)"));
            }
        }
    }
}
