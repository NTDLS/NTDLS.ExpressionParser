using System.Text;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// An expression compiled to a postfix program, which is evaluated on a stack without any string
    /// manipulation or allocation. Instances are immutable and safe to share between threads.
    ///
    /// The expression text is parsed, validated and compiled in a single pass (shunting-yard), which is
    /// iterative so that no depth of nesting can overflow the stack.
    ///
    /// Precedence: unary operators bind tightest, then * / %, then + -, then the third order operators by
    /// Utility.ThirdOrderPrecedence. Binary operators are left associative.
    /// </summary>
    internal sealed class CompiledExpression
    {
        private enum OpCode : byte
        {
            Constant,
            Variable,
            Negate,
            LogicalNot,
            BitwiseNot,
            Binary,
            Call
        }

        private readonly struct Instruction(OpCode code, double? constant = null, int operand = 0,
            BinaryOperator binaryOperator = default, string? functionName = null, bool isNativeFunction = false)
        {
            public readonly OpCode Code = code;
            public readonly double? Constant = constant;
            /// <summary>Variable index, or parameter count for a function call.</summary>
            public readonly int Operand = operand;
            public readonly BinaryOperator BinaryOperator = binaryOperator;
            public readonly string? FunctionName = functionName;
            public readonly bool IsNativeFunction = isNativeFunction;
        }

        private readonly Instruction[] _program;
        private readonly string[] _variables;
        private readonly int _maxStackDepth;
        private readonly int _maxParameterCount;
        private readonly string _text;
        private readonly ExpressionOptions _options;

        /// <summary>
        /// The same program without constant folding, so that showing the work includes every operation.
        /// Created on first use; a race only results in an identical program being compiled twice.
        /// </summary>
        private CompiledExpression? _unfolded;

        private CompiledExpression Unfolded => _unfolded ??= new Compiler(_text, _options, fold: false).Compile();

        /// <summary>
        /// The number of operations (unary operators, binary operators and function calls) that the expression
        /// performs as written, i.e. before constant folding. A negated number literal is not counted.
        /// </summary>
        public int OperationCount
        {
            get
            {
                int count = 0;
                foreach (var instruction in Unfolded._program)
                {
                    if (instruction.Code is not (OpCode.Constant or OpCode.Variable))
                        count++;
                }
                return count;
            }
        }

        private CompiledExpression(Instruction[] program, string[] variables, string text, ExpressionOptions options,
            int maxStackDepth, int maxParameterCount)
        {
            _program = program;
            _variables = variables;
            _text = text;
            _options = options;
            _maxStackDepth = maxStackDepth;
            _maxParameterCount = maxParameterCount;
        }

        /// <summary>
        /// Parses and compiles the expression text, throwing for any syntax error.
        /// </summary>
        public static CompiledExpression Compile(string text, ExpressionOptions options)
            => new Compiler(text, options, fold: true).Compile();

        /// <summary>
        /// The number of distinct variables. Variable values are passed to Evaluate() by index.
        /// </summary>
        public int VariableCount => _variables.Length;

        /// <summary>
        /// Returns the index of the variable (case insensitive), or -1 when the expression does not use it.
        /// Expressions have few variables, so a linear search is faster than a dictionary to build and query.
        /// </summary>
        public int IndexOfVariable(string name)
        {
            for (int i = 0; i < _variables.Length; i++)
            {
                if (string.Equals(_variables[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public string VariableName(int index) => _variables[index];

        #region Evaluation.

        /// <param name="variables">The value of each variable, by index. Every variable must have been given a value.</param>
        /// <param name="functions">Custom functions, by lower case name.</param>
        public double? Evaluate(ReadOnlySpan<double?> variables, Dictionary<string, ExpressionFunction>? functions)
            => Run(variables, functions, null);

        /// <summary>
        /// Evaluates the expression, describing each operation as it is performed.
        /// </summary>
        public double? Evaluate(ReadOnlySpan<double?> variables,
            Dictionary<string, ExpressionFunction>? functions, out string showWork)
        {
            var unfolded = Unfolded;

            var work = new Work(new StringBuilder(), $"G{_options.Precision}");
            work.Builder.AppendLine("{");
            var result = unfolded.Run(variables, functions, work);
            work.Builder.Append("} = ").AppendLine(work.Format(result));

            showWork = work.Builder.ToString();
            return result;
        }

        private double? Run(ReadOnlySpan<double?> variables,
            Dictionary<string, ExpressionFunction>? functions, Work? work)
        {
            double? defaultNullValue = _options.DefaultNullValue;

            if (_program.Length == 1 && _program[0].Code == OpCode.Constant)
                return _program[0].Constant; //Fully folded.

            //Buffers are sized exactly, because stackalloc zero-initializes everything it allocates.
            Span<double?> stack = _maxStackDepth <= 64 ? stackalloc double?[_maxStackDepth] : new double?[_maxStackDepth];
            Span<double> parameterValues = _maxParameterCount <= 32 ? stackalloc double[_maxParameterCount] : new double[_maxParameterCount];
            int sp = 0;

            var program = _program;
            for (int pc = 0; pc < program.Length; pc++)
            {
                ref readonly var instruction = ref program[pc];

                switch (instruction.Code)
                {
                    case OpCode.Constant:
                        stack[sp++] = instruction.Constant;
                        break;

                    case OpCode.Variable:
                        stack[sp++] = variables[instruction.Operand];
                        break;

                    case OpCode.Negate:
                    case OpCode.LogicalNot:
                    case OpCode.BitwiseNot:
                        {
                            var operand = stack[sp - 1];
                            var result = ComputeUnary(instruction.Code, operand);
                            stack[sp - 1] = result;
                            work?.Unary(instruction.Code, operand, result);
                        }
                        break;

                    case OpCode.Binary:
                        {
                            var right = stack[--sp];
                            var left = stack[sp - 1];
                            double? result = left == null || right == null ? null
                                : Utility.ComputeBinary(left.GetValueOrDefault(), instruction.BinaryOperator, right.GetValueOrDefault());
                            stack[sp - 1] = result;
                            work?.Binary(left, instruction.BinaryOperator, right, result);
                        }
                        break;

                    case OpCode.Call:
                        {
                            int count = instruction.Operand;
                            sp -= count;
                            var arguments = stack.Slice(sp, count);
                            var result = Call(instruction, arguments, parameterValues[..count], functions, defaultNullValue);
                            work?.Call(instruction.FunctionName!, arguments, result);
                            stack[sp++] = result;
                        }
                        break;
                }
            }

            return stack[0];
        }

        private static double? ComputeUnary(OpCode code, double? value)
        {
            if (value == null)
                return null;

            return code switch
            {
                OpCode.Negate => -value.GetValueOrDefault(),
                OpCode.LogicalNot => value.GetValueOrDefault() == 0 ? 1 : 0,
                _ => ~(int)value.GetValueOrDefault()
            };
        }

        private static double? Call(in Instruction instruction, ReadOnlySpan<double?> arguments, Span<double> parameterValues,
            Dictionary<string, ExpressionFunction>? functions, double? defaultNullValue)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == null)
                    return null; //Any NULL parameter yields NULL, without calling the function.
                parameterValues[i] = arguments[i].GetValueOrDefault();
            }

            if (instruction.IsNativeFunction)
                return Utility.ComputeNativeFunction(instruction.FunctionName!, parameterValues);

            if (functions != null && functions.TryGetValue(instruction.FunctionName!, out var customFunction))
                return customFunction.Invoke(parameterValues.ToArray()) ?? defaultNullValue;

            throw new Exception($"Undefined function: {instruction.FunctionName}");
        }

        /// <summary>
        /// Writes one line per operation when showing the work.
        /// </summary>
        private sealed class Work(StringBuilder builder, string precisionFormat)
        {
            public readonly StringBuilder Builder = builder;

            public string Format(double? value) => value?.ToString(precisionFormat) ?? "null";

            public void Unary(OpCode code, double? operand, double? result)
            {
                char symbol = code switch { OpCode.Negate => '-', OpCode.LogicalNot => '!', _ => '~' };
                string text = Format(operand);
                //Parenthesize a negative operand so that "-(-3)" is not shown as "--3".
                if (text.StartsWith('-'))
                    text = $"({text})";
                Builder.AppendLine($"    {symbol}{text} = {Format(result)}");
            }

            public void Binary(double? left, BinaryOperator operation, double? right, double? result)
                => Builder.AppendLine($"    {Format(left)}{Utility.ToText(operation)}{Format(right)} = {Format(result)}");

            public void Call(string functionName, ReadOnlySpan<double?> arguments, double? result)
            {
                Builder.Append("    ").Append(functionName).Append('(');
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                        Builder.Append(',');
                    Builder.Append(Format(arguments[i]));
                }
                Builder.Append(") = ").AppendLine(Format(result));
            }
        }

        #endregion

        #region Compiler.

        private sealed class Compiler(string text, ExpressionOptions options, bool fold)
        {
            private enum PendingKind : byte { Unary, Binary, Parenthesis, Call }

            /// <summary>An operator, grouping, or function call waiting on the operator stack.</summary>
            private struct Pending
            {
                public PendingKind Kind;
                public OpCode UnaryCode;
                public BinaryOperator BinaryOperator;
                /// <summary>Binding level of a binary operator: 0 = * / %, 1 = + -, 2 and up = third order.</summary>
                public int Level;
                public string FunctionName;
                public int ParameterCount;
            }

            /// <summary>
            /// More operator characters than this in a row (ignoring whitespace) is rejected as malformed.
            /// </summary>
            private const int MaxConsecutiveOperatorChars = 3;

            private readonly string _text = text;
            /// <summary>
            /// Working lists, reused by each thread's compilations to avoid allocating them for every expression.
            /// Compilation never runs re-entrantly on a thread, so one set per thread is enough.
            /// </summary>
            [ThreadStatic] private static List<Instruction>? t_output;
            [ThreadStatic] private static List<Pending>? t_pending;
            [ThreadStatic] private static List<string>? t_variables;

            /// <summary>
            /// Lists that grew beyond this (from an unusually large expression) are not kept for reuse.
            /// </summary>
            private const int MaxRetainedCapacity = 256;

            private readonly List<Instruction> _output = Rent(ref t_output);
            private readonly List<Pending> _pending = Rent(ref t_pending);
            private readonly List<string> _variables = Rent(ref t_variables);

            private static List<T> Rent<T>(ref List<T>? cached)
            {
                var list = cached ?? new List<T>(16);
                cached = null; //Taken, until returned.
                list.Clear();
                return list;
            }

            private static void Return<T>(ref List<T>? cached, List<T> list)
            {
                if (list.Capacity <= MaxRetainedCapacity)
                {
                    list.Clear(); //Don't keep strings or function names alive.
                    cached = list;
                }
            }
            private int _maxParameterCount;

            private int _position;
            private int _consecutiveOperatorChars;

            private Exception SyntaxError(string problem, int position)
                => new($"Syntax error: {problem} at position {position} of '{_text}'.");

            public CompiledExpression Compile()
            {
                try
                {
                    return CompileExpression();
                }
                finally
                {
                    Return(ref t_output, _output);
                    Return(ref t_pending, _pending);
                    Return(ref t_variables, _variables);
                }
            }

            private CompiledExpression CompileExpression()
            {
                bool expectOperand = true;
                bool isAfterOpenParenthesis = false;

                while (true)
                {
                    SkipWhitespace();
                    if (_position >= _text.Length)
                        break;

                    char c = _text[_position];

                    if (c == '$')
                    {
                        //Reserved, as it once delimited internal placeholders.
                        throw new Exception($"Unhandled character '$' at position {_position}.");
                    }

                    if (!Utility.IsMathChar(c))
                        _consecutiveOperatorChars = 0;

                    if (expectOperand)
                    {
                        bool wasAfterOpenParenthesis = isAfterOpenParenthesis;
                        isAfterOpenParenthesis = false;

                        if (c == '+' || c == '-')
                        {
                            //Consecutive signs multiply, so a run of them collapses to a single sign.
                            if (ConsumeSigns() == '-')
                                _pending.Add(new Pending { Kind = PendingKind.Unary, UnaryCode = OpCode.Negate });
                        }
                        else if (c == '~' || (c == '!' && !IsAt("!=")))
                        {
                            ConsumeOperatorChar();
                            _pending.Add(new Pending { Kind = PendingKind.Unary, UnaryCode = c == '!' ? OpCode.LogicalNot : OpCode.BitwiseNot });
                        }
                        else if (c == '(')
                        {
                            _pending.Add(new Pending { Kind = PendingKind.Parenthesis });
                            _position++;
                            isAfterOpenParenthesis = true;
                        }
                        else if (char.IsAsciiDigit(c))
                        {
                            _output.Add(new Instruction(OpCode.Constant, constant: ReadNumber()));
                            expectOperand = false;
                        }
                        else if (char.IsAsciiLetter(c) || c == '_')
                        {
                            expectOperand = ReadIdentifier();
                        }
                        else if (c == ')' && wasAfterOpenParenthesis && _pending[^1].Kind == PendingKind.Parenthesis)
                        {
                            throw SyntaxError("empty parentheses", _position);
                        }
                        else if (c == ',' && IsInFunctionCall())
                        {
                            throw SyntaxError("missing function parameter", _position);
                        }
                        else if (c == ')' && IsInFunctionCall() && _pending[^1].Kind == PendingKind.Call && _pending[^1].ParameterCount > 0)
                        {
                            throw SyntaxError("missing function parameter", _position);
                        }
                        else if (Utility.IsMathChar(c) || c == ')' || c == ',')
                        {
                            throw SyntaxError($"missing operand before '{c}'", _position);
                        }
                        else
                        {
                            throw SyntaxError($"unexpected character '{c}'", _position);
                        }
                    }
                    else
                    {
                        if (c == ')')
                        {
                            CloseGrouping();
                            _position++;
                        }
                        else if (c == ',')
                        {
                            PopOperators();
                            if (_pending.Count == 0 || _pending[^1].Kind != PendingKind.Call)
                                throw SyntaxError("unexpected ','", _position);

                            var call = _pending[^1];
                            call.ParameterCount++;
                            _pending[^1] = call;
                            _position++;
                            expectOperand = true;
                        }
                        else if (Utility.IsMathChar(c) && c != '!' && c != '~' || IsAt("!="))
                        {
                            var operation = ReadBinaryOperator(out int level);

                            //Unary operators bind tightest; binary operators are left associative.
                            while (_pending.Count > 0 && (_pending[^1].Kind == PendingKind.Unary
                                || (_pending[^1].Kind == PendingKind.Binary && _pending[^1].Level <= level)))
                            {
                                EmitPending();
                            }

                            _pending.Add(new Pending { Kind = PendingKind.Binary, BinaryOperator = operation, Level = level });
                            expectOperand = true;
                        }
                        else if (char.IsAsciiLetterOrDigit(c) || c == '_' || c == '(' || c == '.' || c == '!' || c == '~')
                        {
                            throw SyntaxError($"missing operator before '{c}'", _position);
                        }
                        else
                        {
                            throw SyntaxError($"unexpected character '{c}'", _position);
                        }
                    }
                }

                if (expectOperand)
                    throw SyntaxError("missing operand at end of expression", _position);

                while (_pending.Count > 0)
                {
                    if (_pending[^1].Kind is PendingKind.Parenthesis or PendingKind.Call)
                        throw SyntaxError("unclosed '('", _position);
                    EmitPending();
                }

                var program = _output.ToArray();
                return new CompiledExpression(program, _variables.ToArray(), _text, options, MaxStackDepth(program), _maxParameterCount);
            }

            #region Lexing.

            private void SkipWhitespace()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                    _position++;
            }

            private bool IsAt(string value) => _text.AsSpan(_position).StartsWith(value);

            private void ConsumeOperatorChar()
            {
                if (++_consecutiveOperatorChars > MaxConsecutiveOperatorChars)
                    throw new Exception($"Invalid consecutive operators near position {_position}: '{_text[_position]}'");
                _position++;
            }

            /// <summary>
            /// Consumes a run of '+' and '-' (whitespace between them is allowed), returning the resulting sign.
            /// </summary>
            private char ConsumeSigns()
            {
                bool isNegative = false;
                while (true)
                {
                    char c = _text[_position];
                    isNegative ^= c == '-';
                    ConsumeOperatorChar();

                    int afterSign = _position;
                    SkipWhitespace();
                    if (_position >= _text.Length || (_text[_position] != '-' && _text[_position] != '+'))
                    {
                        _position = afterSign;
                        return isNegative ? '-' : '+';
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _position;
                int decimalPoints = 0;
                while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] == '.'))
                {
                    if (_text[_position] == '.')
                        decimalPoints++;
                    _position++;
                }

                var number = _text.AsSpan(start, _position - start);
                if (decimalPoints > 1 || number[^1] == '.')
                    throw new Exception($"Value is not a number: {number.ToString()}");

                return Utility.ParseNumber(number, options.UseFastFloatingPointParser);
            }

            /// <summary>
            /// Reads a variable, the null keyword or the start of a function call. Returns whether an operand is
            /// still expected (true after the opening parenthesis of a function with parameters).
            /// </summary>
            private bool ReadIdentifier()
            {
                int start = _position;
                while (_position < _text.Length && (char.IsAsciiLetterOrDigit(_text[_position]) || _text[_position] == '_'))
                    _position++;
                var name = _text.AsSpan(start, _position - start);

                if (name.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    _output.Add(new Instruction(OpCode.Constant, constant: options.DefaultNullValue));
                    return false;
                }

                //A function call, which may have whitespace before its parenthesis.
                int afterName = _position;
                SkipWhitespace();
                if (_position < _text.Length && _text[_position] == '(')
                {
                    var functionName = FunctionName(name);
                    _position++;

                    SkipWhitespace();
                    if (_position < _text.Length && _text[_position] == ')')
                    {
                        _position++;
                        EmitCall(functionName, 0);
                        return false;
                    }

                    _pending.Add(new Pending { Kind = PendingKind.Call, FunctionName = functionName });
                    return true;
                }
                _position = afterName;

                _output.Add(new Instruction(OpCode.Variable, operand: VariableIndex(name)));
                return false;
            }

            /// <summary>
            /// Returns the lower case function name, using the shared string for native functions.
            /// </summary>
            private static string FunctionName(ReadOnlySpan<char> name)
            {
                foreach (var native in Utility.NativeFunctions)
                {
                    if (name.Equals(native, StringComparison.OrdinalIgnoreCase))
                        return native;
                }
                return name.ToString().ToLowerInvariant();
            }

            private int VariableIndex(ReadOnlySpan<char> name)
            {
                for (int v = 0; v < _variables.Count; v++)
                {
                    if (name.Equals(_variables[v], StringComparison.OrdinalIgnoreCase))
                        return v;
                }
                _variables.Add(name.ToString().ToLowerInvariant());
                return _variables.Count - 1;
            }

            private BinaryOperator ReadBinaryOperator(out int level)
            {
                char c = _text[_position];

                if (c == '+' || c == '-')
                {
                    //A run of signs after an operand is one binary operator: "a - -b" is "a + b".
                    level = 1;
                    return ConsumeSigns() == '-' ? BinaryOperator.Subtract : BinaryOperator.Add;
                }

                foreach (var operation in Utility.ThirdOrderOperations)
                {
                    if (IsAt(operation))
                    {
                        for (int i = 0; i < operation.Length; i++)
                            ConsumeOperatorChar();
                        level = 2 + Utility.ThirdOrderPrecedence(operation);
                        return Utility.ToBinaryOperator(operation);
                    }
                }

                ConsumeOperatorChar();
                level = 0;
                return c switch
                {
                    '*' => BinaryOperator.Multiply,
                    '/' => BinaryOperator.Divide,
                    '%' => BinaryOperator.Modulus,
                    _ => throw SyntaxError($"unexpected '{c}'", _position - 1)
                };
            }

            #endregion

            #region Grouping.

            private bool IsInFunctionCall()
            {
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (_pending[i].Kind == PendingKind.Call)
                        return true;
                    if (_pending[i].Kind == PendingKind.Parenthesis)
                        return false;
                }
                return false;
            }

            /// <summary>
            /// Emits operators until the innermost grouping (parenthesis or function call) is on top.
            /// </summary>
            private void PopOperators()
            {
                while (_pending.Count > 0 && _pending[^1].Kind is PendingKind.Unary or PendingKind.Binary)
                    EmitPending();
            }

            /// <summary>
            /// Handles ')', which closes either a parenthesized group or a function call.
            /// </summary>
            private void CloseGrouping()
            {
                PopOperators();
                if (_pending.Count == 0)
                    throw SyntaxError("unbalanced ')'", _position);

                var grouping = _pending[^1];
                _pending.RemoveAt(_pending.Count - 1);

                if (grouping.Kind == PendingKind.Call)
                    EmitCall(grouping.FunctionName, grouping.ParameterCount + 1);
            }

            #endregion

            #region Emitting.

            private void EmitPending()
            {
                var pending = _pending[^1];
                _pending.RemoveAt(_pending.Count - 1);

                if (pending.Kind == PendingKind.Unary)
                    EmitOperation(new Instruction(pending.UnaryCode), 1);
                else
                    EmitOperation(new Instruction(OpCode.Binary, binaryOperator: pending.BinaryOperator), 2);
            }

            private void EmitCall(string functionName, int parameterCount)
            {
                _maxParameterCount = Math.Max(_maxParameterCount, parameterCount);
                EmitOperation(new Instruction(OpCode.Call, operand: parameterCount, functionName: functionName,
                    isNativeFunction: Utility.IsNativeFunction(functionName)), parameterCount);
            }

            /// <summary>
            /// Appends an operation to the program, first replacing it with its computed value when all of its
            /// operands are constant. In postfix order those operands are exactly the preceding instructions.
            /// Operations that would throw are kept, so that the error is raised by Evaluate() rather than here.
            /// </summary>
            private void EmitOperation(Instruction instruction, int operandCount)
            {
                //A negated literal is the literal itself, so it is folded even when showing the work.
                bool canFold = fold || (instruction.Code == OpCode.Negate && _output[^1].Code == OpCode.Constant);

                if (canFold && _output.Count >= operandCount && TryFold(instruction, operandCount, out var value))
                {
                    _output.RemoveRange(_output.Count - operandCount, operandCount);
                    _output.Add(new Instruction(OpCode.Constant, constant: value));
                    return;
                }

                _output.Add(instruction);
            }

            private bool TryFold(in Instruction instruction, int operandCount, out double? value)
            {
                value = null;

                for (int i = _output.Count - operandCount; i < _output.Count; i++)
                {
                    if (_output[i].Code != OpCode.Constant)
                        return false;
                }

                if (instruction.Code == OpCode.Call
                    && (!instruction.IsNativeFunction || !Utility.IsDeterministicNativeFunction(instruction.FunctionName!)))
                {
                    return false;
                }

                try
                {
                    switch (instruction.Code)
                    {
                        case OpCode.Negate:
                        case OpCode.LogicalNot:
                        case OpCode.BitwiseNot:
                            value = ComputeUnary(instruction.Code, _output[^1].Constant);
                            return true;

                        case OpCode.Binary:
                            var left = _output[^2].Constant;
                            var right = _output[^1].Constant;
                            value = left == null || right == null ? null
                                : Utility.ComputeBinary(left.Value, instruction.BinaryOperator, right.Value);
                            return true;

                        case OpCode.Call:
                            var parameters = new double[operandCount];
                            for (int i = 0; i < operandCount; i++)
                            {
                                var parameter = _output[_output.Count - operandCount + i].Constant;
                                if (parameter == null)
                                    return true; //NULL in, NULL out.
                                parameters[i] = parameter.Value;
                            }
                            value = Utility.ComputeNativeFunction(instruction.FunctionName!, parameters);
                            return true;
                    }
                }
                catch
                {
                    //Keep the operation so that it throws when evaluated.
                }

                return false;
            }

            private static int MaxStackDepth(Instruction[] program)
            {
                int depth = 0, max = 0;
                foreach (var instruction in program)
                {
                    depth += instruction.Code switch
                    {
                        OpCode.Constant or OpCode.Variable => 1,
                        OpCode.Binary => -1,
                        OpCode.Call => 1 - instruction.Operand,
                        _ => 0
                    };
                    max = Math.Max(max, depth);
                }
                return max;
            }

            #endregion
        }

        #endregion
    }
}
