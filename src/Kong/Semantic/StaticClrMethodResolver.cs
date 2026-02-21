using Mono.Cecil;

namespace Kong.Semantic;

public sealed record StaticClrMethodBinding(
    string MethodPath,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType,
    MethodDefinition MethodDefinition);

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
