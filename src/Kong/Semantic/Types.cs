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

public sealed record LongTypeSymbol : TypeSymbol
{
    public static LongTypeSymbol Instance { get; } = new();

    private LongTypeSymbol()
    {
    }

    public override string Name => "long";
}

public sealed record DoubleTypeSymbol : TypeSymbol
{
    public static DoubleTypeSymbol Instance { get; } = new();

    private DoubleTypeSymbol()
    {
    }

    public override string Name => "double";
}

public sealed record CharTypeSymbol : TypeSymbol
{
    public static CharTypeSymbol Instance { get; } = new();

    private CharTypeSymbol()
    {
    }

    public override string Name => "char";
}

public sealed record ByteTypeSymbol : TypeSymbol
{
    public static ByteTypeSymbol Instance { get; } = new();

    private ByteTypeSymbol()
    {
    }

    public override string Name => "byte";
}

public sealed record SByteTypeSymbol : TypeSymbol
{
    public static SByteTypeSymbol Instance { get; } = new();

    private SByteTypeSymbol()
    {
    }

    public override string Name => "sbyte";
}

public sealed record ShortTypeSymbol : TypeSymbol
{
    public static ShortTypeSymbol Instance { get; } = new();

    private ShortTypeSymbol()
    {
    }

    public override string Name => "short";
}

public sealed record UShortTypeSymbol : TypeSymbol
{
    public static UShortTypeSymbol Instance { get; } = new();

    private UShortTypeSymbol()
    {
    }

    public override string Name => "ushort";
}

public sealed record UIntTypeSymbol : TypeSymbol
{
    public static UIntTypeSymbol Instance { get; } = new();

    private UIntTypeSymbol()
    {
    }

    public override string Name => "uint";
}

public sealed record ULongTypeSymbol : TypeSymbol
{
    public static ULongTypeSymbol Instance { get; } = new();

    private ULongTypeSymbol()
    {
    }

    public override string Name => "ulong";
}

public sealed record NIntTypeSymbol : TypeSymbol
{
    public static NIntTypeSymbol Instance { get; } = new();

    private NIntTypeSymbol()
    {
    }

    public override string Name => "nint";
}

public sealed record NUIntTypeSymbol : TypeSymbol
{
    public static NUIntTypeSymbol Instance { get; } = new();

    private NUIntTypeSymbol()
    {
    }

    public override string Name => "nuint";
}

public sealed record FloatTypeSymbol : TypeSymbol
{
    public static FloatTypeSymbol Instance { get; } = new();

    private FloatTypeSymbol()
    {
    }

    public override string Name => "float";
}

public sealed record DecimalTypeSymbol : TypeSymbol
{
    public static DecimalTypeSymbol Instance { get; } = new();

    private DecimalTypeSymbol()
    {
    }

    public override string Name => "decimal";
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

public sealed record EnumTypeSymbol(string EnumName, IReadOnlyList<TypeSymbol> TypeArguments) : TypeSymbol
{
    public override string Name => TypeArguments.Count == 0
        ? EnumName
        : $"{EnumName}<{string.Join(", ", TypeArguments)}>";
}

public sealed record GenericParameterTypeSymbol(string ParameterName) : TypeSymbol
{
    public override string Name => ParameterName;
}

public sealed record ClassTypeSymbol(string ClassName) : TypeSymbol
{
    public override string Name => ClassName;
}

public sealed record InterfaceTypeSymbol(string InterfaceName) : TypeSymbol
{
    public override string Name => InterfaceName;
}

public sealed record EnumVariantDefinition(string Name, IReadOnlyList<TypeSymbol> PayloadTypes, int Tag);

public sealed record EnumDefinitionSymbol(
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<EnumVariantDefinition> Variants)
{
    public int MaxPayloadArity => Variants.Count == 0 ? 0 : Variants.Max(v => v.PayloadTypes.Count);
}

public sealed record ClrNominalTypeSymbol(string ClrTypeFullName) : TypeSymbol
{
    public override string Name => ClrTypeFullName;
}

public static class TypeSymbols
{
    public static IntTypeSymbol Int { get; } = IntTypeSymbol.Instance;
    public static LongTypeSymbol Long { get; } = LongTypeSymbol.Instance;
    public static DoubleTypeSymbol Double { get; } = DoubleTypeSymbol.Instance;
    public static CharTypeSymbol Char { get; } = CharTypeSymbol.Instance;
    public static ByteTypeSymbol Byte { get; } = ByteTypeSymbol.Instance;
    public static SByteTypeSymbol SByte { get; } = SByteTypeSymbol.Instance;
    public static ShortTypeSymbol Short { get; } = ShortTypeSymbol.Instance;
    public static UShortTypeSymbol UShort { get; } = UShortTypeSymbol.Instance;
    public static UIntTypeSymbol UInt { get; } = UIntTypeSymbol.Instance;
    public static ULongTypeSymbol ULong { get; } = ULongTypeSymbol.Instance;
    public static NIntTypeSymbol NInt { get; } = NIntTypeSymbol.Instance;
    public static NUIntTypeSymbol NUInt { get; } = NUIntTypeSymbol.Instance;
    public static FloatTypeSymbol Float { get; } = FloatTypeSymbol.Instance;
    public static DecimalTypeSymbol Decimal { get; } = DecimalTypeSymbol.Instance;
    public static StringTypeSymbol String { get; } = StringTypeSymbol.Instance;
    public static BoolTypeSymbol Bool { get; } = BoolTypeSymbol.Instance;
    public static VoidTypeSymbol Void { get; } = VoidTypeSymbol.Instance;
    public static NullTypeSymbol Null { get; } = NullTypeSymbol.Instance;
    public static ErrorTypeSymbol Error { get; } = ErrorTypeSymbol.Instance;

    public static TypeSymbol? TryGetPrimitive(string name)
    {
        return name switch
        {
            "int" => Int,
            "long" => Long,
            "double" => Double,
            "char" => Char,
            "byte" => Byte,
            "sbyte" => SByte,
            "short" => Short,
            "ushort" => UShort,
            "uint" => UInt,
            "ulong" => ULong,
            "nint" => NInt,
            "nuint" => NUInt,
            "float" => Float,
            "decimal" => Decimal,
            "string" => String,
            "bool" => Bool,
            "void" => Void,
            "null" => Null,
            _ => null,
        };
    }
}
