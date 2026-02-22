using Mono.Cecil;

namespace Kong.Semantic;

public sealed record ConstructorClrBinding(
    string TypePath,
    TypeSymbol ConstructedType,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    MethodDefinition Constructor);

public static class ConstructorClrResolver
{
    private static readonly Lazy<IReadOnlyList<AssemblyDefinition>> Assemblies = new(LoadTrustedAssemblies);

    public static bool IsKnownTypePath(string typePath)
    {
        return TryFindType(typePath, out _);
    }

    public static bool TryResolveTypeDefinition(string typePath, out TypeDefinition typeDefinition)
    {
        return TryFindType(typePath, out typeDefinition);
    }

    public static bool TryResolve(
        string typePath,
        IReadOnlyList<TypeSymbol> argumentTypes,
        out ConstructorClrBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (!TryFindType(typePath, out var typeDefinition))
        {
            error = StaticClrMethodResolutionError.TypeNotFound;
            errorMessage = $"unknown CLR type '{typePath}'";
            return false;
        }

        var constructors = typeDefinition.Methods
            .Where(m => m.IsConstructor && m.IsPublic && !m.IsStatic)
            .ToList();

        if (constructors.Count == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            errorMessage = $"type '{typePath}' does not expose a supported public constructor";
            return false;
        }

        var scoredMatches = new List<(MethodDefinition Ctor, int Score)>();
        foreach (var ctor in constructors)
        {
            if (TryGetParameterMatchScore(ctor, argumentTypes, out var score))
            {
                scoredMatches.Add((ctor, score));
            }
        }

        if (scoredMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.NoMatchingOverload;
            errorMessage = $"no matching constructor overload for '{typePath}' with argument types ({string.Join(", ", argumentTypes)})";
            return false;
        }

        var bestScore = scoredMatches.Min(s => s.Score);
        var matches = scoredMatches.Where(s => s.Score == bestScore).Select(s => s.Ctor).ToList();
        if (matches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous constructor overload for '{typePath}' with argument types ({string.Join(", ", argumentTypes)})";
            return false;
        }

        if (!TryMapClrTypeReferenceToKongType(typeDefinition, out var constructedType))
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"constructor type '{typePath}' is not supported";
            return false;
        }

        binding = new ConstructorClrBinding(typePath, constructedType, argumentTypes.ToArray(), matches[0]);
        return true;
    }

    private static bool TryGetParameterMatchScore(MethodDefinition method, IReadOnlyList<TypeSymbol> argumentTypes, out int score)
    {
        score = 0;
        var parameters = method.Parameters;
        var hasParamsArray = HasParamsArray(method, out var paramsElementType);
        var fixedParameterCount = hasParamsArray ? parameters.Count - 1 : parameters.Count;

        var requiredFixedCount = 0;
        for (var i = 0; i < fixedParameterCount; i++)
        {
            if (!IsOptionalParameter(parameters[i]))
            {
                requiredFixedCount++;
            }
        }

        if (argumentTypes.Count < requiredFixedCount)
        {
            return false;
        }

        if (!hasParamsArray && argumentTypes.Count > parameters.Count)
        {
            return false;
        }

        var bestScore = int.MaxValue;
        if (argumentTypes.Count <= parameters.Count && TryScoreDirectOrOptionalCall(parameters, argumentTypes, out var directScore))
        {
            bestScore = directScore;
        }

        if (hasParamsArray && paramsElementType != TypeSymbols.Error &&
            argumentTypes.Count >= fixedParameterCount &&
            TryScoreExpandedParamsCall(parameters, fixedParameterCount, paramsElementType, argumentTypes, out var expandedScore) &&
            expandedScore < bestScore)
        {
            bestScore = expandedScore;
        }

        if (bestScore == int.MaxValue)
        {
            return false;
        }

        score = bestScore;
        return true;
    }

    private static bool TryScoreDirectOrOptionalCall(IList<ParameterDefinition> parameters, IReadOnlyList<TypeSymbol> argumentTypes, out int score)
    {
        score = 0;
        if (argumentTypes.Count > parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < argumentTypes.Count; i++)
        {
            if (!TryMapClrTypeReferenceToKongType(parameters[i].ParameterType, out var parameterType) || parameterType == TypeSymbols.Void)
            {
                return false;
            }

            if (!TryGetConversionScore(argumentTypes[i], parameterType, out var conversionScore))
            {
                return false;
            }

            score += conversionScore;
        }

        for (var i = argumentTypes.Count; i < parameters.Count; i++)
        {
            if (!IsOptionalParameter(parameters[i]))
            {
                return false;
            }

            score += 100;
        }

        return true;
    }

    private static bool TryScoreExpandedParamsCall(
        IList<ParameterDefinition> parameters,
        int fixedParameterCount,
        TypeSymbol paramsElementType,
        IReadOnlyList<TypeSymbol> argumentTypes,
        out int score)
    {
        score = 0;
        for (var i = 0; i < fixedParameterCount; i++)
        {
            if (i >= argumentTypes.Count)
            {
                if (!IsOptionalParameter(parameters[i]))
                {
                    return false;
                }

                score += 100;
                continue;
            }

            if (!TryMapClrTypeReferenceToKongType(parameters[i].ParameterType, out var parameterType) || parameterType == TypeSymbols.Void)
            {
                return false;
            }

            if (!TryGetConversionScore(argumentTypes[i], parameterType, out var conversionScore))
            {
                return false;
            }

            score += conversionScore;
        }

        for (var i = fixedParameterCount; i < argumentTypes.Count; i++)
        {
            if (!TryGetConversionScore(argumentTypes[i], paramsElementType, out var conversionScore))
            {
                return false;
            }

            score += conversionScore;
        }

        score += 25;
        return true;
    }

    private static bool TryGetConversionScore(TypeSymbol source, TypeSymbol target, out int score)
    {
        score = int.MaxValue;
        if (source == target)
        {
            score = 0;
            return true;
        }

        if (source is ClrNominalTypeSymbol sourceNominal && target is ClrNominalTypeSymbol targetNominal)
        {
            score = sourceNominal.ClrTypeFullName == targetNominal.ClrTypeFullName ? 0 : int.MaxValue;
            return score == 0;
        }

        if (source == TypeSymbols.Char)
        {
            score = target switch
            {
                _ when target == TypeSymbols.Int => 1,
                _ when target == TypeSymbols.Long => 2,
                _ when target == TypeSymbols.Double => 3,
                _ => int.MaxValue,
            };
            return score != int.MaxValue;
        }

        if (source == TypeSymbols.Byte)
        {
            score = target switch
            {
                _ when target == TypeSymbols.Int => 1,
                _ when target == TypeSymbols.Long => 2,
                _ when target == TypeSymbols.Double => 3,
                _ => int.MaxValue,
            };
            return score != int.MaxValue;
        }

        if (source == TypeSymbols.Int)
        {
            score = target switch
            {
                _ when target == TypeSymbols.Long => 1,
                _ when target == TypeSymbols.Double => 2,
                _ => int.MaxValue,
            };
            return score != int.MaxValue;
        }

        if (source == TypeSymbols.Long)
        {
            score = target == TypeSymbols.Double ? 1 : int.MaxValue;
            return score != int.MaxValue;
        }

        return false;
    }

    private static bool TryMapClrTypeReferenceToKongType(TypeReference clrType, out TypeSymbol type)
    {
        if (clrType is TypeDefinition typeDefinition)
        {
            return TryMapClrTypeDefinitionToKongType(typeDefinition, out type);
        }

        if (clrType is ArrayType arrayType)
        {
            if (!TryMapClrTypeReferenceToKongType(arrayType.ElementType, out var elementType))
            {
                type = TypeSymbols.Error;
                return false;
            }

            type = new ArrayTypeSymbol(elementType);
            return true;
        }

        return TryMapClrFullNameToKongType(NormalizeTypeName(clrType.FullName), out type);
    }

    private static bool TryMapClrTypeDefinitionToKongType(TypeDefinition typeDefinition, out TypeSymbol type)
    {
        var fullName = NormalizeTypeName(typeDefinition.FullName);
        if (TryMapClrFullNameToKongType(fullName, out type))
        {
            return true;
        }

        type = new ClrNominalTypeSymbol(fullName);
        return true;
    }

    private static bool HasParamsArray(MethodDefinition method, out TypeSymbol elementType)
    {
        elementType = TypeSymbols.Error;
        if (method.Parameters.Count == 0)
        {
            return false;
        }

        var lastParameter = method.Parameters[^1];
        if (!lastParameter.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute"))
        {
            return false;
        }

        if (lastParameter.ParameterType is not ArrayType paramsArrayType)
        {
            return false;
        }

        return TryMapClrTypeReferenceToKongType(paramsArrayType.ElementType, out elementType);
    }

    private static bool IsOptionalParameter(ParameterDefinition parameter)
    {
        return parameter.IsOptional || parameter.HasDefault;
    }

    private static bool TryMapClrFullNameToKongType(string clrTypeName, out TypeSymbol type)
    {
        type = TypeSymbols.Error;
        switch (clrTypeName)
        {
            case "System.Int64":
                type = TypeSymbols.Long;
                return true;
            case "System.Int32":
                type = TypeSymbols.Int;
                return true;
            case "System.Double":
                type = TypeSymbols.Double;
                return true;
            case "System.Char":
                type = TypeSymbols.Char;
                return true;
            case "System.Byte":
                type = TypeSymbols.Byte;
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
