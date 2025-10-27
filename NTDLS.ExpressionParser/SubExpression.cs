using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class SubExpression
    {
        private readonly Expression _parentExpression;
        private StringBuilder _buffer = new();

        public string Text { get; internal set; }

        public SubExpression(Expression parentExpression, string text)
        {
            _parentExpression = parentExpression;
            Text = text;
        }

        private int GetStartingIndexOfLastFunctionCall(out string foundFunction)
        {
            ReadOnlySpan<char> span = Text.AsSpan();

            int foundIndex = -1;

            foundFunction = string.Empty;

            foreach (var function in _parentExpression.DiscoveredFunctions)
            {
                int index = span.LastIndexOf(function);
                if (index >= 0 && index > foundIndex
                    && (index == 0 || !Utility.IsValidVariableChar(span[index - 1]))) //Must not be part of a larger function name.
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
                _buffer.Clear();

                int scope = 0;

                var parameters = new List<double>();

                int i = functionStartIndex + foundFunction.Length; //Skip the function name.

                bool foundNull = false;

                for (; i < Text.Length; i++)
                {
                    if (Text[i] == ',')
                    {
                        var subExpression = new SubExpression(_parentExpression, _buffer.ToString());
                        subExpression.Compute();

                        var param = _parentExpression.StringToDouble(subExpression.Text);
                        foundNull = foundNull || param == null;
                        if (param != null)
                        {
                            parameters.Add(param ?? 0);
                        }
                        _buffer.Clear();
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

                        var subExpression = new SubExpression(_parentExpression, _buffer.ToString());
                        subExpression.Compute();

                        var param = _parentExpression.StringToDouble(subExpression.Text);
                        if (param != null)
                        {
                            foundNull = foundNull || param == null;
                            parameters.Add(param ?? 0);
                        }
                        break;
                    }
                    else
                    {
                        _buffer.Append(Text[i]);
                    }
                }

                functionEndIndex = i;

                if (foundNull)
                {
                    SwapInCacheKey(functionStartIndex, functionEndIndex, null);
                    return true;
                }
                else if (Utility.IsNativeFunction(foundFunction))
                {
                    double functionResult = Utility.ComputeNativeFunction(foundFunction, parameters.ToArray());
                    SwapInCacheKey(functionStartIndex, functionEndIndex, functionResult);
                }
                else
                {
                    if (_parentExpression.ExpressionFunctions.TryGetValue(foundFunction, out var customFunction))
                    {
                        var functionResult = customFunction.Invoke(parameters.ToArray()) ?? _parentExpression.Options.DefaultNullValue;
                        SwapInCacheKey(functionStartIndex, functionEndIndex, functionResult);
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
                    var preParsedCacheSlot = _parentExpression.ConsumeNextPreParsedCacheSlot();

                    if (_parentExpression.Options.UseParserCache
                        && _parentExpression.TryGetPreParsedCache(preParsedCacheSlot, out PreParsedCacheItem cachedObj))
                    {
                        SwapInCacheKey(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ParsedValue);
                    }
                    else
                    {
                        var rightValue = GetRightValue(operatorIndex + 1, out int outParsedLength, out bool isCacheable);
                        int notResult = (rightValue == 0) ? 1 : 0;
                        SwapInCacheKey(operatorIndex, operatorIndex + outParsedLength, notResult);

                        if (isCacheable && _parentExpression.Options.UseParserCache)
                        {
                            var parsedNumber = new PreParsedCacheItem
                            {
                                ParsedValue = notResult,
                                BeginPosition = operatorIndex,
                                EndPosition = operatorIndex + outParsedLength
                            };
                            _parentExpression.StorePreParsedCache(preParsedCacheSlot, parsedNumber);
                        }
                    }
                }

                //First order operations:
                operatorIndex = GetIndexOfOperation(Utility.FirstOrderOperations, out string operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheSlot = _parentExpression.ConsumeNextPreParsedCacheSlot();

                    if (_parentExpression.Options.UseParserCache
                        && _parentExpression.TryGetPreParsedCache(preParsedCacheSlot, out PreParsedCacheItem cachedObj))
                    {
                        SwapInCacheKey(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ParsedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;
                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition, out bool isCacheable))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        SwapInCacheKey(beginPosition, endPosition, calculatedResult);
                        if (isCacheable && _parentExpression.Options.UseParserCache)
                        {
                            var parsedNumber = new PreParsedCacheItem
                            {
                                ParsedValue = calculatedResult,
                                BeginPosition = beginPosition,
                                EndPosition = endPosition
                            };
                            _parentExpression.StorePreParsedCache(preParsedCacheSlot, parsedNumber);
                        }
                    }

                    continue;
                }

                //Second order operations:
                operatorIndex = GetIndexOfOperation(Utility.SecondOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheSlot = _parentExpression.ConsumeNextPreParsedCacheSlot();

                    if (_parentExpression.Options.UseParserCache
                        && _parentExpression.TryGetPreParsedCache(preParsedCacheSlot, out PreParsedCacheItem cachedObj))
                    {
                        SwapInCacheKey(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ParsedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;
                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition, out bool isCacheable))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        SwapInCacheKey(beginPosition, endPosition, calculatedResult);
                        if (isCacheable && _parentExpression.Options.UseParserCache)
                        {
                            var parsedNumber = new PreParsedCacheItem
                            {
                                ParsedValue = calculatedResult,
                                BeginPosition = beginPosition,
                                EndPosition = endPosition
                            };
                            _parentExpression.StorePreParsedCache(preParsedCacheSlot, parsedNumber);
                        }
                    }
                    continue;
                }

                //Third order operations:
                operatorIndex = GetIndexOfOperation(Utility.ThirdOrderOperations, out operation);
                if (operatorIndex > 0)
                {
                    var preParsedCacheSlot = _parentExpression.ConsumeNextPreParsedCacheSlot();

                    if (_parentExpression.Options.UseParserCache
                        && _parentExpression.TryGetPreParsedCache(preParsedCacheSlot, out PreParsedCacheItem cachedObj))
                    {
                        SwapInCacheKey(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ParsedValue);
                    }
                    else
                    {
                        double? calculatedResult = null;

                        if (GetLeftAndRightValues(operation, operatorIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition, out bool isCacheable))
                        {
                            calculatedResult = Utility.ComputePrivative(leftValue, operation, rightValue);
                        }

                        SwapInCacheKey(beginPosition, endPosition, calculatedResult);
                        if (isCacheable && _parentExpression.Options.UseParserCache)
                        {
                            var parsedNumber = new PreParsedCacheItem
                            {
                                ParsedValue = calculatedResult,
                                BeginPosition = beginPosition,
                                EndPosition = endPosition
                            };
                            _parentExpression.StorePreParsedCache(preParsedCacheSlot, parsedNumber);
                        }
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

            return _parentExpression.StorePreComputedCacheItem(_parentExpression.StringToDouble(Text));
        }

        internal void SwapInCacheKey(int startIndex, int endIndex, double? value)
        {
            var cacheKey = _parentExpression.StorePreComputedCacheItem(value);
            Text = _parentExpression.ReplaceRange(Text, startIndex, endIndex, cacheKey);
        }

        /// <summary>
        /// Removes leading and trailing parenthesizes, if they exist.
        /// </summary>
        internal void TruncateParenthesizes()
        {
            while (Text.StartsWith('(') && Text.EndsWith(')'))
            {
                Text = Text[1..^1];
            }
        }

        /// <summary>
        /// Gets the numbers to the left and right of an operator.
        /// Returns FALSE when NULL is found for either value.
        /// </summary>
        private bool GetLeftAndRightValues(string operation,
            int operationBeginIndex, out double leftValue, out double rightValue, out int beginPosition, out int endPosition, out bool isCacheable)
        {
            var left = GetLeftValue(operationBeginIndex, out int leftParsedLength, out bool isLeftCacheable);
            var right = GetRightValue(operationBeginIndex + operation.Length, out int rightParsedLength, out bool isRightCacheable);

            leftValue = left ?? 0;
            rightValue = right ?? 0;

            beginPosition = operationBeginIndex - leftParsedLength;
            endPosition = operationBeginIndex + rightParsedLength + (operation.Length - 1);

            isCacheable = isLeftCacheable && isRightCacheable;

            return left != null && right != null;
        }

        private double? GetLeftValue(int operationIndex, out int outParsedLength, out bool isCacheable)
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
                var cacheKey = span.Slice(operationIndex - outParsedLength + 1, outParsedLength - 2);
                var cachedItem = _parentExpression.GetPreComputedCacheItem(cacheKey);
                isCacheable = !cachedItem.IsVariable;
                return cachedItem.ComputedValue;
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
                isCacheable = true;
                return _parentExpression.StringToDouble(span.Slice(operationIndex - outParsedLength, outParsedLength));
            }
        }

        private double? GetRightValue(int endOfOperationIndex, out int outParsedLength, out bool isCacheable)
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
                var cachedItem = _parentExpression.GetPreComputedCacheItem(span.Slice(1, i - 2));
                isCacheable = !cachedItem.IsVariable;
                return cachedItem.ComputedValue;
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
                isCacheable = true;
                return _parentExpression.StringToDouble(span.Slice(0, i));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFreestandingNotOperation(out string outFoundOperation)
        {
            ReadOnlySpan<char> span = Text.AsSpan();
            int len = span.Length - 1;

            for (int i = 0; i < len; i++)
            {
                //Make sure we have a "!' and not a "!=", these two have to be handled in different places.
                if (span[i] == '!' && span[i + 1] != '=')
                {
                    outFoundOperation = "!";
                    return i;
                }
            }

            // Check last char separately
            if (len >= 0 && span[len] == '!')
            {
                outFoundOperation = "!";
                return len;
            }

            outFoundOperation = string.Empty;
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
