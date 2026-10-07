using System.Runtime.CompilerServices;

namespace NTDLS.ExpressionParser
{
    /// <summary>
    /// A sanitized expression compiled to a postfix program, which is evaluated on a stack without
    /// any string manipulation or allocation. Instances are immutable and safe to share between threads.
    ///
    /// The compiler mirrors the semantics of the string based evaluator (SubExpression): unary operators
    /// bind tightest, then * / %, then + -, then the third order operators by Utility.ThirdOrderPrecedence,
    /// all left associative. Any input it does not fully understand is rejected, in which case the
    /// expression falls back to the string based evaluator.
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

        private CompiledExpression(Instruction[] program, string[] variables, int maxStackDepth, int maxParameterCount)
        {
            _program = program;
            _variables = variables;
            _maxStackDepth = maxStackDepth;
            _maxParameterCount = maxParameterCount;
        }

        /// <summary>
        /// Compiles the sanitized expression, returning null if it can not be compiled.
        /// </summary>
        public static CompiledExpression? TryCompile(Sanitized sanitized, ExpressionOptions options)
        {
            try
            {
                return new Compiler(sanitized, options).Compile();
            }
            catch
            {
                return null; //Leave it to the string based evaluator, which produces the appropriate error.
            }
        }

        public double? Evaluate(Dictionary<string, double?>? parameters,
            Dictionary<string, ExpressionFunction>? functions, double? defaultNullValue)
        {
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
                        stack[sp - 1] = -stack[sp - 1];
                        break;

                    case OpCode.LogicalNot:
                        stack[sp - 1] = LogicalNot(stack[sp - 1]);
                        break;

                    case OpCode.BitwiseNot:
                        stack[sp - 1] = BitwiseNot(stack[sp - 1]);
                        break;

                    case OpCode.Binary:
                        {
                            var right = stack[--sp];
                            var left = stack[sp - 1];
                            stack[sp - 1] = left == null || right == null ? null
                                : Utility.ComputeBinary(left.GetValueOrDefault(), instruction.BinaryOperator, right.GetValueOrDefault());
                        }
                        break;

                    case OpCode.Call:
                        {
                            int count = instruction.Operand;
                            sp -= count;
                            var result = Call(instruction, stack.Slice(sp, count), parameterValues[..count], functions, defaultNullValue);
                            stack[sp++] = result;
                        }
                        break;
                }
            }

            return stack[0];
        }

        private static double? LogicalNot(double? value) => value == null ? null : value == 0 ? 1 : 0;

        private static double? BitwiseNot(double? value) => value == null ? null : ~(int)value.GetValueOrDefault();

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

        #region Compiler.

        private abstract class Node { }
        private sealed class ConstantNode(double? value) : Node { public readonly double? Value = value; }
        private sealed class VariableNode(int index) : Node { public readonly int Index = index; }
        private sealed class UnaryNode(OpCode code, Node operand) : Node { public readonly OpCode Code = code; public readonly Node Operand = operand; }
        private sealed class BinaryNode(BinaryOperator op, Node left, Node right) : Node
        {
            public readonly BinaryOperator Operator = op; public readonly Node Left = left; public readonly Node Right = right;
        }
        private sealed class CallNode(string name, bool isNative, Node[] parameters) : Node
        {
            public readonly string Name = name; public readonly bool IsNative = isNative; public readonly Node[] Parameters = parameters;
        }

        private sealed class Compiler(Sanitized sanitized, ExpressionOptions options)
        {
            /// <summary>Binding levels: 0 = * / %, 1 = + -, 2 and up = third order operators by precedence.</summary>
            private const int LowestBindingLevel = 9;

            private readonly string _text = sanitized.Text;
            private readonly string[] _variables = sanitized.Variables;
            private int _position;

            private readonly List<Instruction> _program = new();
            private int _depth;
            private int _maxStackDepth;
            private int _maxParameterCount;

            public CompiledExpression Compile()
            {
                var root = ParseBinary(LowestBindingLevel);
                if (_position != _text.Length)
                    throw new Exception($"Unexpected character at position {_position}.");

                Emit(root);
                return new CompiledExpression(_program.ToArray(), _variables, _maxStackDepth, _maxParameterCount);
            }

            #region Parsing.

            private char Current => _position < _text.Length ? _text[_position] : '\0';
            private char Next => _position + 1 < _text.Length ? _text[_position + 1] : '\0';

            private void Expect(char c)
            {
                if (Current != c)
                    throw new Exception($"Expected '{c}' at position {_position}.");
                _position++;
            }

            private Node ParseBinary(int level)
            {
                //Deeply nested input would otherwise overflow the stack, which can not be caught. This throws a
                //  catchable exception instead, so that the expression falls back to the (iterative) string evaluator.
                RuntimeHelpers.EnsureSufficientExecutionStack();

                if (level < 0)
                    return ParseUnary();

                var left = ParseBinary(level - 1);

                while (TryPeekBinaryOperator(out var operation, out int operationLevel) && operationLevel == level)
                {
                    _position += operation.Length;
                    var right = ParseBinary(level - 1);
                    left = Fold(new BinaryNode(Utility.ToBinaryOperator(operation), left, right));
                }

                return left;
            }

            private bool TryPeekBinaryOperator(out string operation, out int level)
            {
                char c = Current;
                if (c == '\0' || c == ')' || c == '}' || c == ',')
                {
                    operation = string.Empty;
                    level = -1;
                    return false;
                }

                var remaining = _text.AsSpan(_position);
                foreach (var thirdOrder in Utility.ThirdOrderOperations)
                {
                    if (remaining.StartsWith(thirdOrder))
                    {
                        operation = thirdOrder;
                        level = 2 + Utility.ThirdOrderPrecedence(thirdOrder);
                        return true;
                    }
                }

                switch (c)
                {
                    case '*': case '/': case '%':
                        operation = Utility.OperatorString(c);
                        level = 0;
                        return true;
                    case '+': case '-':
                        operation = Utility.OperatorString(c);
                        level = 1;
                        return true;
                }

                throw new Exception($"Unexpected character '{c}' at position {_position}.");
            }

            private Node ParseUnary()
            {
                char c = Current;

                if (c == '-' || c == '+')
                {
                    _position++;
                    var operand = ParseUnary();
                    return c == '-' ? Fold(new UnaryNode(OpCode.Negate, operand)) : operand;
                }
                else if (c == '!' && Next != '=')
                {
                    _position++;
                    return Fold(new UnaryNode(OpCode.LogicalNot, ParseUnary()));
                }
                else if (c == '~')
                {
                    _position++;
                    return Fold(new UnaryNode(OpCode.BitwiseNot, ParseUnary()));
                }

                return ParsePrimary();
            }

            private Node ParsePrimary()
            {
                char c = Current;

                if (c == '(')
                {
                    _position++;
                    var inner = ParseBinary(LowestBindingLevel);
                    Expect(')');
                    return inner;
                }
                else if (char.IsAsciiDigit(c) || c == '.')
                {
                    int start = _position;
                    while (char.IsAsciiDigit(Current) || Current == '.')
                        _position++;
                    return new ConstantNode(Utility.ParseNumber(_text.AsSpan(start, _position - start), options.UseFastFloatingPointParser));
                }
                else if (c == '$')
                {
                    //Placeholders in sanitized text are only ever NULL literals.
                    int start = ++_position;
                    while (char.IsAsciiDigit(Current))
                        _position++;
                    int slot = int.Parse(_text.AsSpan(start, _position - start));
                    Expect('$');
                    if (slot >= sanitized.ConsumedPlaceholderCacheSlots)
                        throw new Exception($"Unexpected placeholder: {slot}");
                    return new ConstantNode(options.DefaultNullValue);
                }
                else if (Utility.IsValidVariableChar(c))
                {
                    int start = _position;
                    while (Utility.IsValidVariableChar(Current))
                        _position++;
                    var name = _text.AsSpan(start, _position - start);

                    if (Current == '{')
                    {
                        _position++;
                        return ParseFunctionCall(name);
                    }

                    for (int i = 0; i < _variables.Length; i++)
                    {
                        if (name.SequenceEqual(_variables[i]))
                            return new VariableNode(i);
                    }
                    throw new Exception($"Undefined variable: {name.ToString()}");
                }

                throw new Exception($"Unexpected character '{c}' at position {_position}.");
            }

            private Node ParseFunctionCall(ReadOnlySpan<char> name)
            {
                string? functionName = null;
                foreach (var function in sanitized.DiscoveredFunctions)
                {
                    if (name.SequenceEqual(function))
                    {
                        functionName = function;
                        break;
                    }
                }
                if (functionName == null)
                    throw new Exception($"Undefined function: {name.ToString()}");

                var parameters = new List<Node>();
                if (Current == '}')
                {
                    _position++;
                }
                else
                {
                    while (true)
                    {
                        parameters.Add(ParseBinary(LowestBindingLevel));
                        if (Current == ',')
                        {
                            _position++;
                            continue;
                        }
                        Expect('}');
                        break;
                    }
                }

                return Fold(new CallNode(functionName, Utility.IsNativeFunction(functionName), parameters.ToArray()));
            }

            #endregion

            #region Constant folding.

            /// <summary>
            /// Replaces a node whose operands are all constant with its computed value. Operations that would throw
            /// are left in place, so that the error is raised at evaluation time just like the string based evaluator.
            /// </summary>
            private static Node Fold(Node node)
            {
                try
                {
                    switch (node)
                    {
                        case UnaryNode { Operand: ConstantNode operand } unary:
                            return new ConstantNode(unary.Code switch
                            {
                                OpCode.Negate => -operand.Value,
                                OpCode.LogicalNot => LogicalNot(operand.Value),
                                _ => BitwiseNot(operand.Value)
                            });

                        case BinaryNode { Left: ConstantNode left, Right: ConstantNode right } binary:
                            return new ConstantNode(left.Value == null || right.Value == null ? null
                                : Utility.ComputeBinary(left.Value.Value, binary.Operator, right.Value.Value));

                        case CallNode { IsNative: true } call when Utility.IsDeterministicNativeFunction(call.Name)
                            && call.Parameters.All(p => p is ConstantNode):
                            {
                                var values = call.Parameters.Select(p => ((ConstantNode)p).Value).ToArray();
                                if (values.Any(v => v == null))
                                    return new ConstantNode(null);
                                return new ConstantNode(Utility.ComputeNativeFunction(call.Name, values.Select(v => v!.Value).ToArray()));
                            }
                    }
                }
                catch
                {
                    //Keep the operation so that it throws when evaluated.
                }

                return node;
            }

            #endregion

            #region Emitting.

            private void Emit(Node node)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (node)
                {
                    case ConstantNode constant:
                        Push(new Instruction(OpCode.Constant, constant: constant.Value));
                        break;

                    case VariableNode variable:
                        Push(new Instruction(OpCode.Variable, operand: variable.Index));
                        break;

                    case UnaryNode unary:
                        Emit(unary.Operand);
                        _program.Add(new Instruction(unary.Code));
                        break;

                    case BinaryNode binary:
                        Emit(binary.Left);
                        Emit(binary.Right);
                        _program.Add(new Instruction(OpCode.Binary, binaryOperator: binary.Operator));
                        _depth--;
                        break;

                    case CallNode call:
                        foreach (var parameter in call.Parameters)
                            Emit(parameter);
                        _maxParameterCount = Math.Max(_maxParameterCount, call.Parameters.Length);
                        _depth -= call.Parameters.Length;
                        Push(new Instruction(OpCode.Call, operand: call.Parameters.Length,
                            functionName: call.Name, isNativeFunction: call.IsNative));
                        break;

                    default:
                        throw new Exception("Unexpected node.");
                }
            }

            private void Push(Instruction instruction)
            {
                _program.Add(instruction);
                _depth++;
                _maxStackDepth = Math.Max(_maxStackDepth, _depth);
            }

            #endregion
        }

        #endregion
    }
}
