﻿namespace NTDLS.ExpressionParser
{
    internal class SubExpression
    {
        private Expression _parentExpression;
        public string Text { get; internal set; }

        public SubExpression(Expression parentExpression, string text)
        {
            _parentExpression = parentExpression;
            Text = text;
        }

        private int GetStartingIndexOfLastFunctionCall(out string foundFunction)
        {
            int foundIndex = -1;

            foundFunction = string.Empty;

            foreach (var function in _parentExpression.DiscoveredFunctions)
            {
                int index = Text.LastIndexOf(function);
                if (index >= 0 && index > foundIndex)
                {
                    foundIndex = index;
                    foundFunction = function;
                }
            }

            return foundIndex;
        }

        private bool ProcessFunctionCall()
        {
            string foundFunction;

            int funcitonStartIndex = GetStartingIndexOfLastFunctionCall(out foundFunction);
            int funcitonEndIndex;

            if (funcitonStartIndex >= 0)
            {
                string buffer = string.Empty;
                int scope = 0;

                List<double> parameters = new();

                int i = funcitonStartIndex + foundFunction.Length; //Sikp the function name.

                for (; i < Text.Length; i++)
                {
                    if (Text[i] == ',')
                    {
                        var subExpression = new SubExpression(_parentExpression, buffer);
                        subExpression.Compute();
                        parameters.Add(double.Parse(subExpression.Text));
                        buffer = string.Empty;
                    }
                    else if (Text[i] == '{')
                    {
                        scope++;
                    }
                    else if (Text[i] == '}')
                    {
                        scope--;

                        if (scope != 0)
                        {
                            throw new Exception("Unexpected function nesting.");
                        }

                        var subExpression = new SubExpression(_parentExpression, buffer);
                        subExpression.Compute();
                        parameters.Add(double.Parse(subExpression.Text));

                        break;
                    }
                    else
                    {
                        buffer += Text[i];
                    }
                }

                funcitonEndIndex = i;

                if (Utility.IsNativeFunction(foundFunction))
                {
                    double functionResult = Utility.ComputeNativeFunction(foundFunction, parameters.ToArray());
                    ReplaceRange(funcitonStartIndex, funcitonEndIndex, functionResult);
                }
                else
                {
                    if (_parentExpression.CustomFunctions.TryGetValue(foundFunction, out var customFunction))
                    {
                        double functionResult = customFunction.Invoke(parameters.ToArray());
                        ReplaceRange(funcitonStartIndex, funcitonEndIndex, functionResult);
                    }
                    else
                    {
                        throw new Exception($"Undefined function: {foundFunction}");
                    }
                }

                return true;
            }

            return false;
        }

        internal string Compute()
        {
            TruncateParenthesises();

            while (true)
            {
                int operatorIndex;
                string operation;

                //Process all function calls from right-to-left.
                while (ProcessFunctionCall()) { }

                //Pre-first-order:
                while ((operatorIndex = GetFreestandingNotOperation(out operation)) > 0)
                {
                    double rightValue = GetRightValue(operatorIndex + 1, out int outParsedLength);
                    int notResult = (rightValue == 0) ? 1 : 0;
                    ReplaceRange(operatorIndex, operatorIndex + outParsedLength, notResult);
                }

                //First order operations:
                operatorIndex = GetIndexOfOperation(Utility.FirstOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrimative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                //Second order operations:
                operatorIndex = GetIndexOfOperation(Utility.SecondOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrimative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                //Third order operations:
                operatorIndex = GetIndexOfOperation(Utility.ThirdOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrimative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                break;
            }

            return Text; //This should be a number at this point, we are going to defer validating/parsing it for performance reasons.
        }

        internal void ReplaceRange(int startIndex, int endIndex, double value)
        {
            Text = Utility.ReplaceRange(Text, startIndex, endIndex, value.ToString());
        }

        /// <summary>
        /// Removes leading and trailing parenthesises, if they exist.
        /// </summary>
        internal void TruncateParenthesises()
        {
            while (Text.StartsWith('(') && Text.EndsWith(')'))
            {
                Text = Text.Substring(1, Text.Length - 2);
            }
        }

        /// <summary>
        /// Gets the numbers to the left and right of an operator.
        /// </summary>
        /// <param name="expresion">The expression</param>
        /// <param name="operation">The operator that we are parsing.</param>
        /// <param name="operationBeginIndex">The index of the first character of the operator we are parsing for</param>
        /// <param name="leftValue">Output: the parsed left-hand value</param>
        /// <param name="rightValue">Output: the parsed right-hand value</param>
        /// <param name="beginPosition">Output: the beginning index of the left-hand value</param>
        /// <param name="endPosition">Output: the ending index of the right-hand value</param>
        private void GetLeftAndRightValues(string operation,
            int operationBeginIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition)
        {
            leftValue = GetLeftValue(operationBeginIndex, out int leftParsedLength);
            rightValue = GetRightValue(operationBeginIndex + operation.Length, out int rightParsedLength);

            beginPosition = operationBeginIndex - leftParsedLength;
            endPosition = operationBeginIndex + rightParsedLength + (operation.Length - 1);
        }

        private double GetLeftValue(int operationIndex, out int outParsedLength)
        {
            string buffer = string.Empty;

            for (int i = operationIndex - 1; i > -1; i--)
            {
                if (char.IsNumber(Text[i]) || Text[i] == '.')
                {
                    if (Text[i] == '-' || Text[i] == '+')
                    {
                        if (i == 0) //The first character is a + or -, this is a valid explicit positive or negative.
                        {
                            break;
                        }
                    }

                    buffer += Text[i];
                }
                else if (Text[i] == '-' || Text[i] == '+')
                {
                    if (i == 0)
                    {
                        break; //The first character is a + or -, this is a valid explicit positive or negative.
                    }
                    if (Utility.IsMathChar(Text[i - 1]))
                    {
                        break; //The next character to the left is a match, this is a valid explicit positive or negative.
                    }
                    buffer += Text[i];
                }
                else
                {
                    break;
                }
            }

            buffer = new string(buffer.ToArray().Reverse().ToArray());

            if (Utility.IsNumeric(buffer) == false)
            {
                throw new Exception($"Invalid right-hand value {buffer}");
            }

            outParsedLength = buffer.Length;

            return double.Parse(buffer);
        }

        private double GetRightValue(int endOfOperationIndex, out int outParsedLength)
        {
            string buffer = string.Empty;

            for (int i = endOfOperationIndex; i < Text.Length; i++)
            {
                if (i == endOfOperationIndex && (Text[i] == '-' || Text[i] == '+'))
                {
                    //Explicit positive or negative number.
                    buffer += Text[i];
                }
                else if (char.IsNumber(Text[i]) || Text[i] == '.')
                {
                    buffer += Text[i];
                }
                else
                {
                    break;
                }
            }

            if (Utility.IsNumeric(buffer) == false)
            {
                throw new Exception($"Invalid right-hand value {buffer}");
            }

            outParsedLength = buffer.Length;

            return double.Parse(buffer);
        }

        private int GetFreestandingNotOperation(out string outFoundOperation) //Pre order.
        {
            for (int i = 0; i < Text.Length; i++)
            {
                //Make sure we have a "!' and not a "!=", these two have to be handled in different places.
                if (Text[i] == '!' && (i + 1 < Text.Length) && Text[i + 1] != '=')
                {
                    outFoundOperation = Text[i].ToString();
                    return i;
                }
            }

            outFoundOperation = string.Empty; //No operation found.
            return -1;
        }

        private int GetIndexOfOperation(char[] validOperations, out string outFoundOperation)
        {
            for (int i = 0; i < Text.Length; i++)
            {
                if (validOperations.Contains(Text[i]))
                {
                    outFoundOperation = Text[i].ToString();
                    return i;
                }
            }

            outFoundOperation = string.Empty; //No operation found.
            return -1;
        }

        private int GetIndexOfOperation(string[] validOperations, out string outFoundOperation)
        {
            int foundIndex = -1;
            string foundOperation = string.Empty;

            foreach (var operation in validOperations)
            {
                int index = Text.IndexOf(operation);
                if (index >= 0 && (foundIndex < 0 || index < foundIndex))
                {
                    foundIndex = index;
                    foundOperation = operation;
                }
            }

            if (foundIndex >= 0)
            {
                outFoundOperation = foundOperation; //No operation found.
                return foundIndex;
            }

            outFoundOperation = string.Empty; //No operation found.
            return -1;
        }
    }
}
