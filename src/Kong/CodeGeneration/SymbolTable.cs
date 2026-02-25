namespace Kong.CodeGeneration;

public enum SymbolScope
{
    Global,
    Local,
    Builtin,
    Free,
    Function,
}

public record struct Symbol(string Name, SymbolScope Scope, int Index);

public class SymbolTable
{
    public SymbolTable? Outer { get; set; }
    public List<Symbol> FreeSymbols { get; set; } = [];

    private readonly Dictionary<string, Symbol> _store = [];
    public int NumDefinitions { get; private set; }

    public static SymbolTable NewSymbolTable() => new();

    public static SymbolTable NewEnclosedSymbolTable(SymbolTable outer) =>
        new() { Outer = outer };

    public Symbol Define(string name)
    {
        var symbol = new Symbol(name, Outer == null ? SymbolScope.Global : SymbolScope.Local, NumDefinitions);
        _store[name] = symbol;
        NumDefinitions++;
        return symbol;
    }

    public Symbol DefineBuiltin(int index, string name)
    {
        var symbol = new Symbol(name, SymbolScope.Builtin, index);
        _store[name] = symbol;
        return symbol;
    }

    public Symbol DefineFunctionName(string name)
    {
        var symbol = new Symbol(name, SymbolScope.Function, 0);
        _store[name] = symbol;
        return symbol;
    }

    private Symbol DefineFree(Symbol original)
    {
        FreeSymbols.Add(original);
        var symbol = new Symbol(original.Name, SymbolScope.Free, FreeSymbols.Count - 1);
        _store[original.Name] = symbol;
        return symbol;
    }

    public (Symbol symbol, bool ok) Resolve(string name)
    {
        if (_store.TryGetValue(name, out var obj))
        {
            return (obj, true);
        }

        if (Outer != null)
        {
            var (outerObj, ok) = Outer.Resolve(name);
            if (!ok)
            {
                return (outerObj, false);
            }

            if (outerObj.Scope is SymbolScope.Global or SymbolScope.Builtin)
            {
                return (outerObj, true);
            }

            var free = DefineFree(outerObj);
            return (free, true);
        }

        return (default, false);
    }
}
