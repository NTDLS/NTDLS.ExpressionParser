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

        /// <summary>
        /// Finds the right-most function call. Its parameter list contains no other function calls because
        /// any nested call would have opened a later '{'. Returns -1 when there are no function calls.
        /// </summary>
        private int GetStartingIndexOfLastFunctionCall(out string foundFunction, out int openingBraceIndex)
        {
            foundFunction = string.Empty;

            openingBraceIndex = Text.LastIndexOf('{');
            if (openingBraceIndex < 0)
                return -1;

            var span = Text.AsSpan();

            int nameStart = openingBraceIndex;
            while (nameStart > 0 && Utility.IsValidVariableChar(span[nameStart - 1]))
                nameStart--;
            while (nameStart < openingBraceIndex && char.IsAsciiDigit(span[nameStart]))
                nameStart++; //Leading digits belong to a preceding number (e.g. "2max(...)"), not the function name.

            var name = span[nameStart..openingBraceIndex];

            foreach (var function in _parentExpression.Sanitized.DiscoveredFunctions)
            {
                if (name.SequenceEqual(function))
                {
                    foundFunction = function;
                    return nameStart;
                }
            }

            throw new Exception($"Undefined function: {name.ToString()}");
        }

        /// <summary>
        /// Processes the right-most function in the expression, return true if any function was found - otherwise returns false.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private bool ProcessFunctionCall()
        {
            int functionStartIndex = GetStartingIndexOfLastFunctionCall(out string foundFunction, out int openingBraceIndex);
            if (functionStartIndex < 0)
                return false;

            int functionEndIndex = Text.IndexOf('}', openingBraceIndex);
            if (functionEndIndex < 0)
                throw new Exception($"Parentheses mismatch when parsing function: {foundFunction}");

            var body = Text.AsSpan(openingBraceIndex + 1, functionEndIndex - openingBraceIndex - 1);

            var parameters = body.Length == 0 ? [] : new double[body.Count(',') + 1];
            bool foundNull = false;

            for (int p = 0; p < parameters.Length; p++)
            {
                int commaIndex = body.IndexOf(',');
                var parameterText = commaIndex < 0 ? body : body[..commaIndex];
                body = commaIndex < 0 ? [] : body[(commaIndex + 1)..];

                if (parameterText.Length == 0)
                    throw new Exception($"Empty parameter passed to function: {foundFunction}");

                var subExpression = new SubExpression(_parentExpression, parameterText.ToString());
                subExpression.Compute();

                var param = _parentExpression.StringToDouble(subExpression.Text, out _);
                foundNull = foundNull || param == null;
                parameters[p] = param ?? 0;
            }

            if (foundNull)
            {
                StorePlaceholder(functionStartIndex, functionEndIndex, null, true);
            }
            else if (Utility.IsNativeFunction(foundFunction))
            {
                double functionResult = Utility.ComputeNativeFunction(foundFunction, parameters);
                StorePlaceholder(functionStartIndex, functionEndIndex, functionResult, true);
            }
            else if (_parentExpression.ExpressionFunctions.TryGetValue(foundFunction, out var customFunction))
            {
                var functionResult = customFunction.Invoke(parameters) ?? _parentExpression.Options.DefaultNullValue;
                StorePlaceholder(functionStartIndex, functionEndIndex, functionResult, true);
            }
            else
            {
                throw new Exception($"Undefined function: {foundFunction}");
            }

            return true;
        }

        internal string Compute()
        {
            TruncateParenthesizes();

            //Process all function calls from right-to-left.
            while (ProcessFunctionCall())
            {
            }

            bool isAnyUserVariableDerived = false;

            OperationStepItem foundOperation;

            while (true)
            {
                //Pre-first-order (unary logical and bitwise NOT):
                while (GetFreestandingNotOperation(out foundOperation))
                {
                    var rightValue = GetRightValue(foundOperation.Index + 1, out int outParsedLength, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    double? calculatedResult = rightValue == null ? null
                        : foundOperation.Operation == "~" ? ~(int)rightValue.Value
                        : (rightValue == 0) ? 1 : 0;
                    StorePlaceholder(foundOperation.Index, foundOperation.Index + outParsedLength, calculatedResult, isUserVariableDerived);
                }

                //First order operations:
                if (GetIndexOfOperation(Utility.FirstOrderOperations, out foundOperation))
                {
                    CollapseRightAndLeft(foundOperation.Operation, foundOperation.Index, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    continue;
                }

                //Second order operations:
                if (GetIndexOfOperation(Utility.SecondOrderOperations, out foundOperation))
                {
                    CollapseRightAndLeft(foundOperation.Operation, foundOperation.Index, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    continue;
                }

                //Third order operations (comparison, bitwise and logical - resolved by precedence):
                if (GetIndexOfThirdOrderOperation(out foundOperation))
                {
                    CollapseRightAndLeft(foundOperation.Operation, foundOperation.Index, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    continue;
                }

                break;
            }

            if (Utility.IsSinglePlaceholder(Text))
                return Text;

            if (Text[0] == '$')
                throw new Exception($"Expression was not fully reduced: '{Text}'. This may indicate a bug in operator parsing or a cache misalignment.");

            var value = _parentExpression.StringToDouble(Text, out bool isValueUserVariableDerived);
            return _parentExpression.State.StorePlaceholderCacheItem(value, isValueUserVariableDerived);
        }

        internal void StorePlaceholder(int startIndex, int endIndex, double? value, bool isUserVariableDerived)
        {
            var cacheKey = _parentExpression.State.StorePlaceholderCacheItem(value, isUserVariableDerived);
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
        private void CollapseRightAndLeft(string operation, int operationBeginIndex, out bool isUserVariableDerived)
        {
            var left = GetLeftValue(operationBeginIndex, out int leftParsedLength, out bool isLeftUserVariableDerived);
            var right = GetRightValue(operationBeginIndex + operation.Length, out int rightParsedLength, out bool isRightUserVariableDerived);

            var beginPosition = operationBeginIndex - leftParsedLength;
            var endPosition = operationBeginIndex + rightParsedLength + (operation.Length - 1);

            isUserVariableDerived = isLeftUserVariableDerived || isRightUserVariableDerived;

            if (_parentExpression.State.ComputedStepCache.TryGet(out ComputedStepItem cachedObj, out int cacheIndex) && !cachedObj.IsUserVariableDerived)
            {
                StorePlaceholder(cachedObj.BeginPosition, cachedObj.EndPosition, cachedObj.ParsedValue, isUserVariableDerived);
            }
            else
            {
                double? result = null;

                if (left != null && right != null)
                {
                    result = Utility.ComputePrivative(left ?? 0, operation, right ?? 0);
                }

                StorePlaceholder(beginPosition, endPosition, result, isUserVariableDerived);
                if (!isLeftUserVariableDerived && !isRightUserVariableDerived)
                {
                    var parsedNumber = new ComputedStepItem
                    {
                        ParsedValue = result,
                        BeginPosition = beginPosition,
                        EndPosition = endPosition,
                        IsUserVariableDerived = false
                    };
                    //Is cachedObj.IsUserVariableDerived is true this means that we have already stored the value
                    //  in the cache and storing it again would increase the visitor count causing a misalignment.
                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ComputedStepCache.Store(cacheIndex, parsedNumber);
                    }
                }
                else
                {
                    //Is cachedObj.IsUserVariableDerived is true this means that we have already stored the value
                    //  in the cache and storing it again would increase the visitor count causing a misalignment.
                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ComputedStepCache.Store(cacheIndex, new ComputedStepItem
                        {
                            IsUserVariableDerived = true
                        });
                    }
                }
            }
        }

        private double? GetLeftValue(int operationIndex, out int outParsedLength, out bool isUserVariableDerived)
        {
            if (_parentExpression.State.ScanStepCache.TryGet(out var cachedObj, out int cacheIndex) && !cachedObj.IsUserVariableDerived)
            {
                outParsedLength = cachedObj.Length;
                isUserVariableDerived = false;
                return cachedObj.Value;
            }
            else
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
                    var cacheKey = span[(i + 1)..(operationIndex - 1)];
                    i--;

                    //Check for an explicit sign: at the start of the expression or following a math character.
                    bool isNegative = false;
                    if (i >= 0 && (span[i] == '-' || span[i] == '+') && (i == 0 || Utility.IsMathChar(span[i - 1])))
                    {
                        isNegative = span[i] == '-';
                        i--;
                    }

                    outParsedLength = (operationIndex - i) - 1;
                    var cachedItem = _parentExpression.State.GetPlaceholderCacheItem(cacheKey);
                    if (isNegative)
                        cachedItem.ComputedValue = -cachedItem.ComputedValue;
                    isUserVariableDerived = cachedItem.IsUserVariableDerived;

                    //Is cachedObj.IsUserVariableDerived is true this means that we have already stored the value
                    //  in the cache and storing it again would increase the visitor count causing a misalignment.
                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ScanStepCache.Store(cacheIndex, new ScanStepItem
                        {
                            Value = cachedItem.ComputedValue,
                            Length = outParsedLength,
                            IsUserVariableDerived = isUserVariableDerived
                        });
                    }

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
                    var result = _parentExpression.StringToDouble(span.Slice(operationIndex - outParsedLength, outParsedLength), out isUserVariableDerived);

                    //Is cachedObj.IsUserVariableDerived is true this means that we have already stored the value
                    //  in the cache and storing it again would increase the visitor count causing a misalignment.
                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ScanStepCache.Store(cacheIndex, new ScanStepItem
                        {
                            Value = result,
                            Length = outParsedLength,
                            IsUserVariableDerived = false
                        });
                    }

                    return result;
                }
            }
        }

        private double? GetRightValue(int endOfOperationIndex, out int outParsedLength, out bool isUserVariableDerived)
        {
            if (_parentExpression.State.ScanStepCache.TryGet(out var cachedObj, out int cacheIndex) && !cachedObj.IsUserVariableDerived)
            {
                outParsedLength = cachedObj.Length;
                isUserVariableDerived = false;
                return cachedObj.Value;
            }
            else
            {
                var span = Text.AsSpan(endOfOperationIndex);

                int i = 0;

                bool isNegative = false;
                if (span.Length > 1 && span[1] == '$' && (span[0] == '-' || span[0] == '+'))
                {
                    isNegative = span[0] == '-';
                    i++; //Skip the explicit sign.
                }

                if (span[i] == '$')
                {
                    int keyStart = ++i; //Skip the cache indicator.
                    while (span[i] != '$')
                    {
                        i++;
                    }
                    var cachedItem = _parentExpression.State.GetPlaceholderCacheItem(span[keyStart..i]);
                    i++;
                    outParsedLength = i;
                    if (isNegative)
                        cachedItem.ComputedValue = -cachedItem.ComputedValue;
                    isUserVariableDerived = cachedItem.IsUserVariableDerived;

                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ScanStepCache.Store(cacheIndex, new ScanStepItem
                        {
                            Value = cachedItem.ComputedValue,
                            Length = outParsedLength,
                            IsUserVariableDerived = isUserVariableDerived
                        });
                    }

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
                    var result = _parentExpression.StringToDouble(span.Slice(0, i), out isUserVariableDerived);

                    if (!cachedObj.IsUserVariableDerived)
                    {
                        _parentExpression.State.ScanStepCache.Store(cacheIndex, new ScanStepItem
                        {
                            Value = result,
                            Length = outParsedLength,
                            IsUserVariableDerived = false
                        });
                    }

                    return result;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetFreestandingNotOperation(out OperationStepItem operation)
        {
            if (_parentExpression.State.OperationStepCache.TryGet(out operation, out int cacheIndex))
            {
                return operation.IsValid;
            }
            else
            {
                ReadOnlySpan<char> span = Text.AsSpan();

                //Right-most first, so that stacked operators such as "!!1" or "!~1" resolve inside-out.
                for (int i = span.Length - 1; i >= 0; i--)
                {
                    //Make sure we have a "!' and not a "!=", these two have to be handled in different places.
                    if (span[i] == '~' || (span[i] == '!' && (i + 1 >= span.Length || span[i + 1] != '=')))
                    {
                        operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                        {
                            Index = i,
                            Operation = Utility.OperatorString(span[i]),
                            IsValid = true
                        });
                        return true;
                    }
                }

                //No operation found.
                operation = _parentExpression.State.OperationStepCache.StoreInvalid(cacheIndex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetIndexOfOperation(ReadOnlySpan<char> validOperations, out OperationStepItem operation)
        {
            if (_parentExpression.State.OperationStepCache.TryGet(out operation, out int cacheIndex))
            {
                return operation.IsValid;
            }
            else
            {
                ReadOnlySpan<char> span = Text.AsSpan();

                for (int i = 1; i < span.Length; i++)
                {
                    char c = span[i];

                    //A sign that directly follows another operator is unary (e.g. "2>-1"), not a binary operation.
                    if ((c == '-' || c == '+') && Utility.IsMathChar(span[i - 1]))
                        continue;

                    for (int j = 0; j < validOperations.Length; j++)
                    {
                        if (c == validOperations[j])
                        {
                            operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                            {
                                Index = i,
                                Operation = Utility.OperatorString(c),
                                IsValid = true
                            });
                            return true;
                        }
                    }
                }

                //No operation found.
                operation = _parentExpression.State.OperationStepCache.StoreInvalid(cacheIndex);
                return false;
            }
        }

        /// <summary>
        /// Finds the third order operation with the highest precedence, choosing the left-most for equal precedence.
        /// </summary>
        private bool GetIndexOfThirdOrderOperation(out OperationStepItem operation)
        {
            if (_parentExpression.State.OperationStepCache.TryGet(out operation, out int cacheIndex))
            {
                return operation.IsValid;
            }
            else
            {
                ReadOnlySpan<char> span = Text.AsSpan();
                var operations = Utility.ThirdOrderOperations;

                string? bestOperation = null;
                int bestIndex = -1;
                int bestPrecedence = int.MaxValue;

                for (int i = 0; i < span.Length;)
                {
                    string? matched = null;
                    for (int j = 0; j < operations.Length; j++)
                    {
                        if (span[i..].StartsWith(operations[j]))
                        {
                            matched = operations[j];
                            break;
                        }
                    }

                    if (matched == null)
                    {
                        i++;
                        continue;
                    }

                    int precedence = Utility.ThirdOrderPrecedence(matched);
                    if (precedence < bestPrecedence)
                    {
                        bestPrecedence = precedence;
                        bestOperation = matched;
                        bestIndex = i;
                    }

                    i += matched.Length; //Skip the whole operator so "<<" is never re-matched as "<".
                }

                if (bestOperation != null)
                {
                    operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                    {
                        Index = bestIndex,
                        Operation = bestOperation,
                        IsValid = true
                    });
                    return true;
                }

                //No operation found.
                operation = _parentExpression.State.OperationStepCache.StoreInvalid(cacheIndex);
                return false;
            }
        }
    }
}
