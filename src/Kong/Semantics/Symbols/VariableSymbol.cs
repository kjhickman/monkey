namespace Kong.Semantics.Symbols;

public sealed class VariableSymbol(string name, TypeSymbol type) : Symbol(name)
{
    public TypeSymbol Type { get; } = type;
}
