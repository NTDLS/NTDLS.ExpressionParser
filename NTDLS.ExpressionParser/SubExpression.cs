using System;
using System.Runtime.CompilerServices;

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
            int functionStartIndex = GetStartingIndexOfLastFunctionCall(out string foundFunction);
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
                while ((operatorIndex = GetFreestandingNotOperation(out _)) != -1)
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

            var cacheKey = _parentExpression.ConsumeNextComputedCacheIndex(out int cacheIndex);
            _parentExpression.ComputedCache[cacheIndex] = Utility.StringToDouble(Text);
            return cacheKey;
        }

        internal void ReplaceRange(int startIndex, int endIndex, double value)
        {
            var cacheKey = _parentExpression.ConsumeNextComputedCacheIndex(out int cacheIndex);
            _parentExpression.ComputedCache[cacheIndex] = value;
            Text = _parentExpression.ReplaceRange(Text, startIndex, endIndex, cacheKey);
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
            var span = Text.AsSpan(0, operationIndex);

            int i = operationIndex - 1;

            if (span[i] == '$')
            {
                i--; //Skip the cache indicator.
                while (span[i] != '$')
                {
                    i--;
                }
                i--;
                outParsedLength = (operationIndex - i) - 1;
                var key = span.Slice(operationIndex - outParsedLength + 1, outParsedLength - 2);
                return _parentExpression.CacheValueNew(key);
            }
            else
            {
                if (span[i] == '-' || span[i] == '+')
                {
                    i--; //Skip the explicit positive or negative sign or cache indicator.
                }

                while (i > -1 && ((span[i] - '0' >= 0 && span[i] - '0' <= 9) || span[i] == '.'))
                {
                    i--;
                }

                outParsedLength = (operationIndex - i) - 1;
                var value = span.Slice(operationIndex - outParsedLength, outParsedLength);
                return Utility.StringToDouble(value);
            }
        }

        private double GetRightValue(int endOfOperationIndex, out int outParsedLength)
        {
            var span = Text.AsSpan(endOfOperationIndex);

            int i = 0;

            if (span[i] == '$')
            {
                i++; //Skip the cache indicator.
                while (span[i] != '$')
                {
                    i++;
                }
                i++;
                outParsedLength = i;
                return _parentExpression.CacheValueNew(span.Slice(1, i - 2));
            }
            else
            {
                if (span[i] == '-' || span[i] == '+')
                {
                    i++; //Skip the explicit positive or negative sign or cache indicator.
                }

                while (i < span.Length && ((span[i] - '0' >= 0 && span[i] - '0' <= 9) || span[i] == '.'))
                {
                    i++;
                }

                outParsedLength = i;
                var value = span.Slice(0, i);
                return Utility.StringToDouble(value);
            }
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetIndexOfOperation(ReadOnlySpan<char> validOperations, out string foundOperation)
        {
            ReadOnlySpan<char> span = Text.AsSpan();

            for (int i = 1; i < span.Length; i++)
            {
                char c = span[i];
                for (int j = 0; j < validOperations.Length; j++)
                {
                    if (c == validOperations[j])
                    {
                        foundOperation = c.ToString();
                        return i;
                    }
                }
            }

            foundOperation = string.Empty;
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetIndexOfOperation(string[] validOperations, out string foundOperation)
        {
            ReadOnlySpan<char> span = Text.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                // For each position, test all operations.
                for (int j = 0; j < validOperations.Length; j++)
                {
                    string op = validOperations[j];
                    ReadOnlySpan<char> opSpan = op.AsSpan();

                    // If not enough room left, skip
                    if (i + opSpan.Length > span.Length)
                        continue;

                    // Compare directly (Span sequence equality)
                    if (span.Slice(i, opSpan.Length).SequenceEqual(opSpan))
                    {
                        // earliest operator found, stop scanning
                        foundOperation = op;
                        return i;
                    }
                }
            }

            foundOperation = string.Empty; //No operation found.
            return -1;
        }

    }
}
