using System.Text;

namespace NTDLS.ExpressionParser
{
    internal static class Sanitizer
    {
        public static Sanitized Process(string expressionText, ExpressionOptions options)
        {
            //'$' delimits internal placeholders (such as NULL literals), so it can not be accepted from the user.
            int reservedIndex = expressionText.IndexOf('$');
            if (reservedIndex >= 0)
            {
                throw new Exception($"Unhandled character '$' at position {reservedIndex}.");
            }

            var sanitized = new Sanitized();

            var result = new StringBuilder();
            var buffer = new StringBuilder();

            //Open groupings: '(' for parentheses, '{' for function calls (whose parentheses are emitted as braces).
            //  This is tracked explicitly, rather than by recursion, so that no depth of nesting can overflow the stack.
            var brackets = new Stack<char>();
            int consecutiveMathChars = 0;
            bool isAfterWhitespace = false;

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

            sanitized.ConsumedPlaceholderCacheSlots = sanitized.OperationCount;

            var expressionSpan = expressionText.AsSpan();

            for (int i = 0; i < expressionSpan.Length;)
            {
                char c = expressionSpan[i];

                if (char.IsWhiteSpace(c))
                {
                    isAfterWhitespace = true;
                    i++;
                    continue;
                }

                //Two operands separated only by whitespace (e.g. "a b" or "2 3") are missing an operator,
                //  reject them rather than silently joining them into a single name or number.
                if (isAfterWhitespace && result.Length > 0 && IsOperandEnd(result[^1])
                    && (Utility.IsValidVariableChar(c) || c == '$'))
                {
                    throw new Exception($"Missing operator between operands near position {i}: '{c}'");
                }
                isAfterWhitespace = false;

                if (Utility.IsMathChar(c))
                {
                    sanitized.OperationCount++;
                    consecutiveMathChars++;

                    // If multiple operator characters appear in a row, that's a malformed expression.
                    if (consecutiveMathChars > 3)
                    {
                        throw new Exception($"Invalid consecutive operators near position {i}: '{c}'");
                    }

                    if ((c == '-' || c == '+') && result.Length > 0 && (result[^1] == '-' || result[^1] == '+'))
                    {
                        //Consecutive signs multiply, and "a - -b" is "a + b", so a run of signs collapses to a single sign.
                        result[^1] = (result[^1] == '-') == (c == '-') ? '+' : '-';
                        i++;
                        continue;
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
                    if (brackets.Count == 0 || brackets.Peek() != '{')
                    {
                        throw new Exception("Unexpected comma found in expression.");
                    }

                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == '(')
                {
                    sanitized.OperationCount++;
                    brackets.Push('(');
                    result.Append(expressionSpan[i++]);
                    continue;
                }
                else if (c == ')')
                {
                    if (brackets.Count == 0)
                    {
                        throw new Exception($"Scope fell below zero while sanitizing input.");
                    }

                    result.Append(brackets.Pop() == '{' ? '}' : ')');
                    i++;
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
                            //Whitespace ends the name, but is allowed between a function name and its parenthesis.
                            int next = i;
                            while (next < expressionSpan.Length && char.IsWhiteSpace(expressionSpan[next]))
                                next++;

                            if (next < expressionSpan.Length && expressionSpan[next] == '(')
                            {
                                i = next;
                                isFunction = true;
                            }
                            break;
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
                        //The function's parentheses are emitted as braces, so that calls are distinguishable from grouping.
                        result.Append(functionOrVariableName).Append('{');
                        sanitized.OperationCount++;
                        sanitized.DiscoveredFunctions.Add(functionOrVariableName);
                        brackets.Push('{');
                        i++; //Consume the opening parenthesis.
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

            if (brackets.Count != 0)
            {
                throw new Exception($"Scope mismatch while sanitizing input.");
            }

            if (sanitized.OperationCount == 0)
            {
                sanitized.OperationCount++;
            }

            sanitized.Text = result.ToString();

            Validate(sanitized);
            return sanitized;
        }

        /// <summary>
        /// Verifies that the sanitized text is a well formed sequence of operands and operators with balanced
        /// grouping, so that malformed input produces a clear error rather than failing deep inside evaluation.
        /// This is iterative so that deeply nested input can not overflow the stack.
        /// </summary>
        private static void Validate(Sanitized sanitized)
        {
            var text = sanitized.Text;
            var openers = new Stack<char>();
            bool expectOperand = true;
            int i = 0;

            Exception SyntaxError(string problem)
                => new($"Syntax error: {problem} at position {i} of '{text}'.");

            while (i < text.Length)
            {
                char c = text[i];

                if (expectOperand)
                {
                    if (c == '-' || c == '+' || c == '~' || (c == '!' && (i + 1 >= text.Length || text[i + 1] != '=')))
                    {
                        i++; //Unary operator, still expecting the operand.
                    }
                    else if (c == '(')
                    {
                        openers.Push('(');
                        i++;
                    }
                    else if (char.IsAsciiDigit(c) || c == '.')
                    {
                        while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                            i++;
                        expectOperand = false;
                    }
                    else if (c == '$')
                    {
                        int start = ++i;
                        while (i < text.Length && char.IsAsciiDigit(text[i]))
                            i++;
                        if (i == start || i >= text.Length || text[i] != '$'
                            || int.Parse(text.AsSpan(start, i - start)) >= sanitized.ConsumedPlaceholderCacheSlots)
                        {
                            throw SyntaxError("invalid placeholder");
                        }
                        i++;
                        expectOperand = false;
                    }
                    else if (Utility.IsValidVariableChar(c))
                    {
                        while (i < text.Length && Utility.IsValidVariableChar(text[i]))
                            i++;

                        if (i < text.Length && text[i] == '{')
                        {
                            openers.Push('{');
                            i++;
                            if (i < text.Length && text[i] == '}')
                            {
                                openers.Pop(); //Function without parameters.
                                i++;
                                expectOperand = false;
                            }
                        }
                        else
                        {
                            expectOperand = false;
                        }
                    }
                    else if (c == ')' && i > 0 && text[i - 1] == '(')
                    {
                        throw SyntaxError("empty parentheses");
                    }
                    else if (c == ',' || c == '}')
                    {
                        throw SyntaxError("missing function parameter");
                    }
                    else
                    {
                        throw SyntaxError($"missing operand before '{c}'");
                    }
                }
                else
                {
                    if (c == ')' || c == '}')
                    {
                        char expected = c == ')' ? '(' : '{';
                        if (openers.Count == 0 || openers.Pop() != expected)
                            throw SyntaxError($"unbalanced '{c}'");
                        i++;
                    }
                    else if (c == ',')
                    {
                        if (openers.Count == 0 || openers.Peek() != '{')
                            throw SyntaxError("unexpected ','");
                        i++;
                        expectOperand = true;
                    }
                    else if (TryMatchBinaryOperator(text.AsSpan(i), out int length))
                    {
                        i += length;
                        expectOperand = true;
                    }
                    else
                    {
                        throw SyntaxError($"missing operator before '{c}'");
                    }
                }
            }

            if (expectOperand)
                throw SyntaxError("missing operand at end of expression");

            if (openers.Count > 0)
                throw SyntaxError($"unclosed '{openers.Peek()}'");
        }

        private static bool TryMatchBinaryOperator(ReadOnlySpan<char> text, out int length)
        {
            foreach (var operation in Utility.ThirdOrderOperations)
            {
                if (text.StartsWith(operation))
                {
                    length = operation.Length;
                    return true;
                }
            }

            length = 1;
            return text[0] is '*' or '/' or '%' or '+' or '-';
        }

        /// <summary>
        /// Returns true when the character can be the last character of an operand (number, name, placeholder or group).
        /// </summary>
        private static bool IsOperandEnd(char c)
            => Utility.IsValidVariableChar(c) || c == '.' || c == ')' || c == '}' || c == '$';
    }
}
