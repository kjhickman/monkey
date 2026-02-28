namespace Kong.Semantics.Symbols;

public sealed class FunctionSymbol(string name, IReadOnlyList<VariableSymbol> parameters, TypeSymbol returnType) : Symbol(name)
{
    public IReadOnlyList<VariableSymbol> Parameters { get; } = parameters;

    public TypeSymbol ReturnType { get; private set; } = returnType;

    public FunctionTypeSymbol Type => new(Parameters.Select(p => p.Type).ToList(), ReturnType);

    public void SetReturnType(TypeSymbol returnType)
    {
        ReturnType = returnType;
    }
}
