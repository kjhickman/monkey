namespace Kong;

public class Vm
{
    public static readonly BooleanObj True = new() { Value = true };
    public static readonly BooleanObj False = new() { Value = false };
    public static readonly NullObj Null = new();

    public const int StackSize = 2048;
    public const int GlobalsSize = 65536;
    public const int MaxFrames = 1024;

    private readonly List<IObject> _constants;
    private readonly IObject[] _stack;
    private int _sp;

    public DiagnosticBag Diagnostics { get; } = new();
    public IObject[] Globals { get; }

    private readonly Frame[] _frames;
    private int _framesIndex;

    public Vm(Bytecode bytecode) : this(bytecode, new IObject[GlobalsSize])
    {
    }

    private Vm(Bytecode bytecode, IObject[] globals)
    {
        var mainFn = new CompiledFunctionObj { Instructions = bytecode.Instructions };
        var mainClosure = new ClosureObj { Function = mainFn };
        var mainFrame = new Frame(mainClosure, 0);

        _constants = bytecode.Constants;
        _stack = new IObject[StackSize];
        _sp = 0;

        Globals = globals;

        _frames = new Frame[MaxFrames];
        _frames[0] = mainFrame;
        _framesIndex = 1;
    }

    public static Vm NewWithGlobalsStore(Bytecode bytecode, IObject[] globals)
    {
        return new Vm(bytecode, globals);
    }

    private Frame CurrentFrame() => _frames[_framesIndex - 1];

    private void PushFrame(Frame f)
    {
        _frames[_framesIndex] = f;
        _framesIndex++;
    }

    private Frame PopFrame()
    {
        _framesIndex--;
        return _frames[_framesIndex];
    }

    public void Run()
    {
        int ip;
        Instructions ins;
        Opcode op;

        while (CurrentFrame().InstructionPointer < CurrentFrame().Instructions().Count - 1)
        {
            CurrentFrame().InstructionPointer++;

            ip = CurrentFrame().InstructionPointer;
            ins = CurrentFrame().Instructions();
            op = (Opcode)ins[ip];

            switch (op)
            {
                case Opcode.OpConstant:
                {
                    var constIndex = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 2;

                    Push(_constants[constIndex]);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpAdd:
                case Opcode.OpSub:
                case Opcode.OpMul:
                case Opcode.OpDiv:
                {
                    ExecuteBinaryOperation(op);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpEqual:
                case Opcode.OpNotEqual:
                case Opcode.OpGreaterThan:
                {
                    ExecuteComparison(op);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpBang:
                {
                    ExecuteBangOperator();
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpMinus:
                {
                    ExecuteMinusOperator();
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpTrue:
                {
                    Push(True);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpFalse:
                {
                    Push(False);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpNull:
                {
                    Push(Null);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpPop:
                    Pop();
                    break;

                case Opcode.OpJump:
                {
                    var pos = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer = pos - 1;
                    break;
                }

                case Opcode.OpJumpNotTruthy:
                {
                    var pos = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 2;

                    var condition = Pop();
                    if (!IsTruthy(condition))
                    {
                        CurrentFrame().InstructionPointer = pos - 1;
                    }
                    break;
                }

                case Opcode.OpSetGlobal:
                {
                    var globalIndex = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 2;
                    Globals[globalIndex] = Pop();
                    break;
                }

                case Opcode.OpGetGlobal:
                {
                    var globalIndex = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 2;

                    Push(Globals[globalIndex]);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpGetBuiltin:
                {
                    var builtinIndex = Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 1;

                    var definition = Builtins.All[builtinIndex];
                    Push(definition.Builtin);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpClosure:
                {
                    var constIndex = Code.ReadUint16(ins.Bytes, ip + 1);
                    var numFree = Code.ReadUint8(ins.Bytes, ip + 3);
                    CurrentFrame().InstructionPointer += 3;

                    PushClosure(constIndex, numFree);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpGetFree:
                {
                    var freeIndex = Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 1;

                    var currentClosure = CurrentFrame().Closure;
                    Push(currentClosure.Free[freeIndex]);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpCurrentClosure:
                {
                    var currentClosure = CurrentFrame().Closure;
                    Push(currentClosure);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpSetLocal:
                {
                    var localIndex = Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 1;

                    var frame = CurrentFrame();
                    _stack[frame.BasePointer + localIndex] = Pop();
                    break;
                }

                case Opcode.OpGetLocal:
                {
                    var localIndex = Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 1;

                    var frame = CurrentFrame();
                    Push(_stack[frame.BasePointer + localIndex]);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpArray:
                {
                    var numElements = Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 2;

                    var array = BuildArray(_sp - numElements, _sp);
                    _sp -= numElements;

                    Push(array);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpIndex:
                {
                    var index = Pop();
                    var left = Pop();

                    ExecuteIndexExpression(left, index);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpCall:
                {
                    var numArgs = Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().InstructionPointer += 1;

                    ExecuteCall(numArgs);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpReturnValue:
                {
                    var returnValue = Pop();
                    var frame = PopFrame();
                    _sp = frame.BasePointer - 1;

                    Push(returnValue);
                    if (Diagnostics.HasErrors) return;
                    break;
                }

                case Opcode.OpReturn:
                {
                    var frame = PopFrame();
                    _sp = frame.BasePointer - 1;

                    Push(Null);
                    if (Diagnostics.HasErrors) return;
                    break;
                }
            }
        }
    }

    private void ExecuteCall(int numArgs)
    {
        var callee = _stack[_sp - 1 - numArgs];
        switch (callee)
        {
            case ClosureObj cl:
                CallClosure(cl, numArgs);
                return;
            case BuiltinObj builtin:
                CallBuiltin(builtin, numArgs);
                return;
            default:
                Diagnostics.Report(Span.Empty, "calling non-closure and non-builtin", "R001");
                return;
        }
    }

    private void CallClosure(ClosureObj cl, int numArgs)
    {
        if (numArgs != cl.Function.NumParameters)
        {
            Diagnostics.Report(Span.Empty, $"wrong number of arguments: want={cl.Function.NumParameters}, got={numArgs}", "R002");
            return;
        }

        var frame = new Frame(cl, _sp - numArgs);
        PushFrame(frame);
        _sp = frame.BasePointer + cl.Function.NumLocals;
    }

    private void PushClosure(int constIndex, int numFree)
    {
        var constant = _constants[constIndex];
        if (constant is not CompiledFunctionObj function)
        {
            Diagnostics.Report(Span.Empty, $"not a function: {constant}", "R003");
            return;
        }

        var free = new List<IObject>(numFree);
        for (var i = 0; i < numFree; i++)
        {
            free.Add(_stack[_sp - numFree + i]);
        }
        _sp -= numFree;

        var closure = new ClosureObj { Function = function, Free = free };
        Push(closure);
    }

    private void CallBuiltin(BuiltinObj builtin, int numArgs)
    {
        var args = new IObject[numArgs];
        Array.Copy(_stack, _sp - numArgs, args, 0, numArgs);

        var result = builtin.Function(args);
        _sp = _sp - numArgs - 1;

        if (result != null)
        {
            Push(result);
        }
        else
        {
            Push(Null);
        }
    }

    private void Push(IObject o)
    {
        if (_sp >= StackSize)
        {
            Diagnostics.Report(Span.Empty, "stack overflow", "R004");
            return;
        }

        _stack[_sp] = o;
        _sp++;
    }

    private IObject Pop()
    {
        var o = _stack[_sp - 1];
        _sp--;
        return o;
    }

    private void ExecuteBinaryOperation(Opcode op)
    {
        var right = Pop();
        var left = Pop();

        var leftType = left.Type();
        var rightType = right.Type();

        if (leftType == ObjectType.Integer && rightType == ObjectType.Integer)
        {
            ExecuteBinaryIntegerOperation(op, left, right);
            return;
        }

        if (leftType == ObjectType.String && rightType == ObjectType.String)
        {
            ExecuteBinaryStringOperation(op, left, right);
            return;
        }

        Diagnostics.Report(Span.Empty, $"unsupported types for binary operation: {leftType} {rightType}", "R005");
    }

    private void ExecuteBinaryStringOperation(Opcode op, IObject left, IObject right)
    {
        if (op != Opcode.OpAdd)
        {
            Diagnostics.Report(Span.Empty, $"unknown string operator: {op}", "R006");
            return;
        }

        var leftValue = ((StringObj)left).Value;
        var rightValue = ((StringObj)right).Value;

        Push(new StringObj { Value = leftValue + rightValue });
    }

    private void ExecuteBinaryIntegerOperation(Opcode op, IObject left, IObject right)
    {
        var leftValue = ((IntegerObj)left).Value;
        var rightValue = ((IntegerObj)right).Value;

        long result = op switch
        {
            Opcode.OpAdd => leftValue + rightValue,
            Opcode.OpSub => leftValue - rightValue,
            Opcode.OpMul => leftValue * rightValue,
            Opcode.OpDiv => leftValue / rightValue,
            _ => throw new InvalidOperationException($"unknown integer operator: {op}"),
        };

        Push(new IntegerObj { Value = result });
    }

    private void ExecuteBangOperator()
    {
        var operand = Pop();

        if (ReferenceEquals(operand, True))
        {
            Push(False);
            return;
        }
        if (ReferenceEquals(operand, False))
        {
            Push(True);
            return;
        }
        if (ReferenceEquals(operand, Null))
        {
            Push(True);
            return;
        }

        Push(False);
    }

    private void ExecuteMinusOperator()
    {
        var operand = Pop();

        if (operand.Type() != ObjectType.Integer)
        {
            Diagnostics.Report(Span.Empty, $"unsupported type for negation: {operand.Type()}", "R007");
            return;
        }

        var value = ((IntegerObj)operand).Value;
        Push(new IntegerObj { Value = -value });
    }

    private void ExecuteComparison(Opcode op)
    {
        var right = Pop();
        var left = Pop();

        if (left.Type() == ObjectType.Integer && right.Type() == ObjectType.Integer)
        {
            ExecuteIntegerComparison(op, left, right);
            return;
        }

        switch (op)
        {
            case Opcode.OpEqual:
                Push(NativeBoolToBooleanObject(ReferenceEquals(right, left)));
                return;
            case Opcode.OpNotEqual:
                Push(NativeBoolToBooleanObject(!ReferenceEquals(right, left)));
                return;
            default:
                Diagnostics.Report(Span.Empty, $"unknown operator: {op} ({left.Type()} {right.Type()})", "R011");
                return;
        }
    }

    private void ExecuteIntegerComparison(Opcode op, IObject left, IObject right)
    {
        var leftValue = ((IntegerObj)left).Value;
        var rightValue = ((IntegerObj)right).Value;

        switch (op)
        {
            case Opcode.OpEqual:
                Push(NativeBoolToBooleanObject(leftValue == rightValue));
                return;
            case Opcode.OpNotEqual:
                Push(NativeBoolToBooleanObject(leftValue != rightValue));
                return;
            case Opcode.OpGreaterThan:
                Push(NativeBoolToBooleanObject(leftValue > rightValue));
                return;
            default:
                Diagnostics.Report(Span.Empty, $"unknown operator: {op}", "R011");
                return;
        }
    }

    public IObject LastPoppedStackElem() => _stack[_sp];

    private IObject BuildArray(int startIndex, int endIndex)
    {
        var elements = new List<IObject>(endIndex - startIndex);
        for (var i = startIndex; i < endIndex; i++)
        {
            elements.Add(_stack[i]);
        }
        return new ArrayObj { Elements = elements };
    }

    private void ExecuteIndexExpression(IObject left, IObject index)
    {
        if (left.Type() == ObjectType.Array && index.Type() == ObjectType.Integer)
        {
            ExecuteArrayIndex(left, index);
            return;
        }

        Diagnostics.Report(Span.Empty, $"index operator not supported: {left.Type()}", "R009");
    }

    private void ExecuteArrayIndex(IObject array, IObject index)
    {
        var arrayObject = (ArrayObj)array;
        var i = ((IntegerObj)index).Value;
        var max = arrayObject.Elements.Count - 1;

        if (i < 0 || i > max)
        {
            Push(Null);
            return;
        }

        Push(arrayObject.Elements[(int)i]);
    }

    private static BooleanObj NativeBoolToBooleanObject(bool input) => input ? True : False;

    private static bool IsTruthy(IObject obj)
    {
        return obj switch
        {
            BooleanObj b => b.Value,
            NullObj => false,
            _ => true,
        };
    }
}
