﻿using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        internal static string ReplaceRange(string original, int startIndex, int endIndex, string replacement)
        {
            if (startIndex < 0 || startIndex >= original.Length || endIndex < 0 || endIndex >= original.Length)
            {
                throw new ArgumentOutOfRangeException("startIndex and endIndex must be within the valid range of the string.");
            }

            return original.Substring(0, startIndex) + replacement + original.Substring(endIndex + 1);
        }

        internal static bool IsIntegerExclusive(string value) => (new string[] { "&", "|", "^", "&=", "|=", "^=", "<<", ">>" }).Contains(value);
        internal static bool IsMathChar(char value) => (new char[] { '*', '/', '+', '-', '>', '<', '!', '=', '&', '|', '^', '%', '~' }).Contains(value);
        internal static bool IsValidChar(char value) => Char.IsNumber(value) || IsMathChar(value) || value == '.' || value == '(' || value == ')';
        internal static bool IsValidVariableChar(char value) => Char.IsNumber(value) || (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || value == '_';
        internal static bool IsNumeric(string sText) => IsNumeric(sText, out bool _);

        internal static bool IsNumeric(string sText, out bool isFloatingPoint)
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
                if (!char.IsNumber(sText[iRPos]))
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

        internal static int ComputeIntegerExclusivePrimative(int leftValue, string operation, int rightValue)
        {
            switch (operation)
            {
                case "&&":
                    return (leftValue != 0 && rightValue != 0) ? 1 : 0;
                case "||":
                    return (leftValue != 0 || rightValue != 0) ? 1 : 0;
                case "&":
                    return leftValue & rightValue;
                case "|":
                    return leftValue | rightValue;
                case "!":
                    return (leftValue != rightValue) ? 1 : 0;
                case "=":
                    return (leftValue == rightValue) ? 1 : 0;
                case "~":
                    return ~leftValue;
                case "^":
                    return leftValue ^ rightValue;
                case "&=":
                    return leftValue &= rightValue;
                case "|=":
                    return leftValue |= rightValue;
                case "^=":
                    return leftValue ^= rightValue;
                case "<<":
                    return leftValue << rightValue;
                case ">>":
                    return leftValue >> rightValue;
            }
            throw new Exception($"Invalid operator: {operation}");
        }

        internal static double ComputePrimative(double leftValue, string operation, double rightValue)
        {
            if (IsIntegerExclusive(operation))
            {
                return ComputeIntegerExclusivePrimative((int)leftValue, operation, (int)rightValue);
            }

            double result;

            switch (operation)
            {
                case "*":
                    result = (leftValue * rightValue);
                    break;
                case "/":
                    if (rightValue == 0)
                    {
                        throw new Exception("Divide by zero.");
                    }
                    result = (leftValue / rightValue);
                    break;
                case "+":
                    result = (leftValue + rightValue);
                    break;
                case "&&":
                    result = (leftValue != 0 && rightValue != 0) ? 1 : 0;
                    break;
                case "||":
                    result = (leftValue != 0 || rightValue != 0) ? 1 : 0;
                    break;
                case "-":
                    result = (leftValue - rightValue);
                    break;
                case "=":
                    result = (leftValue == rightValue) ? 1 : 0;
                    break;
                case ">":
                    result = (leftValue > rightValue) ? 1 : 0;
                    break;
                case "<":
                    result = (leftValue < rightValue) ? 1 : 0;
                    break;
                case ">=":
                    result = (leftValue >= rightValue) ? 1 : 0;
                    break;
                case "<=":
                    result = (leftValue <= rightValue) ? 1 : 0;
                    break;
                case "<>":
                    result = (leftValue != rightValue) ? 1 : 0;
                    break;
                case "!":
                    result = (leftValue != rightValue) ? 1 : 0;
                    break;
                case "!=":
                    result = (leftValue != rightValue) ? 1 : 0;
                    break;
                case "%":
                    if (rightValue == 0)
                    {
                        throw new Exception("Mod by zero.");
                    }
                    result = leftValue % rightValue;
                    break;
                default:
                    throw new Exception("Invalid operator: {sOpr}");
            }

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

        internal static double ComputeNativeFunction(string functionName, double[] parameters)
        {
            switch (functionName)
            {
                case "not":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return (parameters[0] == 0) ? 1 : 0;
                case "acos":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Acos(parameters[0]);
                case "asin":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Asin(parameters[0]);
                case "atan2":
                    if (parameters.Length != 2) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Atan2(parameters[0], parameters[1]);
                case "pow":
                    if (parameters.Length != 2) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Pow(parameters[0], (int)parameters[1]);
                case "tan":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Tan(parameters[0]);
                case "sin":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Sin(parameters[0]);
                case "cos":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Cos(parameters[0]);
                case "atan":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Atan(parameters[0]);
                case "abs":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Abs(parameters[0]);
                case "sqrt":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Sqrt(parameters[0]);
                case "modpow":
                    if (parameters.Length != 3) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return (double)BigInteger.ModPow((BigInteger)parameters[0], (BigInteger)parameters[1], (BigInteger)parameters[2]);
                case "sinh":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Sinh(parameters[0]);
                case "cosh":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Cosh(parameters[0]);
                case "tahn":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Tanh(parameters[0]);
                case "log":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Log(parameters[0]);
                case "log10":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Log10(parameters[0]);
                case "exp":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Exp(parameters[0]);
                case "floor":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Floor(parameters[0]);
                case "ceil":
                    if (parameters.Length != 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return Math.Ceiling(parameters[0]);
                case "sum":
                    if (parameters.Length < 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return parameters.Sum(o => o);
                case "avg":
                    if (parameters.Length < 1) throw new Exception($"Invalid number of parameters passed to function: {functionName}");
                    return parameters.Average(o => o);
                default:
                    throw new Exception($"Undefiend native function: {functionName}");
            }
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
    }
}
