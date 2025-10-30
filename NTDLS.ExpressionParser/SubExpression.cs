using System.Runtime.CompilerServices;
using System.Text;

namespace NTDLS.ExpressionParser
{
    internal class SubExpression
    {
        private readonly Expression _parentExpression;
        private readonly StringBuilder _buffer = new();
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

            foreach (var function in _parentExpression.Sanitized.DiscoveredFunctions)
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

        /// <summary>
        /// Processes all functions in the expression, return true if any function was found - otherwise returns false.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
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

                        var param = _parentExpression.StringToDouble(subExpression.Text, out _);
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

                        var param = _parentExpression.StringToDouble(subExpression.Text, out _);
                        foundNull = foundNull || param == null;
                        parameters.Add(param ?? 0);
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
                    StorePlaceholder(functionStartIndex, functionEndIndex, null, true);
                    return true;
                }
                else if (Utility.IsNativeFunction(foundFunction))
                {
                    double functionResult = Utility.ComputeNativeFunction(foundFunction, parameters.ToArray());
                    StorePlaceholder(functionStartIndex, functionEndIndex, functionResult, true);
                }
                else
                {
                    if (_parentExpression.ExpressionFunctions.TryGetValue(foundFunction, out var customFunction))
                    {
                        var functionResult = customFunction.Invoke(parameters.ToArray()) ?? _parentExpression.Options.DefaultNullValue;
                        StorePlaceholder(functionStartIndex, functionEndIndex, functionResult, true);
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

            //Process all function calls from right-to-left.
            while (ProcessFunctionCall())
            {
            }

            bool isAnyUserVariableDerived = false;

            OperationStepItem foundOperation;

            while (true)
            {
                //Pre-first-order:
                while (GetFreestandingNotOperation(out foundOperation))
                {
                    var rightValue = GetRightValue(foundOperation.Index + 1, out int outParsedLength, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    int? calculatedResult = rightValue == null ? null : (rightValue == 0) ? 1 : 0;
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

                //Third order operations:
                if (GetIndexOfOperation(Utility.ThirdOrderOperations, out foundOperation))
                {
                    CollapseRightAndLeft(foundOperation.Operation, foundOperation.Index, out bool isUserVariableDerived);
                    isAnyUserVariableDerived = isAnyUserVariableDerived || isUserVariableDerived;
                    continue;
                }

                break;
            }

            if (Text[0] == '$')
            {
                //We already have this value in the cache.
                return Text;
            }

            return _parentExpression.State.StorePlaceholderCacheItem(_parentExpression.StringToDouble(Text, out _));
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
                    i--;
                    outParsedLength = (operationIndex - i) - 1;
                    var cacheKey = span.Slice(operationIndex - outParsedLength + 1, outParsedLength - 2);
                    var cachedItem = _parentExpression.State.GetPlaceholderCacheItem(cacheKey);
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

                if (span[i] == '$')
                {
                    i++; //Skip the cache indicator.
                    while (span[i] != '$')
                    {
                        i++;
                    }
                    i++;
                    outParsedLength = i;
                    var cachedItem = _parentExpression.State.GetPlaceholderCacheItem(span.Slice(1, i - 2));
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
                int len = span.Length - 1;

                for (int i = 0; i < len; i++)
                {
                    //Make sure we have a "!' and not a "!=", these two have to be handled in different places.
                    if (span[i] == '!' && span[i + 1] != '=')
                    {
                        operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                        {
                            Index = i,
                            Operation = "!",
                            IsValid = true
                        });
                        return true;
                    }
                }

                // Check last char separately
                if (len >= 0 && span[len] == '!')
                {
                    operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                    {
                        Index = len,
                        Operation = "!",
                        IsValid = true
                    });
                    return true;
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
                    for (int j = 0; j < validOperations.Length; j++)
                    {
                        if (c == validOperations[j])
                        {
                            operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                            {
                                Index = i,
                                Operation = c.ToString(),
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetIndexOfOperation(string[] validOperations, out OperationStepItem operation)
        {
            if (_parentExpression.State.OperationStepCache.TryGet(out operation, out int cacheIndex))
            {
                return operation.IsValid;
            }
            else
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
                            operation = _parentExpression.State.OperationStepCache.Store(cacheIndex, new OperationStepItem()
                            {
                                Index = i,
                                Operation = op,
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
    }
}
