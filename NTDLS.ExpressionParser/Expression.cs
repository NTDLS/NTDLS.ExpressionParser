using System.Text;

namespace NTDLS.ExpressionParser
{
    public class Expression
    {
        private readonly Dictionary<string, double> _definedParameters = new();
        private readonly StringBuilder _replaceRangeBuilder = new();
        private int _nextComputedCacheIndex = 0;
        private int _operationCount = 0;

        internal string Text { get; private set; } = string.Empty;
        internal string WorkingText { get; set; } = string.Empty;
        internal HashSet<string> DiscoveredVariables { get; private set; } = new();
        internal HashSet<string> DiscoveredFunctions { get; private set; } = new();
        internal Dictionary<string, CustomFunction> CustomFunctions { get; private set; } = new();
        internal double[] ComputedCache;

        public delegate double CustomFunction(double[] parameters);

        internal int GetNextComputedCacheIndex()
        {
            return _nextComputedCacheIndex++;
        }

        public Expression(string text)
        {
            Text = Sanitize(text.ToLower());

            ComputedCache = new double[_operationCount];
        }

        public static double Evaluate(string expression) => new Expression(expression).Evaluate();
        public void AddParameter(string name, double value) => _definedParameters.Add(name, value);
        public void ClearParameters() => _definedParameters.Clear();
        public void AddFunction(string name, CustomFunction function) => CustomFunctions.Add(name.ToLower(), function);
        public void ClearFunction() => CustomFunctions.Clear();

        public double Evaluate()
        {
            ResetState();

            bool isComplete = false;

            while (isComplete == false)
            {
                isComplete = AcquireSubexpression(out int startIndex, out int endIndex, out var subExpression);
                var resultString = subExpression.Compute();
                WorkingText = ReplaceRange(WorkingText, startIndex, endIndex, resultString);
            }

            if (WorkingText[0] == '$')
            {
                int index = Utility.StringToUint(WorkingText.Substring(1, WorkingText.Length - 2));
                return ComputedCache[index];
            }

            return Utility.StringToDouble(WorkingText);
        }

        internal void ResetState()
        {
            _nextComputedCacheIndex = 0;
            WorkingText = Text; //Start with a pre-santitized/validated copy of the supplied expression text.

            //Swap out all of the user supplied parameters.
            foreach (var variable in DiscoveredVariables)
            {
                if (_definedParameters.TryGetValue(variable, out var value))
                {
                    WorkingText = WorkingText.Replace(variable, value.ToString());
                }
                else
                {
                    throw new Exception($"Undefiend variable: {variable}");
                }
            }
        }


        internal string ReplaceRange(string original, int startIndex, int endIndex, string replacement)
        {
            _replaceRangeBuilder.Clear();
            int i;

            for (i = 0; i < startIndex; i++)
            {
                _replaceRangeBuilder.Append(original[i]);
            }

            _replaceRangeBuilder.Append(replacement);

            for (i = endIndex + 1; i < original.Length; i++)
            {
                _replaceRangeBuilder.Append(original[i]);
            }

            return _replaceRangeBuilder.ToString();
        }

        /// <summary>
        /// Gets a sub-expression from WorkingText and replaces it with a token.
        /// </summary>
        /// <returns></returns>
        internal bool AcquireSubexpression(out int outStartIndex, out int outEndIndex, out SubExpression outSubExpression)
        {
            int lastParenIndex = WorkingText.LastIndexOf('(');

            string subExpression = string.Empty;

            if (lastParenIndex >= 0)
            {
                outStartIndex = lastParenIndex;

                int scope = 0;
                int i = lastParenIndex;

                for (; i < WorkingText.Length; i++)
                {
                    if (char.IsWhiteSpace(WorkingText[i]))
                    {
                        continue;
                    }
                    else if (WorkingText[i] == '(')
                    {
                        subExpression += WorkingText[i];
                        scope++;
                    }
                    else if (WorkingText[i] == ')')
                    {
                        subExpression += WorkingText[i];
                        scope--;

                        if (scope == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        subExpression += WorkingText[i];
                    }
                }

                if (scope != 0)
                {
                    throw new Exception($"Parenthesises mismatch when parsing subexpression.");
                }

                if (subExpression.StartsWith('(') == false || subExpression.EndsWith(')') == false)
                {
                    throw new Exception($"Subsexpression should be enclosed in parenthesises.");
                }

                outEndIndex = i;

                outSubExpression = new SubExpression(this, subExpression);
                return false;
            }
            else
            {
                outStartIndex = 0;
                outEndIndex = WorkingText.Length - 1;
                subExpression = WorkingText;

                outSubExpression = new SubExpression(this, subExpression);
                return true;
            }
        }

        internal string Sanitize(string expressionText)
        {
            string result = string.Empty;

            int scope = 0;

            for (int i = 0; i < expressionText.Length;)
            {
                if (char.IsWhiteSpace(expressionText[i]))
                {
                    i++;
                    continue;
                }
                else if (expressionText[i] == ',')
                {
                    if (scope == 0)
                    {
                        throw new Exception("Unexpected comma found in expression.");
                    }

                    result += expressionText[i++];
                    continue;
                }
                else if (expressionText[i] == '(')
                {
                    _operationCount++;
                    scope++;
                    result += expressionText[i++];
                    continue;
                }
                else if (expressionText[i] == ')')
                {
                    scope--;

                    if (scope < 0)
                    {
                        throw new Exception($"Scope fell below zero while sanitizing input.");
                    }

                    result += expressionText[i++];
                    continue;
                }
                else if (Utility.IsMathChar(expressionText[i]))
                {
                    _operationCount++;
                    result += expressionText[i++];
                    continue;
                }
                else if (char.IsNumber(expressionText[i]))
                {
                    string buffer = string.Empty;

                    for (; i < expressionText.Length; i++)
                    {
                        if (char.IsNumber(expressionText[i]) || expressionText[i] == '.')
                        {
                            buffer += expressionText[i];
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (Utility.IsNumeric(buffer) == false)
                    {
                        throw new Exception($"Value is not a number: {buffer}");
                    }

                    result += buffer;
                    continue;
                }
                else if (Utility.IsValidVariableChar(expressionText[i]))
                {
                    //Parse the variable/function name and determine which it is. If its a function, then we want to swap out the opening and closing parenthesises with curly braces.

                    string functionOrVariableName = string.Empty;
                    bool isFunction = false;

                    for (; i < expressionText.Length; i++)
                    {
                        if (char.IsWhiteSpace(expressionText[i]))
                        {
                            continue;
                        }
                        else if (Utility.IsValidVariableChar(expressionText[i]))
                        {
                            functionOrVariableName += expressionText[i];
                        }
                        else if (expressionText[i] == '(')
                        {
                            isFunction = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (isFunction)
                    {
                        result += functionOrVariableName; //Append the function name to the expression.

                        DiscoveredFunctions.Add(functionOrVariableName);

                        string functionExpression = string.Empty;
                        //If its a function, then lets find the opening and closing parenthesises and replace them with curly braces.

                        int functionScope = 0;

                        for (; i < expressionText.Length; i++)
                        {
                            if (char.IsWhiteSpace(expressionText[i]))
                            {
                                continue;
                            }
                            else if (expressionText[i] == '(')
                            {
                                functionExpression += expressionText[i];
                                functionScope++;
                            }
                            else if (expressionText[i] == ')')
                            {
                                functionExpression += expressionText[i];
                                functionScope--;

                                if (functionScope == 0)
                                {
                                    i++; //Consume the closing paren.
                                    break;
                                }
                            }
                            else
                            {
                                functionExpression += expressionText[i];
                            }
                        }

                        if (functionScope != 0)
                        {
                            throw new Exception($"Parenthesises mismatch when parsing function scope: {functionOrVariableName}");
                        }

                        var functionParameterString = Sanitize(functionExpression);

                        if (functionParameterString.StartsWith('(') == false || functionParameterString.EndsWith(')') == false)
                        {
                            throw new Exception($"The function scope should be enclosed in parenthesises.");
                        }

                        result += "{" + functionParameterString.Substring(1, functionParameterString.Length - 2) + "}";
                    }
                    else
                    {
                        result += functionOrVariableName; //Append the function name to the expression.

                        DiscoveredVariables.Add(functionOrVariableName);
                    }
                }
                else
                {
                    throw new Exception($"Unhandled character {expressionText[i]}");
                }
            }

            if (scope != 0)
            {
                throw new Exception($"Scope mismatch while sanitizing input.");
            }

            return result;
        }
    }
}
