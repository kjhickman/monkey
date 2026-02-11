using Kong.Code;
using Kong.Compiler;
using Kong.Object;

namespace Kong.Vm;

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

    public IObject[] Globals { get; }

    private readonly Frame[] _frames;
    private int _framesIndex;

    public Vm(Bytecode bytecode) : this(bytecode, new IObject[GlobalsSize])
    {
    }

    private Vm(Bytecode bytecode, IObject[] globals)
    {
        var mainFn = new CompiledFunctionObj { Instructions = bytecode.Instructions };
        var mainClosure = new ClosureObj { Fn = mainFn };
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

    public string? Run()
    {
        int ip;
        Instructions ins;
        Opcode op;

        while (CurrentFrame().Ip < CurrentFrame().Instructions().Count - 1)
        {
            CurrentFrame().Ip++;

            ip = CurrentFrame().Ip;
            ins = CurrentFrame().Instructions();
            op = (Opcode)ins[ip];

            switch (op)
            {
                case Opcode.OpConstant:
                {
                    var constIndex = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;

                    var err = Push(_constants[constIndex]);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpAdd:
                case Opcode.OpSub:
                case Opcode.OpMul:
                case Opcode.OpDiv:
                {
                    var err = ExecuteBinaryOperation(op);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpEqual:
                case Opcode.OpNotEqual:
                case Opcode.OpGreaterThan:
                {
                    var err = ExecuteComparison(op);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpBang:
                {
                    var err = ExecuteBangOperator();
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpMinus:
                {
                    var err = ExecuteMinusOperator();
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpTrue:
                {
                    var err = Push(True);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpFalse:
                {
                    var err = Push(False);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpNull:
                {
                    var err = Push(Null);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpPop:
                    Pop();
                    break;

                case Opcode.OpJump:
                {
                    var pos = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip = pos - 1;
                    break;
                }

                case Opcode.OpJumpNotTruthy:
                {
                    var pos = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;

                    var condition = Pop();
                    if (!IsTruthy(condition))
                    {
                        CurrentFrame().Ip = pos - 1;
                    }
                    break;
                }

                case Opcode.OpSetGlobal:
                {
                    var globalIndex = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;
                    Globals[globalIndex] = Pop();
                    break;
                }

                case Opcode.OpGetGlobal:
                {
                    var globalIndex = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;

                    var err = Push(Globals[globalIndex]);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpGetBuiltin:
                {
                    var builtinIndex = Code.Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 1;

                    var definition = Builtins.All[builtinIndex];
                    var err = Push(definition.Builtin);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpClosure:
                {
                    var constIndex = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    var numFree = Code.Code.ReadUint8(ins.Bytes, ip + 3);
                    CurrentFrame().Ip += 3;

                    var err = PushClosure(constIndex, numFree);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpGetFree:
                {
                    var freeIndex = Code.Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 1;

                    var currentClosure = CurrentFrame().Cl;
                    var err = Push(currentClosure.Free[freeIndex]);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpCurrentClosure:
                {
                    var currentClosure = CurrentFrame().Cl;
                    var err = Push(currentClosure);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpSetLocal:
                {
                    var localIndex = Code.Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 1;

                    var frame = CurrentFrame();
                    _stack[frame.BasePointer + localIndex] = Pop();
                    break;
                }

                case Opcode.OpGetLocal:
                {
                    var localIndex = Code.Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 1;

                    var frame = CurrentFrame();
                    var err = Push(_stack[frame.BasePointer + localIndex]);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpArray:
                {
                    var numElements = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;

                    var array = BuildArray(_sp - numElements, _sp);
                    _sp -= numElements;

                    var err = Push(array);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpHash:
                {
                    var numElements = Code.Code.ReadUint16(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 2;

                    var (hash, hashErr) = BuildHash(_sp - numElements, _sp);
                    if (hashErr != null) return hashErr;
                    _sp -= numElements;

                    var err = Push(hash!);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpIndex:
                {
                    var index = Pop();
                    var left = Pop();

                    var err = ExecuteIndexExpression(left, index);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpCall:
                {
                    var numArgs = Code.Code.ReadUint8(ins.Bytes, ip + 1);
                    CurrentFrame().Ip += 1;

                    var err = ExecuteCall(numArgs);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpReturnValue:
                {
                    var returnValue = Pop();
                    var frame = PopFrame();
                    _sp = frame.BasePointer - 1;

                    var err = Push(returnValue);
                    if (err != null) return err;
                    break;
                }

                case Opcode.OpReturn:
                {
                    var frame = PopFrame();
                    _sp = frame.BasePointer - 1;

                    var err = Push(Null);
                    if (err != null) return err;
                    break;
                }
            }
        }

        return null;
    }

    private string? ExecuteCall(int numArgs)
    {
        var callee = _stack[_sp - 1 - numArgs];
        return callee switch
        {
            ClosureObj cl => CallClosure(cl, numArgs),
            BuiltinObj builtin => CallBuiltin(builtin, numArgs),
            _ => "calling non-closure and non-builtin",
        };
    }

    private string? CallClosure(ClosureObj cl, int numArgs)
    {
        if (numArgs != cl.Fn.NumParameters)
        {
            return $"wrong number of arguments: want={cl.Fn.NumParameters}, got={numArgs}";
        }

        var frame = new Frame(cl, _sp - numArgs);
        PushFrame(frame);
        _sp = frame.BasePointer + cl.Fn.NumLocals;

        return null;
    }

    private string? PushClosure(int constIndex, int numFree)
    {
        var constant = _constants[constIndex];
        if (constant is not CompiledFunctionObj function)
        {
            return $"not a function: {constant}";
        }

        var free = new List<IObject>(numFree);
        for (var i = 0; i < numFree; i++)
        {
            free.Add(_stack[_sp - numFree + i]);
        }
        _sp -= numFree;

        var closure = new ClosureObj { Fn = function, Free = free };
        return Push(closure);
    }

    private string? CallBuiltin(BuiltinObj builtin, int numArgs)
    {
        var args = new IObject[numArgs];
        Array.Copy(_stack, _sp - numArgs, args, 0, numArgs);

        var result = builtin.Fn(args);
        _sp = _sp - numArgs - 1;

        if (result != null)
        {
            var err = Push(result);
            if (err != null) return err;
        }
        else
        {
            var err = Push(Null);
            if (err != null) return err;
        }

        return null;
    }

    private string? Push(IObject o)
    {
        if (_sp >= StackSize)
        {
            return "stack overflow";
        }

        _stack[_sp] = o;
        _sp++;
        return null;
    }

    private IObject Pop()
    {
        var o = _stack[_sp - 1];
        _sp--;
        return o;
    }

    private string? ExecuteBinaryOperation(Opcode op)
    {
        var right = Pop();
        var left = Pop();

        var leftType = left.Type();
        var rightType = right.Type();

        if (leftType == ObjectType.Integer && rightType == ObjectType.Integer)
        {
            return ExecuteBinaryIntegerOperation(op, left, right);
        }

        if (leftType == ObjectType.String && rightType == ObjectType.String)
        {
            return ExecuteBinaryStringOperation(op, left, right);
        }

        return $"unsupported types for binary operation: {leftType} {rightType}";
    }

    private string? ExecuteBinaryStringOperation(Opcode op, IObject left, IObject right)
    {
        if (op != Opcode.OpAdd)
        {
            return $"unknown string operator: {op}";
        }

        var leftValue = ((StringObj)left).Value;
        var rightValue = ((StringObj)right).Value;

        return Push(new StringObj { Value = leftValue + rightValue });
    }

    private string? ExecuteBinaryIntegerOperation(Opcode op, IObject left, IObject right)
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

        return Push(new IntegerObj { Value = result });
    }

    private string? ExecuteBangOperator()
    {
        var operand = Pop();

        if (ReferenceEquals(operand, True))
            return Push(False);
        if (ReferenceEquals(operand, False))
            return Push(True);
        if (ReferenceEquals(operand, Null))
            return Push(True);

        return Push(False);
    }

    private string? ExecuteMinusOperator()
    {
        var operand = Pop();

        if (operand.Type() != ObjectType.Integer)
        {
            return $"unsupported type for negation: {operand.Type()}";
        }

        var value = ((IntegerObj)operand).Value;
        return Push(new IntegerObj { Value = -value });
    }

    private string? ExecuteComparison(Opcode op)
    {
        var right = Pop();
        var left = Pop();

        if (left.Type() == ObjectType.Integer && right.Type() == ObjectType.Integer)
        {
            return ExecuteIntegerComparison(op, left, right);
        }

        return op switch
        {
            Opcode.OpEqual => Push(NativeBoolToBooleanObject(ReferenceEquals(right, left))),
            Opcode.OpNotEqual => Push(NativeBoolToBooleanObject(!ReferenceEquals(right, left))),
            _ => $"unknown operator: {op} ({left.Type()} {right.Type()})",
        };
    }

    private string? ExecuteIntegerComparison(Opcode op, IObject left, IObject right)
    {
        var leftValue = ((IntegerObj)left).Value;
        var rightValue = ((IntegerObj)right).Value;

        return op switch
        {
            Opcode.OpEqual => Push(NativeBoolToBooleanObject(leftValue == rightValue)),
            Opcode.OpNotEqual => Push(NativeBoolToBooleanObject(leftValue != rightValue)),
            Opcode.OpGreaterThan => Push(NativeBoolToBooleanObject(leftValue > rightValue)),
            _ => $"unknown operator: {op}",
        };
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

    private (IObject? hash, string? error) BuildHash(int startIndex, int endIndex)
    {
        var hashedPairs = new Dictionary<HashKey, HashPair>();

        for (var i = startIndex; i < endIndex; i += 2)
        {
            var key = _stack[i];
            var value = _stack[i + 1];

            if (key is not IHashable hashable)
            {
                return (null, $"unusable as hash key: {key.Type()}");
            }

            hashedPairs[hashable.HashKey()] = new HashPair { Key = key, Value = value };
        }

        return (new HashObj { Pairs = hashedPairs }, null);
    }

    private string? ExecuteIndexExpression(IObject left, IObject index)
    {
        if (left.Type() == ObjectType.Array && index.Type() == ObjectType.Integer)
        {
            return ExecuteArrayIndex(left, index);
        }

        if (left.Type() == ObjectType.Hash)
        {
            return ExecuteHashIndex(left, index);
        }

        return $"index operator not supported: {left.Type()}";
    }

    private string? ExecuteArrayIndex(IObject array, IObject index)
    {
        var arrayObject = (ArrayObj)array;
        var i = ((IntegerObj)index).Value;
        var max = arrayObject.Elements.Count - 1;

        if (i < 0 || i > max)
        {
            return Push(Null);
        }

        return Push(arrayObject.Elements[(int)i]);
    }

    private string? ExecuteHashIndex(IObject hash, IObject index)
    {
        var hashObject = (HashObj)hash;

        if (index is not IHashable hashable)
        {
            return $"unusable as hash key: {index.Type()}";
        }

        if (!hashObject.Pairs.TryGetValue(hashable.HashKey(), out var pair))
        {
            return Push(Null);
        }

        return Push(pair.Value);
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
