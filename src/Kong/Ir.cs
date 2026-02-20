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
}

public sealed class IrFunction
{
    public required string Name { get; init; }
    public required TypeSymbol ReturnType { get; init; }
    public List<IrBlock> Blocks { get; } = [];
    public Dictionary<IrValueId, TypeSymbol> ValueTypes { get; } = [];
    public Dictionary<IrLocalId, TypeSymbol> LocalTypes { get; } = [];
}

public sealed class IrBlock
{
    public required int Id { get; init; }
    public List<IrInstruction> Instructions { get; } = [];
    public IrTerminator Terminator { get; set; } = null!;
}

public abstract record class IrInstruction;

public sealed record class IrConstInt(IrValueId Destination, long Value) : IrInstruction;

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

public abstract record class IrTerminator;

public sealed record class IrReturn(IrValueId Value) : IrTerminator;

public sealed class IrLoweringResult
{
    public IrProgram? Program { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}
