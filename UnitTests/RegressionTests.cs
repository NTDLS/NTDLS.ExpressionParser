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
        public void Constant_Errors_Are_Raised_At_Evaluation()
        {
            //Constant folding must not move errors from Evaluate() into the constructor.
            var expr = new Expression("1 / 0 + 2");
            Assert.ThrowsAny<Exception>(() => expr.Evaluate());
        }

        [Fact]
        public void Rand_Is_Not_Folded()
        {
            var expr = new Expression("rand()");
            var values = Enumerable.Range(0, 20).Select(_ => expr.Evaluate()).Distinct().Count();
            Assert.True(values > 1);
        }

        [Fact]
        public void Compiled_And_ShowWork_Agree()
        {
            var expr = new Expression("max(a, 2) * -(b + 1) >= -10 && !c || null");
            expr.SetParameter("a", 3);
            expr.SetParameter("b", 2);
            expr.SetParameter("c", (double?)null);
            Assert.Equal(expr.Evaluate(out _), expr.Evaluate());
        }

        [Fact]
        public void Deeply_Nested_Expression()
        {
            var text = string.Concat(Enumerable.Repeat("(1+", 100)) + "1" + new string(')', 100);
            Assert.Equal(101, Eval(text));
        }

        [Fact]
        public void Extremely_Nested_Expression_Does_Not_Overflow_Stack()
        {
            //Sanitizing, validating and compiling are all iterative, so nesting depth is unlimited.
            var text = new string('(', 100_000) + "1" + new string(')', 100_000);
            Assert.Equal(1, Eval(text));
        }

        [Fact]
        public void Extremely_Nested_Functions_Do_Not_Overflow_Stack()
        {
            var text = string.Concat(Enumerable.Repeat("abs(", 20_000)) + "-1" + new string(')', 20_000);
            Assert.Equal(1, Eval(text));
        }

        [Fact]
        public void ShowWork_Lists_Each_Operation_In_Order()
        {
            //Constant steps are shown, even though Evaluate() folds them away.
            var result = Expression.Evaluate("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", out string showWork);
            Assert.Equal(6140750, result);
            Assert.Equal(string.Join(Environment.NewLine,
                "{",
                "    5+1000 = 1005",
                "    1005+10 = 1015",
                "    1015*60.5 = 61407.5",
                "    10*61407.5 = 614075",
                "    614075*10 = 6140750",
                "} = 6140750",
                ""), showWork);
        }

        [Fact]
        public void ShowWork_Shows_Variables_Functions_And_Nulls()
        {
            var expr = new Expression("max(a, 2) * -(b + 1) + c");
            expr.SetParameter("a", 3);
            expr.SetParameter("b", 2);
            expr.SetParameter("c", (double?)null);
            var result = expr.Evaluate(out string showWork);

            Assert.Null(result);
            Assert.Contains("max(3,2) = 3", showWork);
            Assert.Contains("2+1 = 3", showWork);
            Assert.Contains("-3 = -3", showWork);
            Assert.Contains("3*-3 = -9", showWork);
            Assert.Contains("-9+null = null", showWork);
            Assert.EndsWith("} = null" + Environment.NewLine, showWork);
        }

        [Theory]
        [InlineData("1 + --2", 3)]
        [InlineData("1 - - 2", 3)]
        [InlineData("1 ---2", -1)]
        [InlineData("2 * -+3", -6)]
        [InlineData("--5", 5)]
        [InlineData("-(-(-5))", -5)]
        public void Consecutive_Signs_Collapse(string text, double expected)
        {
            Assert.Equal(expected, Eval(text));
            Assert.Equal(expected, new Expression(text).Evaluate(out _)); //String evaluator must agree.
        }

        [Theory]
        [InlineData("()")]
        [InlineData("1 +")]
        [InlineData("*2")]
        [InlineData("(1 +)")]
        [InlineData("!=1")]
        [InlineData("2x")]
        [InlineData("2(3)")]
        [InlineData("(1)(2)")]
        [InlineData("2max(1, 2)")]
        [InlineData("max(1,)")]
        [InlineData("max(,1)")]
        public void Malformed_Expression_Is_A_Syntax_Error(string text)
        {
            var ex = Assert.ThrowsAny<Exception>(() => new Expression(text, new ExpressionOptions { UseCompileCache = false }));
            Assert.StartsWith("Syntax error", ex.Message);
        }

        [Fact]
        public void Reserved_Placeholder_Character_Is_Rejected()
        {
            var ex = Assert.ThrowsAny<Exception>(() => new Expression("$0$ + 1"));
            Assert.Contains("'$'", ex.Message);
        }

        [Theory]
        [InlineData("10 * ((5 + 1000 + ( 10 )) *  60.5) * 10", 5)] //Counted as written, even though it folds to a constant.
        [InlineData("1", 0)]
        [InlineData("-5", 0)] //A negated literal is just a number.
        [InlineData("-(5)", 0)] //Still just the number -5.
        [InlineData("-(5 + 1)", 2)]
        [InlineData("!a && max(a, 2, 3) > -a", 5)] //!, max(), -, >, &&
        [InlineData("pi()", 1)]
        public void Operation_Count(string text, int expected)
            => Assert.Equal(expected, new Expression(text).OperationCount);

        [Fact]
        public void Parameter_Lifecycle()
        {
            var expr = new Expression("a + B");
            Assert.Throws<Exception>(() => expr.Evaluate()); //Nothing set.

            expr.SetParameter("A", 1); //Names are case insensitive.
            expr.SetParameter("b", 2);
            expr.SetParameter("unused", 100); //Harmless.
            Assert.Equal(3, expr.Evaluate());

            expr.SetParameter("a", 10); //Overwrite.
            Assert.Equal(12, expr.Evaluate());

            expr.RemoveParameter("b");
            var ex = Assert.Throws<Exception>(() => expr.Evaluate());
            Assert.Equal("Undefined variable: b", ex.Message);

            expr.RemoveParameter("b"); //Removing twice is harmless.
            expr.SetParameter("b", 5);
            Assert.Equal(15, expr.Evaluate());

            expr.ClearParameters();
            Assert.Throws<Exception>(() => expr.Evaluate());
            Assert.Throws<Exception>(() => expr.Evaluate(out _));
        }

        [Fact]
        public void Null_Parameter_Uses_DefaultNullValue()
        {
            var expr = new Expression("a + 1", new ExpressionOptions { DefaultNullValue = 4 });
            expr.SetParameter("a", (double?)null);
            Assert.Equal(5, expr.Evaluate());

            var noDefault = new Expression("a + 1");
            noDefault.SetParameter("a", (double?)null);
            Assert.Null(noDefault.Evaluate());
        }

        [Fact]
        public void Bool_And_Int_Parameters()
        {
            var expr = new Expression("a + b");
            expr.SetParameter("a", true);
            expr.SetParameter("b", 41);
            Assert.Equal(42, expr.Evaluate());
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
