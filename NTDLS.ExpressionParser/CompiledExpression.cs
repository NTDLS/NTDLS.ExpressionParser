using System.Text;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// A sanitized expression compiled to a postfix program, which is evaluated on a stack without any
    /// string manipulation or allocation. Instances are immutable and safe to share between threads.
    ///
    /// Precedence: unary operators bind tightest, then * / %, then + -, then the third order operators by
    /// Utility.ThirdOrderPrecedence. Binary operators are left associative. The compiler is iterative
    /// (shunting-yard), so no depth of nesting can overflow the stack.
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
        private readonly Sanitized _sanitized;
        private readonly ExpressionOptions _options;

        /// <summary>
        /// The same program without constant folding, so that showing the work includes every operation.
        /// Created on first use; a race only results in an identical program being compiled twice.
        /// </summary>
        private CompiledExpression? _unfolded;

        private CompiledExpression Unfolded => _unfolded ??= new Compiler(_sanitized, _options, fold: false).Compile();

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

        private CompiledExpression(Instruction[] program, Sanitized sanitized, ExpressionOptions options,
            int maxStackDepth, int maxParameterCount)
        {
            _program = program;
            _sanitized = sanitized;
            _options = options;
            _variables = sanitized.Variables;
            _maxStackDepth = maxStackDepth;
            _maxParameterCount = maxParameterCount;
        }

        public static CompiledExpression Compile(Sanitized sanitized, ExpressionOptions options)
            => new Compiler(sanitized, options, fold: true).Compile();

        #region Evaluation.

        public double? Evaluate(Dictionary<string, double?>? parameters,
            Dictionary<string, ExpressionFunction>? functions)
            => Run(parameters, functions, null);

        /// <summary>
        /// Evaluates the expression, describing each operation as it is performed.
        /// </summary>
        public double? Evaluate(Dictionary<string, double?>? parameters,
            Dictionary<string, ExpressionFunction>? functions, out string showWork)
        {
            var unfolded = Unfolded;

            var work = new Work(new StringBuilder(), $"G{_options.Precision}");
            work.Builder.AppendLine("{");
            var result = unfolded.Run(parameters, functions, work);
            work.Builder.Append("} = ").AppendLine(work.Format(result));

            showWork = work.Builder.ToString();
            return result;
        }

        private double? Run(Dictionary<string, double?>? parameters,
            Dictionary<string, ExpressionFunction>? functions, Work? work)
        {
            double? defaultNullValue = _options.DefaultNullValue;

            //Every discovered variable must be defined, even if constant folding made it irrelevant.
            Span<double?> variables = _variables.Length <= 32 ? stackalloc double?[_variables.Length] : new double?[_variables.Length];
            for (int i = 0; i < _variables.Length; i++)
            {
                if (parameters == null || !parameters.TryGetValue(_variables[i], out var value))
                    throw new Exception($"Undefined variable: {_variables[i]}");
                variables[i] = value ?? defaultNullValue;
            }

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

        private sealed class Compiler(Sanitized sanitized, ExpressionOptions options, bool fold)
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

            private readonly string _text = sanitized.Text;
            private readonly string[] _variables = sanitized.Variables;
            private readonly List<Instruction> _output = new();
            private readonly List<Pending> _pending = new();
            private int _maxParameterCount;

            public CompiledExpression Compile()
            {
                bool expectOperand = true;
                int i = 0;

                while (i < _text.Length)
                {
                    char c = _text[i];

                    if (expectOperand)
                    {
                        if (c == '+')
                        {
                            i++; //Unary plus has no effect.
                        }
                        else if (c == '-' || c == '~' || (c == '!' && (i + 1 >= _text.Length || _text[i + 1] != '=')))
                        {
                            _pending.Add(new Pending
                            {
                                Kind = PendingKind.Unary,
                                UnaryCode = c == '-' ? OpCode.Negate : c == '!' ? OpCode.LogicalNot : OpCode.BitwiseNot
                            });
                            i++;
                        }
                        else if (c == '(')
                        {
                            _pending.Add(new Pending { Kind = PendingKind.Parenthesis });
                            i++;
                        }
                        else if (char.IsAsciiDigit(c) || c == '.')
                        {
                            int start = i;
                            while (i < _text.Length && (char.IsAsciiDigit(_text[i]) || _text[i] == '.'))
                                i++;
                            var value = Utility.ParseNumber(_text.AsSpan(start, i - start), options.UseFastFloatingPointParser);
                            _output.Add(new Instruction(OpCode.Constant, constant: value));
                            expectOperand = false;
                        }
                        else if (c == '$')
                        {
                            //Placeholders in sanitized text are only ever NULL literals.
                            i = _text.IndexOf('$', i + 1) + 1;
                            _output.Add(new Instruction(OpCode.Constant, constant: options.DefaultNullValue));
                            expectOperand = false;
                        }
                        else if (Utility.IsValidVariableChar(c))
                        {
                            int start = i;
                            while (i < _text.Length && Utility.IsValidVariableChar(_text[i]))
                                i++;
                            var name = _text.AsSpan(start, i - start);

                            if (i < _text.Length && _text[i] == '{')
                            {
                                var functionName = name.ToString();
                                i++;
                                if (_text[i] == '}')
                                {
                                    i++;
                                    EmitCall(functionName, 0);
                                    expectOperand = false;
                                }
                                else
                                {
                                    _pending.Add(new Pending { Kind = PendingKind.Call, FunctionName = functionName });
                                }
                            }
                            else
                            {
                                _output.Add(new Instruction(OpCode.Variable, operand: IndexOfVariable(name)));
                                expectOperand = false;
                            }
                        }
                        else
                        {
                            throw new Exception($"Syntax error: unexpected '{c}' at position {i} of '{_text}'.");
                        }
                    }
                    else
                    {
                        if (c == ')')
                        {
                            PopUntil(PendingKind.Parenthesis);
                            _pending.RemoveAt(_pending.Count - 1);
                            i++;
                        }
                        else if (c == '}' || c == ',')
                        {
                            PopUntil(PendingKind.Call);
                            var call = _pending[^1];
                            call.ParameterCount++;
                            i++;

                            if (c == ',')
                            {
                                _pending[^1] = call;
                                expectOperand = true;
                            }
                            else
                            {
                                _pending.RemoveAt(_pending.Count - 1);
                                EmitCall(call.FunctionName, call.ParameterCount);
                            }
                        }
                        else
                        {
                            var operation = MatchBinaryOperator(i, out int level);

                            //Unary operators bind tightest; binary operators are left associative.
                            while (_pending.Count > 0 && (_pending[^1].Kind == PendingKind.Unary
                                || (_pending[^1].Kind == PendingKind.Binary && _pending[^1].Level <= level)))
                            {
                                EmitPending();
                            }

                            _pending.Add(new Pending
                            {
                                Kind = PendingKind.Binary,
                                BinaryOperator = Utility.ToBinaryOperator(operation),
                                Level = level
                            });
                            i += operation.Length;
                            expectOperand = true;
                        }
                    }
                }

                while (_pending.Count > 0)
                {
                    if (_pending[^1].Kind is PendingKind.Parenthesis or PendingKind.Call)
                        throw new Exception($"Syntax error: unbalanced grouping in '{_text}'.");
                    EmitPending();
                }

                var program = _output.ToArray();
                return new CompiledExpression(program, sanitized, options, MaxStackDepth(program), _maxParameterCount);
            }

            private int IndexOfVariable(ReadOnlySpan<char> name)
            {
                for (int v = 0; v < _variables.Length; v++)
                {
                    if (name.SequenceEqual(_variables[v]))
                        return v;
                }
                throw new Exception($"Undefined variable: {name.ToString()}");
            }

            private string MatchBinaryOperator(int i, out int level)
            {
                var remaining = _text.AsSpan(i);
                foreach (var operation in Utility.ThirdOrderOperations)
                {
                    if (remaining.StartsWith(operation))
                    {
                        level = 2 + Utility.ThirdOrderPrecedence(operation);
                        return operation;
                    }
                }

                char c = _text[i];
                level = c is '*' or '/' or '%' ? 0 : c is '+' or '-' ? 1
                    : throw new Exception($"Syntax error: unexpected '{c}' at position {i} of '{_text}'.");
                return Utility.OperatorString(c);
            }

            private void PopUntil(PendingKind kind)
            {
                while (_pending.Count > 0 && _pending[^1].Kind != kind)
                {
                    if (_pending[^1].Kind is PendingKind.Parenthesis or PendingKind.Call)
                        throw new Exception($"Syntax error: unbalanced grouping in '{_text}'.");
                    EmitPending();
                }
                if (_pending.Count == 0)
                    throw new Exception($"Syntax error: unbalanced grouping in '{_text}'.");
            }

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
        }

        #endregion
    }
}
