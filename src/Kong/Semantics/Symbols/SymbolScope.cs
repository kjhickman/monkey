namespace Kong.Semantics.Symbols;

public sealed class SymbolScope(SymbolScope? parent = null)
{
    private readonly Dictionary<string, Symbol> _symbols = [];

    public SymbolScope? Parent { get; } = parent;

    public void Define(Symbol symbol)
    {
        symbol.DeclaringScope = this;
        _symbols[symbol.Name] = symbol;
    }

    public bool IsWithin(SymbolScope ancestor)
    {
        SymbolScope? current = this;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    public bool TryLookup(string name, out Symbol? symbol)
    {
        if (_symbols.TryGetValue(name, out var local))
        {
            symbol = local;
            return true;
        }

        if (Parent is not null)
        {
            return Parent.TryLookup(name, out symbol);
        }

        symbol = null;
        return false;
    }

    public bool TryLookupVariable(string name, out VariableSymbol? symbol)
    {
        if (TryLookup(name, out var resolved) && resolved is VariableSymbol variableSymbol)
        {
            symbol = variableSymbol;
            return true;
        }

        symbol = null;
        return false;
    }

    public bool TryLookupFunction(string name, out FunctionSymbol? symbol)
    {
        if (TryLookup(name, out var resolved) && resolved is FunctionSymbol functionSymbol)
        {
            symbol = functionSymbol;
            return true;
        }

        symbol = null;
        return false;
    }
}
