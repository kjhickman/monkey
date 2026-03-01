namespace Kong.Semantics.Symbols;

public sealed class VariableSymbol(string name, TypeSymbol type, bool isMutable = false) : Symbol(name)
{
    public TypeSymbol Type { get; private set; } = type;

    public bool IsMutable { get; } = isMutable;

    public void SetType(TypeSymbol type)
    {
        Type = type;
    }
}
