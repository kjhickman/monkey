using Kong.Common;
using Kong.Semantic;

namespace Kong.CodeGeneration;

public readonly record struct IrValueId(int Id)
{
    public override string ToString() => $"v{Id}";
}

public readonly record struct IrLocalId(int Id)
{
    public override string ToString() => $"l{Id}";
}

public sealed class IrProgram
{
    public IrFunction EntryPoint { get; set; } = null!;
    public List<IrFunction> Functions { get; } = [];
}

public sealed class IrFunction
{
    public required string Name { get; set; }
    public required TypeSymbol ReturnType { get; init; }
    public int CaptureParameterCount { get; set; }
    public List<IrParameter> Parameters { get; } = [];
    public List<IrBlock> Blocks { get; } = [];
    public Dictionary<IrValueId, TypeSymbol> ValueTypes { get; } = [];
    public Dictionary<IrLocalId, TypeSymbol> LocalTypes { get; } = [];
}

public sealed record IrParameter(IrLocalId LocalId, string Name, TypeSymbol Type);

public sealed class IrBlock
{
    public required int Id { get; init; }
    public List<IrInstruction> Instructions { get; } = [];
    public IrTerminator Terminator { get; set; } = null!;
}

public abstract record IrInstruction;

public sealed record IrConstInt(IrValueId Destination, long Value) : IrInstruction;

public sealed record IrConstDouble(IrValueId Destination, double Value) : IrInstruction;

public sealed record IrConstBool(IrValueId Destination, bool Value) : IrInstruction;

public sealed record IrConstString(IrValueId Destination, string Value) : IrInstruction;

public enum IrBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    LessThan,
    GreaterThan,
    Equal,
    NotEqual,
}

public sealed record IrBinary(
    IrValueId Destination,
    IrBinaryOperator Operator,
    IrValueId Left,
    IrValueId Right) : IrInstruction;

public sealed record IrStoreLocal(IrLocalId Local, IrValueId Source) : IrInstruction;

public sealed record IrLoadLocal(IrValueId Destination, IrLocalId Local) : IrInstruction;

public sealed record IrCall(IrValueId Destination, string FunctionName, IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record IrCallVoid(string FunctionName, IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record IrStaticCall(
    IrValueId Destination,
    string MethodPath,
    IReadOnlyList<IrValueId> Arguments,
    IReadOnlyList<TypeSymbol> ArgumentTypes) : IrInstruction;

public sealed record IrStaticCallVoid(
    string MethodPath,
    IReadOnlyList<IrValueId> Arguments,
    IReadOnlyList<TypeSymbol> ArgumentTypes) : IrInstruction;

public sealed record IrInstanceCall(
    IrValueId Destination,
    IrValueId Receiver,
    TypeSymbol ReceiverType,
    string MemberName,
    IReadOnlyList<IrValueId> Arguments,
    IReadOnlyList<TypeSymbol> ArgumentTypes) : IrInstruction;

public sealed record IrInstanceCallVoid(
    IrValueId Receiver,
    TypeSymbol ReceiverType,
    string MemberName,
    IReadOnlyList<IrValueId> Arguments,
    IReadOnlyList<TypeSymbol> ArgumentTypes) : IrInstruction;

public sealed record IrStaticValueGet(
    IrValueId Destination,
    string MemberPath) : IrInstruction;

public sealed record IrInstanceValueGet(
    IrValueId Destination,
    IrValueId Receiver,
    TypeSymbol ReceiverType,
    string MemberName) : IrInstruction;

public sealed record IrCreateClosure(
    IrValueId Destination,
    string FunctionName,
    IReadOnlyList<IrLocalId> CapturedLocals) : IrInstruction;

public sealed record IrInvokeClosure(
    IrValueId Destination,
    IrValueId Closure,
    IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record IrInvokeClosureVoid(
    IrValueId Closure,
    IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record IrNewArray(IrValueId Destination, TypeSymbol ElementType, IReadOnlyList<IrValueId> Elements) : IrInstruction;

public sealed record IrArrayIndex(IrValueId Destination, IrValueId Array, IrValueId Index, TypeSymbol ElementType) : IrInstruction;

public sealed record IrNewObject(
    IrValueId Destination,
    TypeSymbol ObjectType,
    IReadOnlyList<IrValueId> Arguments,
    IReadOnlyList<TypeSymbol> ArgumentTypes) : IrInstruction;

public abstract record IrTerminator;

public sealed record IrReturn(IrValueId Value) : IrTerminator;

public sealed record IrReturnVoid() : IrTerminator;

public sealed record IrJump(int TargetBlockId) : IrTerminator;

public sealed record IrBranch(IrValueId Condition, int ThenBlockId, int ElseBlockId) : IrTerminator;

public sealed class IrLoweringResult
{
    public IrProgram? Program { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}
