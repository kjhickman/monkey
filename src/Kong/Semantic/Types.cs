namespace Kong.Semantic;

public abstract record TypeSymbol
{
    public abstract string Name { get; }

    public sealed override string ToString()
    {
        return Name;
    }
}

public sealed record IntTypeSymbol : TypeSymbol
{
    public static IntTypeSymbol Instance { get; } = new();

    private IntTypeSymbol()
    {
    }

    public override string Name => "int";
}

public sealed record StringTypeSymbol : TypeSymbol
{
    public static StringTypeSymbol Instance { get; } = new();

    private StringTypeSymbol()
    {
    }

    public override string Name => "string";
}

public sealed record BoolTypeSymbol : TypeSymbol
{
    public static BoolTypeSymbol Instance { get; } = new();

    private BoolTypeSymbol()
    {
    }

    public override string Name => "bool";
}

public sealed record VoidTypeSymbol : TypeSymbol
{
    public static VoidTypeSymbol Instance { get; } = new();

    private VoidTypeSymbol()
    {
    }

    public override string Name => "void";
}

public sealed record NullTypeSymbol : TypeSymbol
{
    public static NullTypeSymbol Instance { get; } = new();

    private NullTypeSymbol()
    {
    }

    public override string Name => "null";
}

public sealed record ErrorTypeSymbol : TypeSymbol
{
    public static ErrorTypeSymbol Instance { get; } = new();

    private ErrorTypeSymbol()
    {
    }

    public override string Name => "<error>";
}

public sealed record ArrayTypeSymbol(TypeSymbol ElementType) : TypeSymbol
{
    public override string Name => $"{ElementType}[]";
}

public sealed record FunctionTypeSymbol(IReadOnlyList<TypeSymbol> ParameterTypes, TypeSymbol ReturnType) : TypeSymbol
{
    public override string Name => $"fn({string.Join(", ", ParameterTypes)}) -> {ReturnType}";
}

public static class TypeSymbols
{
    public static IntTypeSymbol Int { get; } = IntTypeSymbol.Instance;
    public static StringTypeSymbol String { get; } = StringTypeSymbol.Instance;
    public static BoolTypeSymbol Bool { get; } = BoolTypeSymbol.Instance;
    public static VoidTypeSymbol Void { get; } = VoidTypeSymbol.Instance;
    public static NullTypeSymbol Null { get; } = NullTypeSymbol.Instance;
    public static ErrorTypeSymbol Error { get; } = ErrorTypeSymbol.Instance;

    public static TypeSymbol? TryGetBuiltin(string name)
    {
        return name switch
        {
            "int" => Int,
            "string" => String,
            "bool" => Bool,
            "void" => Void,
            "null" => Null,
            _ => null,
        };
    }
}
