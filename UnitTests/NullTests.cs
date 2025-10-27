using NTDLS.ExpressionParser;

namespace UnitTests
{
    public class NullTests
    {

        [Fact]
        public void Function_ParamNull()
        {
            Assert.Null(Expression.Evaluate("2 * sum(1,null,3)"));
        }

        [Fact]
        public void Value_ParamNull()
        {
            Assert.Null(Expression.Evaluate("2 * null"));
        }

        [Fact]
        public void Param_ParamNull()
        {
            var exp = new Expression("2 * myParam");
            exp.SetParameter("myParam", (double?)null);
            Assert.Null(exp.Evaluate());
        }

        [Fact]
        public void Param_ParamNull_Function()
        {
            var expression = new Expression("10 * ((5 + extra + CustomSum(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10");

            expression.SetParameter("extra", 1000);

            expression.AddFunction("CustomSum", parameters =>
            {
                return null;
            });

            Assert.Null(expression.Evaluate());
        }

        [Fact]
        public void Function_ParamNull_OptNull0()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 0
            };

            Assert.Equal(8, Expression.Evaluate("2 * sum(1,null,3)", options));
        }

        [Fact]
        public void Value_ParamNull_OptNull0()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 0
            };

            Assert.Equal(0, Expression.Evaluate("2 * null", options));
        }

        [Fact]
        public void Param_ParamNull_OptNull0()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 0
            };

            var exp = new Expression("2 * myParam", options);
            exp.SetParameter("myParam", (double?)null);
            Assert.Equal(0, exp.Evaluate());
        }

        [Fact]
        public void Param_ParamNull_Function_OptNull0()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 0
            };

            var expression = new Expression("10 * ((5 + extra + CustomSum(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10", options);

            expression.SetParameter("extra", 1000);

            expression.AddFunction("CustomSum", parameters =>
            {
                return null;
            });

            Assert.Equal(5086050, expression.Evaluate());
        }

        [Fact]
        public void Function_ParamNull_OptNull1()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 1
            };

            Assert.Equal(10, Expression.Evaluate("2 * sum(1,null,3)", options));
        }

        [Fact]
        public void Value_ParamNull_OptNull1()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 1
            };

            Assert.Equal(2, Expression.Evaluate("2 * null", options));
        }

        [Fact]
        public void Param_ParamNull_OptNull1()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 1
            };

            var exp = new Expression("2 * myParam", options);
            exp.SetParameter("myParam", (double?)null);
            Assert.Equal(2, exp.Evaluate());
        }

        [Fact]
        public void Param_ParamNull_Function_OptNull1()
        {
            var options = new ExpressionOptions()
            {
                DefaultNullValue = 1
            };

            var expression = new Expression("10 * ((5 + extra + CustomSum(11,55) + ( 10 + !0 )) * Ceil(SUM(11.6, 12.5, 14.7, 11.11)) + 60.5) * 10", options);

            expression.SetParameter("extra", 1000);

            expression.AddFunction("CustomSum", parameters =>
            {
                return null;
            });

            Assert.Equal(5091050, expression.Evaluate());
        }
    }
}
