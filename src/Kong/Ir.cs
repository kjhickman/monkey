namespace Kong;

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
    public IrFunction EntryPoint { get; init; } = null!;
    public List<IrFunction> Functions { get; } = [];
}

public sealed class IrFunction
{
    public required string Name { get; init; }
    public required TypeSymbol ReturnType { get; init; }
    public int CaptureParameterCount { get; set; }
    public List<IrParameter> Parameters { get; } = [];
    public List<IrBlock> Blocks { get; } = [];
    public Dictionary<IrValueId, TypeSymbol> ValueTypes { get; } = [];
    public Dictionary<IrLocalId, TypeSymbol> LocalTypes { get; } = [];
}

public sealed record class IrParameter(IrLocalId LocalId, string Name, TypeSymbol Type);

public sealed class IrBlock
{
    public required int Id { get; init; }
    public List<IrInstruction> Instructions { get; } = [];
    public IrTerminator Terminator { get; set; } = null!;
}

public abstract record class IrInstruction;

public sealed record class IrConstInt(IrValueId Destination, long Value) : IrInstruction;

public sealed record class IrConstBool(IrValueId Destination, bool Value) : IrInstruction;

public sealed record class IrConstString(IrValueId Destination, string Value) : IrInstruction;

public enum IrBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
}

public sealed record class IrBinary(
    IrValueId Destination,
    IrBinaryOperator Operator,
    IrValueId Left,
    IrValueId Right) : IrInstruction;

public sealed record class IrStoreLocal(IrLocalId Local, IrValueId Source) : IrInstruction;

public sealed record class IrLoadLocal(IrValueId Destination, IrLocalId Local) : IrInstruction;

public sealed record class IrCall(IrValueId Destination, string FunctionName, IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record class IrCallVoid(string FunctionName, IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record class IrCreateClosure(
    IrValueId Destination,
    string FunctionName,
    IReadOnlyList<IrLocalId> CapturedLocals) : IrInstruction;

public sealed record class IrInvokeClosure(
    IrValueId Destination,
    IrValueId Closure,
    IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record class IrInvokeClosureVoid(
    IrValueId Closure,
    IReadOnlyList<IrValueId> Arguments) : IrInstruction;

public sealed record class IrNewIntArray(IrValueId Destination, IReadOnlyList<IrValueId> Elements) : IrInstruction;

public sealed record class IrIntArrayIndex(IrValueId Destination, IrValueId Array, IrValueId Index) : IrInstruction;

public abstract record class IrTerminator;

public sealed record class IrReturn(IrValueId Value) : IrTerminator;

public sealed record class IrReturnVoid() : IrTerminator;

public sealed record class IrJump(int TargetBlockId) : IrTerminator;

public sealed record class IrBranch(IrValueId Condition, int ThenBlockId, int ElseBlockId) : IrTerminator;

public sealed class IrLoweringResult
{
    public IrProgram? Program { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}
