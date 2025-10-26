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
                if (index >= 0 && index > foundIndex
                    && (index == 0 || !Utility.IsValidVariableChar(Text[index - 1]))) //Must not be part of a larger function name.
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

                var parameters = new List<double>();

                int i = functionStartIndex + foundFunction.Length; //Skip the function name.

                bool foundNull = false;

                for (; i < Text.Length; i++)
                {
                    if (Text[i] == ',')
                    {
                        var subExpression = new SubExpression(_parentExpression, buffer);
                        subExpression.Compute();

                        var param = _parentExpression.StringToDouble(subExpression.Text);
                        foundNull = foundNull || param == null;
                        if (param != null)
                        {
                            parameters.Add(param ?? 0);
                        }
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
                        if (param != null)
                        {
                            foundNull = foundNull || param == null;
                            parameters.Add(param ?? 0);
                        }
                        break;
                    }
                    else
                    {
                        buffer += Text[i];
                    }
                }

                functionEndIndex = i;

                if (foundNull)
                {
                    ReplaceRange(functionStartIndex, functionEndIndex, null);
                    return true;
                }
                else if (Utility.IsNativeFunction(foundFunction))
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
                    var preParsedCacheKey = _parentExpression.NextPreParsedCacheKey++;

                    if (_parentExpression.TryGetPreParsed(preParsedCacheKey, out PreParsedCacheItem cachedObj))
                    {
                        ReplaceRange(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ComputedValue);
                    }
                    else
                    {
                        var rightValue = GetRightValue(operatorIndex + 1, out int outParsedLength);
                        int notResult = (rightValue == 0) ? 1 : 0;
                        ReplaceRange(operatorIndex, operatorIndex + outParsedLength, notResult);

                        var parsedNumber = new PreParsedCacheItem
                        {
                            ComputedValue = notResult,
                            BeginPosition = operatorIndex,
                            EndPosition = operatorIndex + outParsedLength
                        };
                        _parentExpression.SetPreParsed(preParsedCacheKey, parsedNumber);
                    }
                }

                //First order operations:
                operatorIndex = GetIndexOfOperation(Utility.FirstOrderOperations, out string operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheKey = _parentExpression.NextPreParsedCacheKey++;

                    if (_parentExpression.TryGetPreParsed(preParsedCacheKey, out PreParsedCacheItem cachedObj))
                    {
                        ReplaceRange(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ComputedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;
                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        ReplaceRange(beginPosition, endPosition, calculatedResult);
                        var parsedNumber = new PreParsedCacheItem
                        {
                            ComputedValue = calculatedResult,
                            BeginPosition = beginPosition,
                            EndPosition = endPosition
                        };
                        _parentExpression.SetPreParsed(preParsedCacheKey, parsedNumber);
                    }

                    continue;
                }

                //Second order operations:
                operatorIndex = GetIndexOfOperation(Utility.SecondOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheKey = _parentExpression.NextPreParsedCacheKey++;

                    if (_parentExpression.TryGetPreParsed(preParsedCacheKey, out PreParsedCacheItem cachedObj))
                    {
                        ReplaceRange(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ComputedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;
                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        ReplaceRange(beginPosition, endPosition, calculatedResult);
                        var parsedNumber = new PreParsedCacheItem
                        {
                            ComputedValue = calculatedResult,
                            BeginPosition = beginPosition,
                            EndPosition = endPosition
                        };
                        _parentExpression.SetPreParsed(preParsedCacheKey, parsedNumber);

                    }
                    continue;
                }

                //Third order operations:
                operatorIndex = GetIndexOfOperation(Utility.ThirdOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheKey = _parentExpression.NextPreParsedCacheKey++;

                    if (_parentExpression.TryGetPreParsed(preParsedCacheKey, out PreParsedCacheItem cachedObj))
                    {
                        ReplaceRange(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ComputedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;

                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        ReplaceRange(beginPosition, endPosition, calculatedResult);
                        var parsedNumber = new PreParsedCacheItem
                        {
                            ComputedValue = calculatedResult,
                            BeginPosition = beginPosition,
                            EndPosition = endPosition
                        };
                        _parentExpression.SetPreParsed(preParsedCacheKey, parsedNumber);
                    }
                    continue;
                }

                break;
            }

            if (Text[0] == '$')
            {
                //We already have this value in the cache.
                return Text;
            }

            var cacheKey = _parentExpression.ConsumeNextPreComputedCacheKey(out int cacheIndex);
            _parentExpression.PreComputedCache[cacheIndex] = _parentExpression.StringToDouble(Text);
            return cacheKey;
        }

        internal void ReplaceRange(int startIndex, int endIndex, double? value)
        {
            var cacheKey = _parentExpression.ConsumeNextPreComputedCacheKey(out int cacheIndex);
            _parentExpression.PreComputedCache[cacheIndex] = value;
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
        /// Returns FALSE when NULL is found for either value.
        /// </summary>
        private bool GetLeftAndRightValues(string operation,
            int operationBeginIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition)
        {
            var left = GetLeftValue(operationBeginIndex, out int leftParsedLength);
            var right = GetRightValue(operationBeginIndex + operation.Length, out int rightParsedLength);

            leftValue = left ?? 0;
            rightValue = right ?? 0;

            beginPosition = operationBeginIndex - leftParsedLength;
            endPosition = operationBeginIndex + rightParsedLength + (operation.Length - 1);

            return left != null && right != null;
        }

        private double? GetLeftValue(int operationIndex, out int outParsedLength)
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
                return _parentExpression.CachedValue(key);
            }
            else
            {
                while (i > -1 && ((span[i] - '0' >= 0 && span[i] - '0' <= 9) || span[i] == '.'))
                {
                    i--;
                }

                //Check for explicit positive or negative sign if the number is not at the start of the expression.
                if (i == 0 && (span[i] == '-' || span[i] == '+'))
                {
                    i--; //Skip the explicit positive or negative sign or cache indicator.
                }

                //Check for explicit positive or negative sign when the preceding character is a math character.
                if (i > 0 && Utility.IsMathChar(span[i - 1]) && (span[i] == '-' || span[i] == '+'))
                {
                    i--; //Skip the explicit positive or negative sign or cache indicator.
                }

                outParsedLength = (operationIndex - i) - 1;
                var value = span.Slice(operationIndex - outParsedLength, outParsedLength);
                return _parentExpression.StringToDouble(value);
            }
        }

        private double? GetRightValue(int endOfOperationIndex, out int outParsedLength)
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
                return _parentExpression.CachedValue(span.Slice(1, i - 2));
            }
            else
            {
                if (i < span.Length && (span[i] == '-' || span[i] == '+'))
                {
                    i++; //Skip the explicit positive or negative sign or cache indicator.
                }

                while (i < span.Length && ((span[i] - '0' >= 0 && span[i] - '0' <= 9) || span[i] == '.'))
                {
                    i++;
                }

                outParsedLength = i;
                var value = span.Slice(0, i);
                return _parentExpression.StringToDouble(value);
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
