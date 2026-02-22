using Mono.Cecil;
using Kong.Parsing;

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

public readonly record struct ClrCallArgument(TypeSymbol Type, CallArgumentModifier Modifier);

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
        if (!TryGetMethodCandidates(methodPath, out var _, out var methods, out _))
        {
            return false;
        }

        return methods.Any(IsSupportedMethodSignature);
    }

    public static bool IsKnownValuePath(string memberPath)
    {
        return TryResolveValue(memberPath, out _, out _, out _);
    }

    public static StaticClrMethodBinding? Resolve(string methodPath, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        var arguments = parameterTypes.Select(t => new ClrCallArgument(t, CallArgumentModifier.None)).ToArray();
        return TryResolve(methodPath, arguments, out var binding, out _, out _) ? binding : null;
    }

    public static bool TryResolve(
        string methodPath,
        IReadOnlyList<TypeSymbol> parameterTypes,
        out StaticClrMethodBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        var arguments = parameterTypes.Select(t => new ClrCallArgument(t, CallArgumentModifier.None)).ToArray();
        return TryResolve(methodPath, arguments, out binding, out error, out errorMessage);
    }

    public static bool TryResolve(
        string methodPath,
        IReadOnlyList<ClrCallArgument> arguments,
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

        foreach (var argument in arguments)
        {
            if (!TryMapKongTypeToClrFullName(argument.Type, out _))
            {
                error = StaticClrMethodResolutionError.UnsupportedParameterType;
                errorMessage = $"static calls do not support Kong type '{argument.Type}' in parameter list";
                return false;
            }
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
            errorMessage = $"no matching overload for static method '{methodPath}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var bestScore = scoredMatches.Min(m => m.Score);
        var matches = scoredMatches
            .Where(m => m.Score == bestScore)
            .Select(m => m.Method)
            .ToList();

        var compatibleMatches = matches
            .Where(m => TryMapClrFullNameToKongType(NormalizeTypeName(m.ReturnType.FullName), out _))
            .ToList();

        if (compatibleMatches.Count == 0)
        {
            var unsupportedReturn = matches[0].ReturnType.FullName;
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"static method '{methodPath}' returns unsupported CLR type '{unsupportedReturn}'";
            return false;
        }

        if (compatibleMatches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous static method call '{methodPath}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var selected = compatibleMatches[0];
        _ = TryMapClrFullNameToKongType(NormalizeTypeName(selected.ReturnType.FullName), out var returnType);

        binding = new StaticClrMethodBinding(methodPath, arguments.Select(a => a.Type).ToArray(), returnType, selected);
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

    private static bool TryScoreDirectOrOptionalCall(
        IList<ParameterDefinition> parameters,
        IReadOnlyList<ClrCallArgument> arguments,
        out int score)
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
            if (arguments[i].Modifier != CallArgumentModifier.None)
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

    private static bool IsSupportedMethodSignature(MethodDefinition method)
    {
        if (!TryMapClrTypeReferenceToKongType(method.ReturnType, out _))
        {
            return false;
        }

        foreach (var parameter in method.Parameters)
        {
            if (!TryMapClrTypeReferenceToKongType(parameter.ParameterType, out var parameterType))
            {
                return false;
            }

            if (parameterType == TypeSymbols.Void)
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

        if (type == TypeSymbols.SByte)
        {
            clrTypeName = "System.SByte";
            return true;
        }

        if (type == TypeSymbols.Short)
        {
            clrTypeName = "System.Int16";
            return true;
        }

        if (type == TypeSymbols.UShort)
        {
            clrTypeName = "System.UInt16";
            return true;
        }

        if (type == TypeSymbols.UInt)
        {
            clrTypeName = "System.UInt32";
            return true;
        }

        if (type == TypeSymbols.ULong)
        {
            clrTypeName = "System.UInt64";
            return true;
        }

        if (type == TypeSymbols.NInt)
        {
            clrTypeName = "System.IntPtr";
            return true;
        }

        if (type == TypeSymbols.NUInt)
        {
            clrTypeName = "System.UIntPtr";
            return true;
        }

        if (type == TypeSymbols.Float)
        {
            clrTypeName = "System.Single";
            return true;
        }

        if (type == TypeSymbols.Decimal)
        {
            clrTypeName = "System.Decimal";
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
        if (clrType is ByReferenceType byReferenceType)
        {
            return TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, out type);
        }

        if (clrType is Mono.Cecil.ArrayType arrayType)
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

        if (lastParameter.ParameterType is not Mono.Cecil.ArrayType paramsArrayType)
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
        out CallArgumentModifier modifier)
    {
        modifier = CallArgumentModifier.None;
        if (parameter.ParameterType is ByReferenceType byReferenceType)
        {
            if (!TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, out parameterType))
            {
                return false;
            }

            modifier = parameter.IsOut ? CallArgumentModifier.Out : CallArgumentModifier.Ref;
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
            case "System.SByte":
                type = TypeSymbols.SByte;
                return true;
            case "System.Int16":
                type = TypeSymbols.Short;
                return true;
            case "System.UInt16":
                type = TypeSymbols.UShort;
                return true;
            case "System.UInt32":
                type = TypeSymbols.UInt;
                return true;
            case "System.UInt64":
                type = TypeSymbols.ULong;
                return true;
            case "System.IntPtr":
                type = TypeSymbols.NInt;
                return true;
            case "System.UIntPtr":
                type = TypeSymbols.NUInt;
                return true;
            case "System.Single":
                type = TypeSymbols.Float;
                return true;
            case "System.Decimal":
                type = TypeSymbols.Decimal;
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
