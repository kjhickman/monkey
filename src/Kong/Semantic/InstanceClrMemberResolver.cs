using Mono.Cecil;

namespace Kong.Semantic;

public sealed record InstanceClrMethodBinding(
    TypeSymbol ReceiverType,
    string MemberName,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType,
    MethodDefinition MethodDefinition);

public sealed record InstanceClrValueBinding(
    TypeSymbol ReceiverType,
    string MemberName,
    TypeSymbol Type,
    MethodDefinition? PropertyGetter,
    FieldDefinition? Field);

public static class InstanceClrMemberResolver
{
    private static readonly Lazy<IReadOnlyList<AssemblyDefinition>> Assemblies = new(LoadTrustedAssemblies);

    public static bool TryResolveMethod(
        TypeSymbol receiverType,
        string memberName,
        IReadOnlyList<TypeSymbol> parameterTypes,
        out InstanceClrMethodBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        var arguments = parameterTypes.Select(t => new ClrCallArgument(t, Kong.Parsing.CallArgumentModifier.None)).ToArray();
        return TryResolveMethod(receiverType, memberName, arguments, out binding, out error, out errorMessage);
    }

    public static bool TryResolveMethod(
        TypeSymbol receiverType,
        string memberName,
        IReadOnlyList<ClrCallArgument> arguments,
        out InstanceClrMethodBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(memberName))
        {
            error = StaticClrMethodResolutionError.InvalidMethodPath;
            errorMessage = "invalid instance method name";
            return false;
        }

        if (!TryMapReceiverTypeToClrFullName(receiverType, out var receiverClrTypeName))
        {
            error = StaticClrMethodResolutionError.UnsupportedParameterType;
            errorMessage = $"instance calls do not support Kong receiver type '{receiverType}'";
            return false;
        }

        if (!TryFindType(receiverClrTypeName, out var typeDefinition))
        {
            error = StaticClrMethodResolutionError.TypeNotFound;
            errorMessage = $"unknown CLR type '{receiverClrTypeName}'";
            return false;
        }

        foreach (var argument in arguments)
        {
            if (!TryMapKongTypeToClrFullName(argument.Type, out _))
            {
                error = StaticClrMethodResolutionError.UnsupportedParameterType;
                errorMessage = $"instance calls do not support Kong type '{argument.Type}' in parameter list";
                return false;
            }
        }

        var candidates = typeDefinition.Methods
            .Where(m => m.IsPublic && !m.IsStatic && m.Name == memberName && !m.HasGenericParameters)
            .ToList();

        if (candidates.Count == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            errorMessage = $"unknown instance method '{memberName}' on '{receiverType}'";
            return false;
        }

        var scoredMatches = new List<(MethodDefinition Method, int Score)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (TryGetParameterMatchScore(candidate, arguments, out var score))
            {
                scoredMatches.Add((candidate, score));
            }
        }

        if (scoredMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.NoMatchingOverload;
            errorMessage = $"no matching overload for instance method '{memberName}' on '{receiverType}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var bestScore = scoredMatches.Min(m => m.Score);
        var matches = scoredMatches
            .Where(m => m.Score == bestScore)
            .Select(m => m.Method)
            .ToList();

        var compatibleMatches = matches
            .Where(m => TryMapClrTypeReferenceToKongType(m.ReturnType, out _))
            .ToList();

        if (compatibleMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"instance method '{memberName}' on '{receiverType}' returns unsupported CLR type '{matches[0].ReturnType.FullName}'";
            return false;
        }

        if (compatibleMatches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous instance method call '{memberName}' on '{receiverType}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var selected = compatibleMatches[0];
        _ = TryMapClrTypeReferenceToKongType(selected.ReturnType, out var returnType);

        binding = new InstanceClrMethodBinding(receiverType, memberName, arguments.Select(a => a.Type).ToArray(), returnType, selected);
        return true;
    }

    public static bool TryResolveValue(
        TypeSymbol receiverType,
        string memberName,
        out InstanceClrValueBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(memberName))
        {
            error = StaticClrMethodResolutionError.InvalidMethodPath;
            errorMessage = "invalid instance member name";
            return false;
        }

        if (!TryMapReceiverTypeToClrFullName(receiverType, out var receiverClrTypeName))
        {
            error = StaticClrMethodResolutionError.UnsupportedParameterType;
            errorMessage = $"instance member access does not support Kong receiver type '{receiverType}'";
            return false;
        }

        if (!TryFindType(receiverClrTypeName, out var typeDefinition))
        {
            error = StaticClrMethodResolutionError.TypeNotFound;
            errorMessage = $"unknown CLR type '{receiverClrTypeName}'";
            return false;
        }

        var properties = typeDefinition.Properties
            .Where(p => p.Name == memberName && p.GetMethod is { IsPublic: true, IsStatic: false } getter && getter.Parameters.Count == 0)
            .ToList();

        var fields = typeDefinition.Fields
            .Where(f => f.Name == memberName && f.IsPublic && !f.IsStatic)
            .ToList();

        var totalMatches = properties.Count + fields.Count;
        if (totalMatches == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            errorMessage = $"unknown instance member '{memberName}' on '{receiverType}'";
            return false;
        }

        if (totalMatches > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous instance member access '{memberName}' on '{receiverType}'";
            return false;
        }

        if (properties.Count == 1)
        {
            var property = properties[0];
            if (!TryMapClrTypeReferenceToKongType(property.PropertyType, out var propertyType))
            {
                error = StaticClrMethodResolutionError.UnsupportedReturnType;
                errorMessage = $"instance member '{memberName}' on '{receiverType}' has unsupported CLR type '{property.PropertyType.FullName}'";
                return false;
            }

            binding = new InstanceClrValueBinding(receiverType, memberName, propertyType, property.GetMethod!, null);
            return true;
        }

        var field = fields[0];
        if (!TryMapClrTypeReferenceToKongType(field.FieldType, out var fieldType))
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"instance member '{memberName}' on '{receiverType}' has unsupported CLR type '{field.FieldType.FullName}'";
            return false;
        }

        binding = new InstanceClrValueBinding(receiverType, memberName, fieldType, null, field);
        return true;
    }

    private static bool TryGetParameterMatchScore(MethodDefinition method, IReadOnlyList<ClrCallArgument> arguments, out int score)
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

        if (arguments.Count < requiredFixedCount)
        {
            return false;
        }

        if (!hasParamsArray && arguments.Count > parameters.Count)
        {
            return false;
        }

        var bestScore = int.MaxValue;

        if (arguments.Count <= parameters.Count &&
            TryScoreDirectOrOptionalCall(parameters, arguments, out var directScore))
        {
            bestScore = directScore;
        }

        if (hasParamsArray && paramsElementType != TypeSymbols.Error &&
            arguments.Count >= fixedParameterCount &&
            TryScoreExpandedParamsCall(parameters, fixedParameterCount, paramsElementType, arguments, out var expandedScore))
        {
            if (expandedScore < bestScore)
            {
                bestScore = expandedScore;
            }
        }

        if (bestScore == int.MaxValue)
        {
            return false;
        }

        score = bestScore;
        return true;
    }

    private static bool TryScoreDirectOrOptionalCall(IList<ParameterDefinition> parameters, IReadOnlyList<ClrCallArgument> arguments, out int score)
    {
        score = 0;

        if (arguments.Count > parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            if (!TryGetParameterTypeAndModifier(parameters[i], out var parameterType, out var expectedModifier) ||
                parameterType == TypeSymbols.Void)
            {
                return false;
            }

            if (expectedModifier != arguments[i].Modifier)
            {
                return false;
            }

            if (!TryGetConversionScore(arguments[i].Type, parameterType, out var conversionScore))
            {
                return false;
            }

            score += conversionScore;
        }

        for (var i = arguments.Count; i < parameters.Count; i++)
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
        IReadOnlyList<ClrCallArgument> arguments,
        out int score)
    {
        score = 0;

        for (var i = 0; i < fixedParameterCount; i++)
        {
            if (i >= arguments.Count)
            {
                if (!IsOptionalParameter(parameters[i]))
                {
                    return false;
                }

                score += 100;
                continue;
            }

            if (!TryGetParameterTypeAndModifier(parameters[i], out var parameterType, out var expectedModifier) ||
                parameterType == TypeSymbols.Void)
            {
                return false;
            }

            if (expectedModifier != arguments[i].Modifier)
            {
                return false;
            }

            if (!TryGetConversionScore(arguments[i].Type, parameterType, out var conversionScore))
            {
                return false;
            }

            score += conversionScore;
        }

        for (var i = fixedParameterCount; i < arguments.Count; i++)
        {
            if (arguments[i].Modifier != Kong.Parsing.CallArgumentModifier.None)
            {
                return false;
            }

            if (!TryGetConversionScore(arguments[i].Type, paramsElementType, out var conversionScore))
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

    private static bool TryMapReceiverTypeToClrFullName(TypeSymbol type, out string clrTypeName)
    {
        if (type is ClrNominalTypeSymbol nominalType)
        {
            clrTypeName = nominalType.ClrTypeFullName;
            return true;
        }

        if (type == TypeSymbols.String)
        {
            clrTypeName = "System.String";
            return true;
        }

        if (type is ArrayTypeSymbol)
        {
            clrTypeName = "System.Array";
            return true;
        }

        clrTypeName = string.Empty;
        return false;
    }

    private static bool TryMapKongTypeToClrFullName(TypeSymbol type, out string clrTypeName)
    {
        clrTypeName = string.Empty;

        if (type is ClrNominalTypeSymbol nominalType)
        {
            clrTypeName = nominalType.ClrTypeFullName;
            return true;
        }

        if (type == TypeSymbols.Int)
        {
            clrTypeName = "System.Int32";
            return true;
        }

        if (type == TypeSymbols.Long)
        {
            clrTypeName = "System.Int64";
            return true;
        }

        if (type == TypeSymbols.Double)
        {
            clrTypeName = "System.Double";
            return true;
        }

        if (type == TypeSymbols.Char)
        {
            clrTypeName = "System.Char";
            return true;
        }

        if (type == TypeSymbols.Byte)
        {
            clrTypeName = "System.Byte";
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

        if (type is ArrayTypeSymbol array)
        {
            if (!TryMapKongTypeToClrFullName(array.ElementType, out var elementClrName))
            {
                return false;
            }

            clrTypeName = elementClrName + "[]";
            return true;
        }

        return false;
    }

    private static bool TryMapClrTypeReferenceToKongType(TypeReference clrType, out TypeSymbol type)
    {
        if (clrType is TypeDefinition typeDefinition)
        {
            return TryMapClrTypeDefinitionToKongType(typeDefinition, out type);
        }

        if (clrType is ByReferenceType byReferenceType)
        {
            return TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, out type);
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

        if (TryMapClrFullNameToKongType(NormalizeTypeName(clrType.FullName), out type))
        {
            return true;
        }

        type = new ClrNominalTypeSymbol(NormalizeTypeName(clrType.FullName));
        return true;
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

    private static bool TryGetParameterTypeAndModifier(
        ParameterDefinition parameter,
        out TypeSymbol parameterType,
        out Kong.Parsing.CallArgumentModifier modifier)
    {
        modifier = Kong.Parsing.CallArgumentModifier.None;
        if (parameter.ParameterType is ByReferenceType byReferenceType)
        {
            if (!TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, out parameterType))
            {
                return false;
            }

            modifier = parameter.IsOut ? Kong.Parsing.CallArgumentModifier.Out : Kong.Parsing.CallArgumentModifier.Ref;
            return true;
        }

        return TryMapClrTypeReferenceToKongType(parameter.ParameterType, out parameterType);
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
