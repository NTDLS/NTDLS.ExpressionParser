using System.Numerics;
using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    internal static class Utility
    {
        internal static readonly string[] NativeFunnctions =
        {
            "not",
            "acos",
            "asin",
            "atan2",
            "pow",
            "tan",
            "sin",
            "cos",
            "atan",
            "abs",
            "sqrt",
            "modpow",
            "sinh",
            "cosh",
            "tanh",
            "log",
            "log10",
            "exp",
            "floor",
            "ceil",
            "sum",
            "avg"
        };

        internal static readonly char[] PreOrderOperations =
        {
            '!',  //Logical NOT
        };

        internal static readonly char[] FirstOrderOperations =
        {
            '~',  //Bitwise NOT
	        '*',  //Multiplication
	        '/',  //Division
	        '%'  //Modulation
        };

        internal static readonly char[] SecondOrderOperations =
        {
            '+',  //Addition
	        '-'  //Subtraction
        };

        internal static readonly string[] ThirdOrderOperations =
        {
            "<>", //Logical Not Equal
	        "|=", //Bitwise Or Equal
	        "&=", //Bitwise And Equal
	        "^=", //Bitwise XOR Equal
	        "<=", //Logical Less or Equal
	        ">=", //Logical Greater or Equal
	        "!=", //Logical Not Equal

	        "<<", //Bitwise Left Shift
	        ">>", //Bitwise Right Shift

	        "=",  //Logical Equals
	        ">",  //Logical Greater Than
	        "<",  //Logical Less Than

	        "&&", //Logical AND
	        "||", //Logical OR

	        "|",  //Bitwise OR
	        "&",  //Bitwise AND
	        "^",  //Exclusive OR
        };

        internal static bool IsNativeFunction(string value) => NativeFunnctions.Contains(value);
        internal static bool IsIntegerExclusiveOperation(string value) => (new string[] { "&", "|", "^", "&=", "|=", "^=", "<<", ">>" }).Contains(value);
        internal static bool IsMathChar(char value) => (new char[] { '*', '/', '+', '-', '>', '<', '!', '=', '&', '|', '^', '%', '~' }).Contains(value);
        internal static bool IsValidChar(char value) => Char.IsDigit(value) || IsMathChar(value) || value == '.' || value == '(' || value == ')';
        internal static bool IsValidVariableChar(char value) => Char.IsDigit(value) || (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || value == '_';
        internal static bool IsNumeric(string sText) => IsNumeric(sText, out bool _);

        internal static bool IsNumeric(ReadOnlySpan<char> sText, out bool isFloatingPoint)
        {

            int iRPos = 0;
            isFloatingPoint = false;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ComputeIntegerExclusivePrimitive(int leftValue, string operation, int rightValue)
        {
            return operation switch
            {
                "!" => (leftValue != rightValue) ? 1 : 0,
                "&" => leftValue & rightValue,
                "&&" => (leftValue != 0 && rightValue != 0) ? 1 : 0,
                "&=" => leftValue &= rightValue,
                "^" => leftValue ^ rightValue,
                "^=" => leftValue ^= rightValue,
                "|" => leftValue | rightValue,
                "||" => (leftValue != 0 || rightValue != 0) ? 1 : 0,
                "|=" => leftValue |= rightValue,
                "~" => ~leftValue,
                "<<" => leftValue << rightValue,
                "=" => (leftValue == rightValue) ? 1 : 0,
                ">>" => leftValue >> rightValue,
                _ => throw new Exception($"Invalid operator: {operation}"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double ComputePrivative(double leftValue, string operation, double rightValue)
        {
            if (IsIntegerExclusiveOperation(operation))
            {
                return ComputeIntegerExclusivePrimitive((int)leftValue, operation, (int)rightValue);
            }

            var result = operation switch
            {
                "!" => (leftValue != rightValue) ? 1 : 0,
                "!=" => (leftValue != rightValue) ? 1 : 0,
                "-" => (leftValue - rightValue),
                "%" => rightValue != 0 ? (leftValue % rightValue) : throw new Exception("Divide by zero (mod)."),
                "&&" => (leftValue != 0 && rightValue != 0) ? 1 : 0,
                "*" => (leftValue * rightValue),
                "/" => rightValue != 0 ? (leftValue / rightValue) : throw new Exception("Divide by zero."),
                "||" => (leftValue != 0 || rightValue != 0) ? 1 : 0,
                "+" => (leftValue + rightValue),
                "<" => (leftValue < rightValue) ? 1 : 0,
                "<=" => (leftValue <= rightValue) ? 1 : 0,
                "<>" => (leftValue != rightValue) ? 1 : 0,
                "=" => (leftValue == rightValue) ? 1 : 0,
                ">" => (leftValue > rightValue) ? 1 : 0,
                ">=" => (leftValue >= rightValue) ? 1 : 0,
                _ => throw new Exception($"Invalid operator: {operation}"),
            };

            if (double.IsNaN(result))
            {
                throw new Exception($"Result of {operation} is NaN.");
            }

            if (double.IsInfinity(result))
            {
                throw new Exception($"Result of {operation} is infinite.");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double ComputeNativeFunction(string functionName, double[] parameters)
        {
            return functionName switch
            {
                "abs" => parameters.Length == 1 ? (Math.Abs(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "acos" => parameters.Length == 1 ? (Math.Acos(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "asin" => parameters.Length == 1 ? (Math.Asin(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "atan" => parameters.Length == 1 ? (Math.Atan(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "atan2" => parameters.Length == 2 ? (Math.Atan2(parameters[0], parameters[1])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "avg" => parameters.Length > 0 ? (parameters.Average()) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "ceil" => parameters.Length == 1 ? (Math.Ceiling(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "cos" => parameters.Length == 1 ? (Math.Cos(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "cosh" => parameters.Length == 1 ? (Math.Cosh(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "exp" => parameters.Length == 1 ? (Math.Exp(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "floor" => parameters.Length == 1 ? (Math.Floor(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "log" => parameters.Length == 1 ? (Math.Log(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "log10" => parameters.Length == 1 ? (Math.Log10(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "max" => parameters.Length > 0 ? (parameters.Max()) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "min" => parameters.Length > 0 ? (parameters.Min()) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "modpow" => parameters.Length == 3 ? ((double)BigInteger.ModPow((BigInteger)parameters[0], (BigInteger)parameters[1], (BigInteger)parameters[2])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "not" => parameters.Length == 1 ? ((parameters[0] == 0) ? 1 : 0) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "pow" => parameters.Length == 2 ? (Math.Pow(parameters[0], (int)parameters[1])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "sin" => parameters.Length == 1 ? (Math.Sin(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "sinh" => parameters.Length == 1 ? (Math.Sinh(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "sqrt" => parameters.Length == 1 ? (Math.Sqrt(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "sum" => parameters.Length > 0 ? (parameters.Sum()) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "tanh" => parameters.Length == 1 ? (Math.Tanh(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                "tan" => parameters.Length == 1 ? (Math.Tan(parameters[0])) : throw new Exception($"Invalid number of parameters passed to function: {functionName}"),
                _ => throw new Exception($"Undefined native function: {functionName}"),
            };
        }
      
        public static double StringToDouble(ReadOnlySpan<char> span)
        {
            double result = 0.0;
            int length = span.Length;
            int i = 0;

            if (length > 0 && span[0] == '-')
            {
                i++;
            }

            for (; i < length; i++)
            {
                if ((span[i] - '0') >= 0 && (span[i] - '0') <= 9)
                {
                    result = result * 10.0 + (span[i] - '0');
                }
                else if (span[i] == '.')
                {
                    i++; //Skip the decimal point.

                    double fraction = 0.0;
                    double multiplier = 1.0;

                    for (; i < length; i++)
                    {
                        if ((span[i] - '0') >= 0 && (span[i] - '0') <= 9)
                        {
                            fraction = fraction * 10.0 + (span[i] - '0');
                            multiplier *= 0.1;
                        }
                        else throw new FormatException("Invalid character in input string.");
                    }

                    result += fraction * multiplier;
                }
                else throw new FormatException("Invalid character in input string.");
            }

            if (length > 0 && span[0] == '-')
            {
                return -result;
            }
            return result;
        }

        public static int StringToInt(ReadOnlySpan<char> span)
        {
            int result = 0;
            int length = span.Length;

            for (int i = 0; i < length; i++)
            {
                if ((span[i] - '0') >= 0 && (span[i] - '0') <= 9)
                {
                    result = result * 10 + (span[i] - '0');
                }
            }
            return result;
        }
    }
}