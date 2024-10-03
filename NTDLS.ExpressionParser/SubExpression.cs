namespace NTDLS.ExpressionParser
{
    internal class SubExpression
    {
        private readonly Expression _parentExpression;
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

            int functionStartIndex = GetStartingIndexOfLastFunctionCall(out foundFunction);
            int functionEndIndex;

            if (functionStartIndex >= 0)
            {
                string buffer = string.Empty;
                int scope = 0;

                List<double> parameters = new();

                int i = functionStartIndex + foundFunction.Length; //Skip the function name.

                for (; i < Text.Length; i++)
                {
                    if (Text[i] == ',')
                    {
                        var subExpression = new SubExpression(_parentExpression, buffer);
                        subExpression.Compute();
                        parameters.Add(Utility.StringToDouble(subExpression.Text));
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

                        var param = _parentExpression.ExpToDouble(subExpression.Text);
                        parameters.Add(param);

                        break;
                    }
                    else
                    {
                        buffer += Text[i];
                    }
                }

                functionEndIndex = i;

                if (Utility.IsNativeFunction(foundFunction))
                {
                    double functionResult = Utility.ComputeNativeFunction(foundFunction, parameters.ToArray());
                    ReplaceRange(functionStartIndex, functionEndIndex, functionResult);
                }
                else
                {
                    if (_parentExpression.CustomFunctions.TryGetValue(foundFunction, out var customFunction))
                    {
                        double functionResult = customFunction.Invoke(parameters.ToArray());
                        ReplaceRange(functionStartIndex, functionEndIndex, functionResult);
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
            TruncateParenthesizes();

            while (true)
            {
                int operatorIndex;

                //Process all function calls from right-to-left.
                while (ProcessFunctionCall()) { }

                //Pre-first-order:
                while ((operatorIndex = GetFreestandingNotOperation(out _)) > 0)
                {
                    double rightValue = GetRightValue(operatorIndex + 1, out int outParsedLength);
                    int notResult = (rightValue == 0) ? 1 : 0;
                    ReplaceRange(operatorIndex, operatorIndex + outParsedLength, notResult);
                }

                //First order operations:
                operatorIndex = GetIndexOfOperation(Utility.FirstOrderOperations, out string operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                //Second order operations:
                operatorIndex = GetIndexOfOperation(Utility.SecondOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                //Third order operations:
                operatorIndex = GetIndexOfOperation(Utility.ThirdOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition);

                    double calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                    ReplaceRange(beginPosition, endPosition, calculatedResult);

                    continue;
                }

                break;
            }

            if (Text[0] == '$')
            {
                //We already have this value in the cache.
                return Text;
            }

            int index = _parentExpression.ConsumeNextComputedCacheIndex();
            _parentExpression.ComputedCache[index] = Utility.StringToDouble(Text);
            return "$" + index + "$";
        }

        internal void ReplaceRange(int startIndex, int endIndex, double value)
        {
            int index = _parentExpression.ConsumeNextComputedCacheIndex();
            _parentExpression.ComputedCache[index] = value;
            Text = _parentExpression.ReplaceRange(Text, startIndex, endIndex, "$" + index + "$");
        }

        /// <summary>
        /// Removes leading and trailing parenthesizes, if they exist.
        /// </summary>
        internal void TruncateParenthesizes()
        {
            while (Text.StartsWith('(') && Text.EndsWith(')'))
            {
                Text = Text.Substring(1, Text.Length - 2);
            }
        }

        /// <summary>
        /// Gets the numbers to the left and right of an operator.
        /// </summary>
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
            int i = operationIndex - 1;

            for (; i > -1; i--)
            {
                if (((Text[i] - '0') >= 0 && (Text[i] - '0') <= 9) || Text[i] == '.' || Text[i] == '$')
                {
                }
                else if (Text[i] == '-' || Text[i] == '+')
                {
                    if (i == 0)
                    {
                        //The first character is a + or -, this is a valid explicit positive or negative.
                    }
                    else if (Utility.IsMathChar(Text[i - 1]))
                    {
                        //The next character to the left is a match, this is a valid explicit positive or negative.
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            outParsedLength = (operationIndex - 1) - i;
            string value = Text.Substring(i + 1, outParsedLength);

            if (value[0] == '$')
            {
                int index = Utility.StringToUint(value.AsSpan(1, outParsedLength - 2));
                return _parentExpression.ComputedCache[index];
            }

            return Utility.StringToDouble(value);
        }

        private double GetRightValue(int endOfOperationIndex, out int outParsedLength)
        {
            int i = endOfOperationIndex;

            for (; i < Text.Length; i++)
            {
                if (i == endOfOperationIndex && (Text[i] == '-' || Text[i] == '+') || Text[i] == '$')
                {
                }
                else if (((Text[i] - '0') >= 0 && (Text[i] - '0') <= 9) || (Text[i] == '.'))
                {
                }
                else
                {
                    break;
                }
            }

            outParsedLength = i - endOfOperationIndex;

            string value = Text.Substring(endOfOperationIndex, outParsedLength);
            if (value[0] == '$')
            {
                int index = Utility.StringToUint(value.AsSpan(1, outParsedLength - 2));
                return _parentExpression.ComputedCache[index];
            }

            return Utility.StringToDouble(value);
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
            for (int i = 1; i < Text.Length; i++)
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

            for (int i = 0; i < validOperations.Length; i++)
            {
                int index = Text.IndexOf(validOperations[i], 1);
                if (index >= 1 && (foundIndex < 0 || index < foundIndex))
                {
                    foundIndex = index;
                    foundOperation = validOperations[i];
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
