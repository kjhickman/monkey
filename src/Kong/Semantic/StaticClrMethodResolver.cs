using Mono.Cecil;

namespace Kong.Semantic;

public sealed record StaticClrMethodBinding(
    string MethodPath,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType,
    MethodDefinition MethodDefinition);

public sealed record StaticClrValueBinding(
    string MemberPath,
    TypeSymbol Type,
    MethodDefinition? PropertyGetter,
    FieldDefinition? Field);

public enum StaticClrMethodResolutionError
{
    None,
    InvalidMethodPath,
    UnsupportedParameterType,
    TypeNotFound,
    MethodNotFound,
    NoMatchingOverload,
    AmbiguousOverload,
    UnsupportedReturnType,
}

public static class StaticClrMethodResolver
{
    private static readonly Lazy<IReadOnlyList<AssemblyDefinition>> Assemblies = new(LoadTrustedAssemblies);

    public static bool IsKnownMethodPath(string methodPath)
    {
        return TryGetMethodCandidates(methodPath, out var _, out var methods, out _) && methods.Count > 0;
    }

    public static bool IsKnownValuePath(string memberPath)
    {
        return TryResolveValue(memberPath, out _, out _, out _);
    }

    public static StaticClrMethodBinding? Resolve(string methodPath, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        return TryResolve(methodPath, parameterTypes, out var binding, out _, out _) ? binding : null;
    }

    public static bool TryResolve(
        string methodPath,
        IReadOnlyList<TypeSymbol> parameterTypes,
        out StaticClrMethodBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (!TryGetMethodCandidates(methodPath, out var typeFullName, out var candidates, out error))
        {
            errorMessage = error switch
            {
                StaticClrMethodResolutionError.InvalidMethodPath => $"invalid static method path '{methodPath}'",
                StaticClrMethodResolutionError.TypeNotFound => $"unknown CLR type '{typeFullName}'",
                StaticClrMethodResolutionError.MethodNotFound => $"unknown static method '{methodPath}'",
                _ => $"failed to resolve static method '{methodPath}'",
            };
            return false;
        }

        var parameterClrTypes = new List<string>(parameterTypes.Count);
        foreach (var parameterType in parameterTypes)
        {
            if (!TryMapKongTypeToClrFullName(parameterType, out var clrTypeName))
            {
                error = StaticClrMethodResolutionError.UnsupportedParameterType;
                errorMessage = $"static calls do not support Kong type '{parameterType}' in parameter list";
                return false;
            }

            parameterClrTypes.Add(clrTypeName);
        }

        var matches = candidates
            .Where(method => ParametersMatch(method.Parameters, parameterClrTypes))
            .ToList();

        if (matches.Count == 0)
        {
            error = StaticClrMethodResolutionError.NoMatchingOverload;
            errorMessage = $"no matching overload for static method '{methodPath}' with argument types ({string.Join(", ", parameterTypes)})";
            return false;
        }

        if (matches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous static method call '{methodPath}' with argument types ({string.Join(", ", parameterTypes)})";
            return false;
        }

        var selected = matches[0];
        if (!TryMapClrFullNameToKongType(NormalizeTypeName(selected.ReturnType.FullName), out var returnType))
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"static method '{methodPath}' returns unsupported CLR type '{selected.ReturnType.FullName}'";
            return false;
        }

        binding = new StaticClrMethodBinding(methodPath, parameterTypes.ToArray(), returnType, selected);
        return true;
    }

    public static bool TryResolveValue(
        string memberPath,
        out StaticClrValueBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (!TryGetTypeAndMember(memberPath, out var typeFullName, out var memberName, out error))
        {
            errorMessage = $"invalid static member path '{memberPath}'";
            return false;
        }

        if (!TryFindType(typeFullName, out var typeDefinition))
        {
            error = StaticClrMethodResolutionError.TypeNotFound;
            errorMessage = $"unknown CLR type '{typeFullName}'";
            return false;
        }

        var properties = typeDefinition.Properties
            .Where(p => p.Name == memberName && p.GetMethod is { IsStatic: true, IsPublic: true } getter && getter.Parameters.Count == 0)
            .ToList();

        var fields = typeDefinition.Fields
            .Where(f => f.Name == memberName && f.IsStatic && f.IsPublic)
            .ToList();

        var totalMatches = properties.Count + fields.Count;
        if (totalMatches == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            errorMessage = $"unknown static member '{memberPath}'";
            return false;
        }

        if (totalMatches > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous static member access '{memberPath}'";
            return false;
        }

        if (properties.Count == 1)
        {
            var property = properties[0];
            if (!TryMapClrFullNameToKongType(NormalizeTypeName(property.PropertyType.FullName), out var propertyType))
            {
                error = StaticClrMethodResolutionError.UnsupportedReturnType;
                errorMessage = $"static member '{memberPath}' has unsupported CLR type '{property.PropertyType.FullName}'";
                return false;
            }

            binding = new StaticClrValueBinding(memberPath, propertyType, property.GetMethod!, null);
            return true;
        }

        var field = fields[0];
        if (!TryMapClrFullNameToKongType(NormalizeTypeName(field.FieldType.FullName), out var fieldType))
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"static member '{memberPath}' has unsupported CLR type '{field.FieldType.FullName}'";
            return false;
        }

        binding = new StaticClrValueBinding(memberPath, fieldType, null, field);
        return true;
    }

    private static bool TryGetMethodCandidates(
        string methodPath,
        out string typeFullName,
        out List<MethodDefinition> methods,
        out StaticClrMethodResolutionError error)
    {
        methods = [];
        typeFullName = string.Empty;
        error = StaticClrMethodResolutionError.None;

        var lastDot = methodPath.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == methodPath.Length - 1)
        {
            error = StaticClrMethodResolutionError.InvalidMethodPath;
            return false;
        }

        typeFullName = methodPath[..lastDot];
        var methodName = methodPath[(lastDot + 1)..];

        if (!TryFindType(typeFullName, out var typeDefinition))
        {
            error = StaticClrMethodResolutionError.TypeNotFound;
            return false;
        }

        methods = typeDefinition.Methods
            .Where(m => m.IsPublic && m.IsStatic && m.Name == methodName && !m.HasGenericParameters)
            .ToList();

        if (methods.Count == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            return false;
        }

        return true;
    }

    private static bool TryGetTypeAndMember(
        string memberPath,
        out string typeFullName,
        out string memberName,
        out StaticClrMethodResolutionError error)
    {
        typeFullName = string.Empty;
        memberName = string.Empty;
        error = StaticClrMethodResolutionError.None;

        var lastDot = memberPath.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == memberPath.Length - 1)
        {
            error = StaticClrMethodResolutionError.InvalidMethodPath;
            return false;
        }

        typeFullName = memberPath[..lastDot];
        memberName = memberPath[(lastDot + 1)..];
        return true;
    }

    private static bool ParametersMatch(IList<ParameterDefinition> parameters, IReadOnlyList<string> expectedClrTypeNames)
    {
        if (parameters.Count != expectedClrTypeNames.Count)
        {
            return false;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            if (NormalizeTypeName(parameters[i].ParameterType.FullName) != expectedClrTypeNames[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMapKongTypeToClrFullName(TypeSymbol type, out string clrTypeName)
    {
        clrTypeName = string.Empty;

        if (type == TypeSymbols.Int)
        {
            clrTypeName = "System.Int64";
            return true;
        }

        if (type == TypeSymbols.Bool)
        {
            clrTypeName = "System.Boolean";
            return true;
        }

        if (type == TypeSymbols.String)
        {
            clrTypeName = "System.String";
            return true;
        }

        if (type == TypeSymbols.Void)
        {
            clrTypeName = "System.Void";
            return true;
        }

        return false;
    }

    private static bool TryMapClrFullNameToKongType(string clrTypeName, out TypeSymbol type)
    {
        type = TypeSymbols.Error;

        switch (clrTypeName)
        {
            case "System.Int64":
                type = TypeSymbols.Int;
                return true;
            case "System.Boolean":
                type = TypeSymbols.Bool;
                return true;
            case "System.String":
                type = TypeSymbols.String;
                return true;
            case "System.Void":
                type = TypeSymbols.Void;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeTypeName(string clrTypeName)
    {
        return clrTypeName.Replace("/", ".");
    }

    private static bool TryFindType(string fullName, out TypeDefinition typeDefinition)
    {
        foreach (var assembly in Assemblies.Value)
        {
            foreach (var module in assembly.Modules)
            {
                var type = module.GetType(fullName) ?? module.Types.FirstOrDefault(t => t.FullName == fullName);
                if (type != null)
                {
                    typeDefinition = type;
                    return true;
                }
            }
        }

        typeDefinition = null!;
        return false;
    }

    private static IReadOnlyList<AssemblyDefinition> LoadTrustedAssemblies()
    {
        var list = new List<AssemblyDefinition>();
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return list;
        }

        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                list.Add(AssemblyDefinition.ReadAssembly(path));
            }
            catch
            {
            }
        }

        return list;
    }
}
