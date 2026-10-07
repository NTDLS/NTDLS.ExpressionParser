using Microsoft.Extensions.Caching.Memory;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    internal static class Utility
    {
        internal static readonly MemoryCache PersistentCaches = new(new MemoryCacheOptions());

        internal static readonly string[] NativeFunctions =
        [
            "abs",
            "acos",
            "asin",
            "atan",
            "atan2",
            "avg",
            "ceil",
            "clamp",
            "cos",
            "cosh",
            "count",
            "deg",
            "e",
            "exp",
            "floor",
            "hypot",
            "if",
            "log",
            "log10",
            "logn",
            "max",
            "min",
            "modpow",
            "not",
            "pi",
            "pow",
            "prod",
            "rad",
            "rand",
            "round",
            "sign",
            "sin",
            "sinh",
            "sqrt",
            "sum",
            "tan",
            "tanh",
            "trunc"
        ];

        internal static readonly HashSet<string> NativeFunctionSet = new(NativeFunctions);

        /// <summary>
        /// Third order operations, ordered so that two-character operators are matched before their one-character prefixes.
        /// </summary>
        internal static readonly string[] ThirdOrderOperations =
        [
            "<<", //Bitwise Left Shift
            ">>", //Bitwise Right Shift
            "<=", //Logical Less or Equal
            ">=", //Logical Greater or Equal
            "<>", //Logical Not Equal
            "==", //Logical Equals
            "!=", //Logical Not Equal
            "&&", //Logical AND
            "||", //Logical OR
            "&=", //Bitwise And Equal
            "|=", //Bitwise Or Equal
            "^=", //Bitwise XOR Equal
            "<",  //Logical Less Than
            ">",  //Logical Greater Than
            "=",  //Logical Equals
            "&",  //Bitwise AND
            "|",  //Bitwise OR
            "^",  //Exclusive OR
        ];

        /// <summary>
        /// Precedence of the third order operations (mirrors C operator precedence). Lower binds tighter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ThirdOrderPrecedence(string operation) => operation switch
        {
            "<<" or ">>" => 0,
            "<" or "<=" or ">" or ">=" => 1,
            "=" or "==" or "!=" or "<>" => 2,
            "&" or "&=" => 3,
            "^" or "^=" => 4,
            "|" or "|=" => 5,
            "&&" => 6,
            "||" => 7,
            _ => throw new Exception($"Invalid operator: {operation}")
        };

        /// <summary>
        /// Returns a cached string for a single character operator so that operator discovery does not allocate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string OperatorString(char value) => value switch
        {
            '*' => "*",
            '/' => "/",
            '%' => "%",
            '+' => "+",
            '-' => "-",
            _ => value.ToString()
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNativeFunction(string value) => NativeFunctionSet.Contains(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsMathChar(char value) => value switch
        {
            '*' or '/' or '+' or '-' or '>' or '<' or '!' or '=' or '&' or '|' or '^' or '%' or '~' => true,
            _ => false
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValidVariableChar(char value) => char.IsDigit(value) || (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || value == '_';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNumeric(ReadOnlySpan<char> sText)
        {
            int iRPos = 0;
            bool isFloatingPoint = false;

            if (sText.Length == 0)
            {
                return false;
            }

            if (sText[iRPos] == '-' || sText[iRPos] == '+') //Explicit positive or negative number.
            {
                iRPos++;
            }

            for (; iRPos < sText.Length; iRPos++)
            {
                if (!char.IsDigit(sText[iRPos]))
                {
                    if (sText[iRPos] == '.')
                    {
                        if (iRPos == sText.Length - 1) //Decimal cannot be the last character.
                        {
                            return false;
                        }
                        if (iRPos == 0 || (iRPos == 1 && sText[0] == '-')) //Decimal cannot be the first character.
                        {
                            return false;
                        }

                        if (isFloatingPoint) //More than one decimal is not allowed.
                        {
                            return false;
                        }
                        isFloatingPoint = true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static readonly double[] _powersOfTen =
            [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22];

        /// <summary>
        /// Parses a numeric literal (optionally signed). Placeholders are not handled here.
        /// </summary>
        internal static double ParseNumber(ReadOnlySpan<char> span, bool useFastFloatingPointParser)
        {
            if (useFastFloatingPointParser)
            {
                int i = 0;
                bool isNegative = false;

                if (span.Length > 0 && (span[0] == '-' || span[0] == '+'))
                {
                    isNegative = span[0] == '-';
                    i++; //Skip the explicit sign.
                }

                ulong mantissa = 0;
                int significantDigits = 0;
                int fractionDigits = 0;
                bool seenDecimal = false;

                for (; i < span.Length; i++)
                {
                    int digit = span[i] - '0';
                    if ((uint)digit <= 9)
                    {
                        if (significantDigits > 0 || digit != 0)
                            significantDigits++;
                        mantissa = mantissa * 10 + (uint)digit;
                        if (seenDecimal)
                            fractionDigits++;
                    }
                    else if (span[i] == '.' && !seenDecimal)
                    {
                        seenDecimal = true;
                    }
                    else throw new FormatException("Invalid character in input string.");
                }

                //When both the mantissa and the power of ten are exactly representable, a single
                //  division yields the correctly rounded result. Otherwise fall back to the full parser.
                if (significantDigits <= 15 && fractionDigits < _powersOfTen.Length)
                {
                    double result = fractionDigits == 0 ? mantissa : mantissa / _powersOfTen[fractionDigits];
                    return isNegative ? -result : result;
                }
            }

            return double.Parse(span, System.Globalization.CultureInfo.InvariantCulture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BinaryOperator ToBinaryOperator(string operation) => operation switch
        {
            "*" => BinaryOperator.Multiply,
            "/" => BinaryOperator.Divide,
            "%" => BinaryOperator.Modulus,
            "+" => BinaryOperator.Add,
            "-" => BinaryOperator.Subtract,
            "<<" => BinaryOperator.ShiftLeft,
            ">>" => BinaryOperator.ShiftRight,
            "<" => BinaryOperator.Less,
            "<=" => BinaryOperator.LessOrEqual,
            ">" => BinaryOperator.Greater,
            ">=" => BinaryOperator.GreaterOrEqual,
            "=" => BinaryOperator.Equal,
            "==" => BinaryOperator.DoubleEqual,
            "!=" => BinaryOperator.NotEqual,
            "<>" => BinaryOperator.LessGreater,
            "&" => BinaryOperator.BitwiseAnd,
            "&=" => BinaryOperator.BitwiseAndEqual,
            "^" => BinaryOperator.BitwiseXor,
            "^=" => BinaryOperator.BitwiseXorEqual,
            "|" => BinaryOperator.BitwiseOr,
            "|=" => BinaryOperator.BitwiseOrEqual,
            "&&" => BinaryOperator.LogicalAnd,
            "||" => BinaryOperator.LogicalOr,
            _ => throw new Exception($"Invalid operator: {operation}"),
        };

        private static readonly string[] _binaryOperatorText =
            ["*", "/", "%", "+", "-", "<<", ">>", "<", "<=", ">", ">=", "=", "==", "!=", "<>", "&", "&=", "^", "^=", "|", "|=", "&&", "||"];

        internal static string ToText(BinaryOperator operation) => _binaryOperatorText[(int)operation];

        internal static double ComputeBinary(double leftValue, BinaryOperator operation, double rightValue)
        {
            double result;

            switch (operation)
            {
                //Integer exclusive operations.
                case BinaryOperator.BitwiseAnd:
                case BinaryOperator.BitwiseAndEqual: return (int)leftValue & (int)rightValue;
                case BinaryOperator.BitwiseXor:
                case BinaryOperator.BitwiseXorEqual: return (int)leftValue ^ (int)rightValue;
                case BinaryOperator.BitwiseOr:
                case BinaryOperator.BitwiseOrEqual: return (int)leftValue | (int)rightValue;
                case BinaryOperator.ShiftLeft: return (int)leftValue << (int)rightValue;
                case BinaryOperator.ShiftRight: return (int)leftValue >> (int)rightValue;

                case BinaryOperator.Multiply: result = leftValue * rightValue; break;
                case BinaryOperator.Divide: result = rightValue != 0 ? (leftValue / rightValue) : throw new Exception("Divide by zero."); break;
                case BinaryOperator.Modulus: result = rightValue != 0 ? (leftValue % rightValue) : throw new Exception("Divide by zero (mod)."); break;
                case BinaryOperator.Add: result = leftValue + rightValue; break;
                case BinaryOperator.Subtract: result = leftValue - rightValue; break;
                case BinaryOperator.Less: return (leftValue < rightValue) ? 1 : 0;
                case BinaryOperator.LessOrEqual: return (leftValue <= rightValue) ? 1 : 0;
                case BinaryOperator.Greater: return (leftValue > rightValue) ? 1 : 0;
                case BinaryOperator.GreaterOrEqual: return (leftValue >= rightValue) ? 1 : 0;
                case BinaryOperator.Equal:
                case BinaryOperator.DoubleEqual: return (leftValue == rightValue) ? 1 : 0;
                case BinaryOperator.NotEqual:
                case BinaryOperator.LessGreater: return (leftValue != rightValue) ? 1 : 0;
                case BinaryOperator.LogicalAnd: return (leftValue != 0 && rightValue != 0) ? 1 : 0;
                case BinaryOperator.LogicalOr: return (leftValue != 0 || rightValue != 0) ? 1 : 0;
                default: throw new Exception($"Invalid operator: {operation}");
            }

            if (double.IsNaN(result))
            {
                throw new Exception($"Result of {_binaryOperatorText[(int)operation]} is NaN.");
            }

            if (double.IsInfinity(result))
            {
                throw new Exception($"Result of {_binaryOperatorText[(int)operation]} is infinite.");
            }

            return result;
        }

        /// <summary>
        /// Returns false for native functions whose result can differ between calls with the same parameters.
        /// </summary>
        internal static bool IsDeterministicNativeFunction(string functionName) => functionName != "rand";

        internal static double ComputeNativeFunction(string functionName, ReadOnlySpan<double> parameters)
        {
            return functionName switch
            {
                "abs" => parameters.Length == 1 ? (Math.Abs(parameters[0])) : throw InvalidParameterCount(functionName),
                "acos" => parameters.Length == 1 ? (Math.Acos(parameters[0])) : throw InvalidParameterCount(functionName),
                "asin" => parameters.Length == 1 ? (Math.Asin(parameters[0])) : throw InvalidParameterCount(functionName),
                "atan" => parameters.Length == 1 ? (Math.Atan(parameters[0])) : throw InvalidParameterCount(functionName),
                "atan2" => parameters.Length == 2 ? (Math.Atan2(parameters[0], parameters[1])) : throw InvalidParameterCount(functionName),
                "avg" => parameters.Length > 0 ? Sum(parameters) / parameters.Length : throw InvalidParameterCount(functionName),
                "ceil" => parameters.Length == 1 ? (Math.Ceiling(parameters[0])) : throw InvalidParameterCount(functionName),
                "clamp" => parameters.Length == 3 ? Math.Min(Math.Max(parameters[0], parameters[1]), parameters[2]) : throw InvalidParameterCount(functionName),
                "cos" => parameters.Length == 1 ? (Math.Cos(parameters[0])) : throw InvalidParameterCount(functionName),
                "cosh" => parameters.Length == 1 ? (Math.Cosh(parameters[0])) : throw InvalidParameterCount(functionName),
                "count" => parameters.Length,
                "deg" => parameters.Length == 1 ? parameters[0] * 180.0 / Math.PI : throw InvalidParameterCount(functionName),
                "e" => parameters.Length == 0 ? Math.E : throw InvalidParameterCount(functionName),
                "exp" => parameters.Length == 1 ? (Math.Exp(parameters[0])) : throw InvalidParameterCount(functionName),
                "floor" => parameters.Length == 1 ? (Math.Floor(parameters[0])) : throw InvalidParameterCount(functionName),
                "hypot" => parameters.Length > 0 ? Math.Sqrt(SumOfSquares(parameters)) : throw InvalidParameterCount(functionName),
                "if" => parameters.Length == 3 ? parameters[0] != 0 ? parameters[1] : parameters[2] : throw InvalidParameterCount(functionName),
                "log" => parameters.Length == 1 ? (Math.Log(parameters[0])) : throw InvalidParameterCount(functionName),
                "log10" => parameters.Length == 1 ? (Math.Log10(parameters[0])) : throw InvalidParameterCount(functionName),
                "logn" => parameters.Length == 2 ? Math.Log(parameters[0], parameters[1]) : throw InvalidParameterCount(functionName),
                "max" => parameters.Length > 0 ? Max(parameters) : throw InvalidParameterCount(functionName),
                "min" => parameters.Length > 0 ? Min(parameters) : throw InvalidParameterCount(functionName),
                "modpow" => parameters.Length == 3 ? ((double)BigInteger.ModPow((BigInteger)parameters[0], (BigInteger)parameters[1], (BigInteger)parameters[2])) : throw InvalidParameterCount(functionName),
                "not" => parameters.Length == 1 ? ((parameters[0] == 0) ? 1 : 0) : throw InvalidParameterCount(functionName),
                "pi" => parameters.Length == 0 ? Math.PI : throw InvalidParameterCount(functionName),
                "pow" => parameters.Length == 2 ? (Math.Pow(parameters[0], parameters[1])) : throw InvalidParameterCount(functionName),
                "prod" => parameters.Length > 0 ? Product(parameters) : throw InvalidParameterCount(functionName),
                "rad" => parameters.Length == 1 ? parameters[0] * Math.PI / 180.0 : throw InvalidParameterCount(functionName),
                "rand" => parameters.Length == 0 ? Random.Shared.NextDouble() : throw InvalidParameterCount(functionName),
                "round" => (parameters.Length == 1 || parameters.Length == 2) ? parameters.Length == 1 ? Math.Round(parameters[0]) : Math.Round(parameters[0], (int)parameters[1]) : throw InvalidParameterCount(functionName),
                "sign" => parameters.Length == 1 ? Math.Sign(parameters[0]) : throw InvalidParameterCount(functionName),
                "sin" => parameters.Length == 1 ? (Math.Sin(parameters[0])) : throw InvalidParameterCount(functionName),
                "sinh" => parameters.Length == 1 ? (Math.Sinh(parameters[0])) : throw InvalidParameterCount(functionName),
                "sqrt" => parameters.Length == 1 ? (Math.Sqrt(parameters[0])) : throw InvalidParameterCount(functionName),
                "sum" => parameters.Length > 0 ? Sum(parameters) : throw InvalidParameterCount(functionName),
                "tan" => parameters.Length == 1 ? (Math.Tan(parameters[0])) : throw InvalidParameterCount(functionName),
                "tanh" => parameters.Length == 1 ? (Math.Tanh(parameters[0])) : throw InvalidParameterCount(functionName),
                "trunc" => parameters.Length == 1 ? Math.Truncate(parameters[0]) : throw InvalidParameterCount(functionName),
                _ => throw new Exception($"Undefined native function: {functionName}"),
            };
        }

        private static Exception InvalidParameterCount(string functionName)
            => new($"Invalid number of parameters passed to function: {functionName}");

        private static double Sum(ReadOnlySpan<double> values)
        {
            double sum = 0;
            foreach (var value in values) sum += value;
            return sum;
        }

        private static double SumOfSquares(ReadOnlySpan<double> values)
        {
            double sum = 0;
            foreach (var value in values) sum += value * value;
            return sum;
        }

        private static double Product(ReadOnlySpan<double> values)
        {
            double product = 1.0;
            foreach (var value in values) product *= value;
            return product;
        }

        private static double Max(ReadOnlySpan<double> values)
        {
            double max = values[0];
            for (int i = 1; i < values.Length; i++) max = Math.Max(max, values[i]);
            return max;
        }

        private static double Min(ReadOnlySpan<double> values)
        {
            double min = values[0];
            for (int i = 1; i < values.Length; i++) min = Math.Min(min, values[i]);
            return min;
        }
    }
}
