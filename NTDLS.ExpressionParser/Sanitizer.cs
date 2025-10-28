using System.Text;

namespace NTDLS.ExpressionParser
{
    internal static class Sanitizer
    {
        public static Sanitized Process(string expressionText, ExpressionOptions options)
        {
            var sanitized = new Sanitized();

            var result = new StringBuilder();
            var buffer = new StringBuilder();

            int scope = 0;
            int consecutiveMathChars = 0;

            var regex = CompiledRegEx.RegExNullCheck();

            //Find and replace all NULL literals with cache keys.
            //These cache entries contain NULL by default, so no need to set them.
            while (true)
            {
                var match = regex.Match(expressionText);
                if (!match.Success)
                    break;

                buffer.Clear();
                buffer.Append(expressionText.AsSpan(0, match.Index));
                buffer.Append($"${sanitized.OperationCount}$");
                buffer.Append(expressionText.AsSpan((match.Index + match.Length)));
                expressionText = buffer.ToString();

                sanitized.OperationCount++;
            }

            sanitized.ConsumedPreComputedCacheSlots = sanitized.OperationCount;

            var expressionSpan = expressionText.AsSpan();

            for (int i = 0; i < expressionSpan.Length;)
            {
                char c = expressionSpan[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (Utility.IsMathChar(c))
                {
                    sanitized.OperationCount++;
                    consecutiveMathChars++;

                    // If multiple operator characters appear in a row, that's a malformed expression.
                    if (consecutiveMathChars > 2)
                    {
                        throw new Exception($"Invalid consecutive operators near position {i}: '{c}'");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else
                {
                    // Reset the consecutive operator counter when we hit a number, variable, or parenthesis
                    consecutiveMathChars = 0;
                }

                if (c == ',')
                {
                    if (scope == 0)
                    {
                        throw new Exception("Unexpected comma found in expression.");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == '(')
                {
                    sanitized.OperationCount++;
                    scope++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == ')')
                {
                    scope--;

                    if (scope < 0)
                    {
                        throw new Exception($"Scope fell below zero while sanitizing input.");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (Utility.IsMathChar(c))
                {
                    sanitized.OperationCount++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (char.IsDigit(c))
                {
                    buffer.Clear();

                    for (; i < expressionSpan.Length; i++)
                    {
                        c = expressionSpan[i];

                        if (char.IsDigit(c) || c == '.')
                        {
                            buffer.Append(c);
                        }
                        else
                        {
                            break;
                        }
                    }

                    var strBuffer = buffer.ToString();

                    if (Utility.IsNumeric(strBuffer) == false)
                    {
                        throw new Exception($"Value is not a number: {strBuffer}");
                    }

                    result.Append(strBuffer);
                    continue;
                }
                else if (Utility.IsValidVariableChar(c))
                {
                    //Parse the variable/function name and determine which it is. If its a function,
                    //then we want to swap out the opening and closing parenthesizes with curly braces.

                    buffer.Clear();
                    bool isFunction = false;

                    for (; i < expressionSpan.Length; i++)
                    {
                        c = expressionSpan[i];

                        if (char.IsWhiteSpace(c))
                        {
                            continue;
                        }
                        else if (Utility.IsValidVariableChar(c))
                        {
                            buffer.Append(c);
                        }
                        else if (c == '(')
                        {
                            isFunction = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    var functionOrVariableName = buffer.ToString();

                    if (isFunction)
                    {
                        result.Append(functionOrVariableName); //Append the function name to the expression.
                        sanitized.OperationCount++;
                        sanitized.DiscoveredFunctions.Add(functionOrVariableName);

                        buffer.Clear();

                        //If its a function, then lets find the opening and closing parenthesizes and replace them with curly braces.
                        int functionScope = 0;

                        for (; i < expressionSpan.Length; i++)
                        {
                            c = expressionSpan[i];

                            if (char.IsWhiteSpace(c))
                            {
                                continue;
                            }

                            if (c == '(')
                            {
                                functionScope++;
                            }
                            else if (c == ')')
                            {
                                functionScope--;

                                if (functionScope == 0)
                                {
                                    buffer.Append(c);
                                    i++; //Consume the closing paren.
                                    break;
                                }
                            }
                            else if (c == ',')
                            {
                                sanitized.OperationCount++;
                            }

                            buffer.Append(c);
                        }

                        if (functionScope != 0)
                        {
                            throw new Exception($"Parenthesizes mismatch when parsing function scope: {functionOrVariableName}");
                        }

                        var subSanitized = Process(buffer.ToString(), options);

                        var functionParameterString = subSanitized.Text;
                        sanitized.OperationCount += subSanitized.OperationCount;

                        foreach (var variable in subSanitized.DiscoveredVariables)
                            sanitized.DiscoveredVariables.Add(variable);

                        foreach (var function in subSanitized.DiscoveredFunctions)
                            sanitized.DiscoveredFunctions.Add(function);

                        if (functionParameterString.StartsWith('(') == false || functionParameterString.EndsWith(')') == false)
                        {
                            throw new Exception($"The function scope should be enclosed in parenthesizes.");
                        }

                        result.AppendFormat("{{{0}}}", functionParameterString[1..^1]);
                    }
                    else
                    {
                        result.Append(functionOrVariableName); //Append the function name to the expression.

                        sanitized.OperationCount++;
                        sanitized.DiscoveredVariables.Add(functionOrVariableName);
                    }
                }
                else if (c == '$')
                {
                    sanitized.OperationCount++;
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else
                {
                    throw new Exception($"Unhandled character {c}");
                }
            }

            if (scope != 0)
            {
                throw new Exception($"Scope mismatch while sanitizing input.");
            }

            if (sanitized.OperationCount == 0)
            {
                sanitized.OperationCount++;
            }

            sanitized.Text = result.ToString();

            return sanitized;
        }
    }
}
