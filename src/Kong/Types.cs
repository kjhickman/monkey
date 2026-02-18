namespace Kong;

public abstract class TypeSymbol
{
    public abstract string Name { get; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class IntTypeSymbol : TypeSymbol
{
    public static IntTypeSymbol Instance { get; } = new();

    private IntTypeSymbol()
    {
    }

    public override string Name => "int";
}

public sealed class StringTypeSymbol : TypeSymbol
{
    public static StringTypeSymbol Instance { get; } = new();

    private StringTypeSymbol()
    {
    }

    public override string Name => "string";
}

public sealed class BoolTypeSymbol : TypeSymbol
{
    public static BoolTypeSymbol Instance { get; } = new();

    private BoolTypeSymbol()
    {
    }

    public override string Name => "bool";
}

public sealed class VoidTypeSymbol : TypeSymbol
{
    public static VoidTypeSymbol Instance { get; } = new();

    private VoidTypeSymbol()
    {
    }

    public override string Name => "void";
}

public sealed class NullTypeSymbol : TypeSymbol
{
    public static NullTypeSymbol Instance { get; } = new();

    private NullTypeSymbol()
    {
    }

    public override string Name => "null";
}

public sealed class ArrayTypeSymbol(TypeSymbol elementType) : TypeSymbol
{
    public TypeSymbol ElementType { get; } = elementType;

    public override string Name => $"{ElementType}[]";
}

public sealed class FunctionTypeSymbol(IReadOnlyList<TypeSymbol> parameterTypes, TypeSymbol returnType) : TypeSymbol
{
    public IReadOnlyList<TypeSymbol> ParameterTypes { get; } = parameterTypes;
    public TypeSymbol ReturnType { get; } = returnType;

    public override string Name => $"fn({string.Join(", ", ParameterTypes)}) -> {ReturnType}";
}

public static class TypeSymbols
{
    public static IntTypeSymbol Int { get; } = IntTypeSymbol.Instance;
    public static StringTypeSymbol String { get; } = StringTypeSymbol.Instance;
    public static BoolTypeSymbol Bool { get; } = BoolTypeSymbol.Instance;
    public static VoidTypeSymbol Void { get; } = VoidTypeSymbol.Instance;
    public static NullTypeSymbol Null { get; } = NullTypeSymbol.Instance;

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
