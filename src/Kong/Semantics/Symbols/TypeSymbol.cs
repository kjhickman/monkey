namespace Kong.Semantics.Symbols;

public abstract record TypeSymbol
{
    public static TypeSymbol Unknown { get; } = new PrimitiveTypeSymbol("unknown", KongType.Unknown);
    public static TypeSymbol Void { get; } = new PrimitiveTypeSymbol("void", KongType.Void);
    public static TypeSymbol Int { get; } = new PrimitiveTypeSymbol("int", KongType.Int64);
    public static TypeSymbol Bool { get; } = new PrimitiveTypeSymbol("bool", KongType.Boolean);
    public static TypeSymbol String { get; } = new PrimitiveTypeSymbol("string", KongType.String);
    public static TypeSymbol Char { get; } = new PrimitiveTypeSymbol("char", KongType.Char);
    public static TypeSymbol Double { get; } = new PrimitiveTypeSymbol("double", KongType.Double);
    public static TypeSymbol Array { get; } = new ArrayTypeSymbol(Unknown);
    public static TypeSymbol HashMap { get; } = new MapTypeSymbol(Unknown, Unknown);

    public static TypeSymbol ArrayOf(TypeSymbol elementType)
    {
        return new ArrayTypeSymbol(elementType);
    }

    public static TypeSymbol MapOf(TypeSymbol keyType, TypeSymbol valueType)
    {
        return new MapTypeSymbol(keyType, valueType);
    }

    public static TypeSymbol FunctionOf(IReadOnlyList<TypeSymbol> parameterTypes, TypeSymbol returnType)
    {
        return new FunctionTypeSymbol(parameterTypes, returnType);
    }

    public static TypeSymbol FromKongType(KongType type)
    {
        return type switch
        {
            KongType.Int64 => Int,
            KongType.Boolean => Bool,
            KongType.String => String,
            KongType.Char => Char,
            KongType.Double => Double,
            KongType.Array => Array,
            KongType.HashMap => HashMap,
            KongType.Void => Void,
            _ => Unknown,
        };
    }

    public virtual bool IsCompatibleWith(TypeSymbol actual)
    {
        return this == Unknown || actual == Unknown || Equals(actual);
    }

    public virtual KongType ToKongType()
    {
        return KongType.Unknown;
    }
}

public sealed record PrimitiveTypeSymbol(string Name, KongType RuntimeType) : TypeSymbol
{
    public override KongType ToKongType()
    {
        return RuntimeType;
    }

    public override string ToString()
    {
        return Name;
    }
}

public sealed record ArrayTypeSymbol(TypeSymbol ElementType) : TypeSymbol
{
    public override bool IsCompatibleWith(TypeSymbol actual)
    {
        if (actual == Unknown)
        {
            return true;
        }

        if (actual is not ArrayTypeSymbol arrayType)
        {
            return false;
        }

        return ElementType.IsCompatibleWith(arrayType.ElementType);
    }

    public override KongType ToKongType()
    {
        return KongType.Array;
    }

    public override string ToString()
    {
        return $"{ElementType}[]";
    }
}

public sealed record MapTypeSymbol(TypeSymbol KeyType, TypeSymbol ValueType) : TypeSymbol
{
    public override bool IsCompatibleWith(TypeSymbol actual)
    {
        if (actual == Unknown)
        {
            return true;
        }

        if (actual is not MapTypeSymbol mapType)
        {
            return false;
        }

        return KeyType.IsCompatibleWith(mapType.KeyType)
            && ValueType.IsCompatibleWith(mapType.ValueType);
    }

    public override KongType ToKongType()
    {
        return KongType.HashMap;
    }

    public override string ToString()
    {
        return $"map[{KeyType}]{ValueType}";
    }
}

public sealed record FunctionTypeSymbol(IReadOnlyList<TypeSymbol> ParameterTypes, TypeSymbol ReturnType) : TypeSymbol
{
    public override bool IsCompatibleWith(TypeSymbol actual)
    {
        if (actual == Unknown)
        {
            return true;
        }

        if (actual is not FunctionTypeSymbol functionType)
        {
            return false;
        }

        if (ParameterTypes.Count != functionType.ParameterTypes.Count)
        {
            return false;
        }

        for (var i = 0; i < ParameterTypes.Count; i++)
        {
            if (!ParameterTypes[i].IsCompatibleWith(functionType.ParameterTypes[i])
                || !functionType.ParameterTypes[i].IsCompatibleWith(ParameterTypes[i]))
            {
                return false;
            }
        }

        return ReturnType.IsCompatibleWith(functionType.ReturnType)
            && functionType.ReturnType.IsCompatibleWith(ReturnType);
    }

    public override string ToString()
    {
        return $"fn({string.Join(", ", ParameterTypes)}) -> {ReturnType}";
    }
}
