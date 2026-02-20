namespace Kong;

using System.Diagnostics.CodeAnalysis;

public record class BuiltinSignature(
    string PublicName,
    string IrFunctionName,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType)
{
    public override string ToString()
    {
        return $"{PublicName}({string.Join(", ", ParameterTypes)}) -> {ReturnType}";
    }
}

public record class BuiltinBinding(
    BuiltinSignature Signature,
    string RuntimeMethodName,
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type RuntimeType);

public class BuiltinRegistry
{
    public static BuiltinRegistry Default { get; } = CreateDefault();

    private readonly Dictionary<string, List<BuiltinBinding>> _bindings = [];

    public void Register(BuiltinBinding binding)
    {
        var key = binding.Signature.PublicName;
        if (!_bindings.ContainsKey(key))
        {
            _bindings[key] = [];
        }

        _bindings[key].Add(binding);
    }

    public BuiltinBinding? ResolveByTypeSignature(
        string publicName,
        IReadOnlyList<TypeSymbol> parameterTypes)
    {
        if (!_bindings.TryGetValue(publicName, out var bindings))
        {
            return null;
        }

        foreach (var binding in bindings)
        {
            if (TypeSignatureMatches(binding.Signature.ParameterTypes, parameterTypes))
            {
                return binding;
            }
        }

        return null;
    }

    public IEnumerable<string> GetAllPublicNames()
    {
        return _bindings.Keys;
    }

    public IEnumerable<BuiltinBinding> GetBindingsForName(string publicName)
    {
        if (_bindings.TryGetValue(publicName, out var bindings))
        {
            return bindings;
        }
        return [];
    }

    public FunctionTypeSymbol? GetFunctionTypeBySignature(
        string publicName,
        IReadOnlyList<TypeSymbol> parameterTypes)
    {
        var binding = ResolveByTypeSignature(publicName, parameterTypes);
        if (binding == null)
        {
            return null;
        }

        return new FunctionTypeSymbol(binding.Signature.ParameterTypes, binding.Signature.ReturnType);
    }

    public bool IsDefined(string publicName)
    {
        return _bindings.ContainsKey(publicName);
    }

    private static bool TypeSignatureMatches(
        IReadOnlyList<TypeSymbol> expected,
        IReadOnlyList<TypeSymbol> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i] != actual[i])
            {
                return false;
            }
        }

        return true;
    }

    public static BuiltinRegistry CreateDefault()
    {
        var registry = new BuiltinRegistry();

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "puts",
                "__builtin_puts_int",
                [TypeSymbols.Int],
                TypeSymbols.Void),
            nameof(ClrRuntimeBuiltins.PutsInt),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "puts",
                "__builtin_puts_string",
                [TypeSymbols.String],
                TypeSymbols.Void),
            nameof(ClrRuntimeBuiltins.PutsString),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "puts",
                "__builtin_puts_bool",
                [TypeSymbols.Bool],
                TypeSymbols.Void),
            nameof(ClrRuntimeBuiltins.PutsBool),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "len",
                "__builtin_len_string",
                [TypeSymbols.String],
                TypeSymbols.Int),
            nameof(ClrRuntimeBuiltins.LenString),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "first",
                "__builtin_first_int_array",
                [new ArrayTypeSymbol(TypeSymbols.Int)],
                TypeSymbols.Int),
            nameof(ClrRuntimeBuiltins.FirstIntArray),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "last",
                "__builtin_last_int_array",
                [new ArrayTypeSymbol(TypeSymbols.Int)],
                TypeSymbols.Int),
            nameof(ClrRuntimeBuiltins.LastIntArray),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "rest",
                "__builtin_rest_int_array",
                [new ArrayTypeSymbol(TypeSymbols.Int)],
                new ArrayTypeSymbol(TypeSymbols.Int)),
            nameof(ClrRuntimeBuiltins.RestIntArray),
            typeof(ClrRuntimeBuiltins)));

        registry.Register(new BuiltinBinding(
            new BuiltinSignature(
                "push",
                "__builtin_push_int_array",
                [new ArrayTypeSymbol(TypeSymbols.Int), TypeSymbols.Int],
                new ArrayTypeSymbol(TypeSymbols.Int)),
            nameof(ClrRuntimeBuiltins.PushIntArray),
            typeof(ClrRuntimeBuiltins)));

        return registry;
    }
}
