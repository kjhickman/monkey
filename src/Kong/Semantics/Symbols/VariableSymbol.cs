namespace Kong.Semantics.Symbols;

public sealed class VariableSymbol(string name, TypeSymbol type) : Symbol(name)
{
    public TypeSymbol Type { get; private set; } = type;

    public void SetType(TypeSymbol type)
    {
        Type = type;
    }
}
