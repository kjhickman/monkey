using Mono.Cecil;
using Kong.Parsing;
using System.Collections.Concurrent;

namespace Kong.Semantic;

public sealed record StaticClrMethodBinding(
    string MethodPath,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType,
    MethodDefinition MethodDefinition,
    IReadOnlyList<TypeSymbol> MethodTypeArguments);

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
    private static readonly ConcurrentDictionary<string, MethodDefinition[]> ExtensionMethodCandidatesCache = new(StringComparer.Ordinal);

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
            if (argument.Type is InferredLambdaPlaceholderTypeSymbol)
            {
                continue;
            }

            if (!TryMapKongTypeToClrFullName(argument.Type, out _))
            {
                error = StaticClrMethodResolutionError.UnsupportedParameterType;
                errorMessage = $"static calls do not support Kong type '{argument.Type}' in parameter list";
                return false;
            }
        }

        var scoredMatches = new List<(MethodDefinition Method, int Score, TypeSymbol ReturnType, IReadOnlyList<TypeSymbol> MethodTypeArguments, IReadOnlyList<TypeSymbol> ExpectedArgumentTypes)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (TryGetCandidateMatch(candidate, arguments, out var score, out var returnType, out var methodTypeArguments, out var expectedArgumentTypes))
            {
                scoredMatches.Add((candidate, score, returnType, methodTypeArguments, expectedArgumentTypes));
            }
        }

        if (scoredMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.NoMatchingOverload;
            errorMessage = $"no matching overload for static method '{methodPath}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var bestScore = scoredMatches.Min(m => m.Score);
        var matches = scoredMatches.Where(m => m.Score == bestScore).ToList();
        var compatibleMatches = matches.Where(m => m.ReturnType != TypeSymbols.Error).ToList();
        compatibleMatches = compatibleMatches
            .GroupBy(m => m.Method.FullName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        if (compatibleMatches.Count > 1)
        {
            var nonGenericMatches = compatibleMatches.Where(m => !m.Method.HasGenericParameters).ToList();
            if (nonGenericMatches.Count > 0)
            {
                compatibleMatches = nonGenericMatches;
            }
        }
        if (compatibleMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"static method '{methodPath}' returns unsupported CLR type '{matches[0].Method.ReturnType.FullName}'";
            return false;
        }

        if (compatibleMatches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous static method call '{methodPath}' with argument types ({string.Join(", ", arguments.Select(a => a.Type))})";
            return false;
        }

        var selected = compatibleMatches[0];

        binding = new StaticClrMethodBinding(methodPath, selected.ExpectedArgumentTypes, selected.ReturnType, selected.Method, selected.MethodTypeArguments);
        return true;
    }

    public static bool TryResolveExtensionMethod(
        IReadOnlyCollection<string> importedNamespaces,
        string memberName,
        TypeSymbol receiverType,
        IReadOnlyList<ClrCallArgument> arguments,
        out StaticClrMethodBinding binding,
        out StaticClrMethodResolutionError error,
        out string errorMessage)
    {
        binding = null!;
        error = StaticClrMethodResolutionError.None;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(memberName))
        {
            error = StaticClrMethodResolutionError.InvalidMethodPath;
            errorMessage = "invalid extension method name";
            return false;
        }

        if (importedNamespaces.Count == 0)
        {
            error = StaticClrMethodResolutionError.MethodNotFound;
            errorMessage = $"unknown extension method '{memberName}'";
            return false;
        }

        var callArguments = new ClrCallArgument[arguments.Count + 1];
        callArguments[0] = new ClrCallArgument(receiverType, CallArgumentModifier.None);
        for (var i = 0; i < arguments.Count; i++)
        {
            callArguments[i + 1] = arguments[i];
        }

        var candidates = GetExtensionMethodCandidates(importedNamespaces, memberName);
        var scoredMatches = new List<(MethodDefinition Method, int Score, TypeSymbol ReturnType, IReadOnlyList<TypeSymbol> MethodTypeArguments, IReadOnlyList<TypeSymbol> ExpectedArgumentTypes)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (TryGetCandidateMatch(candidate, callArguments, out var score, out var returnType, out var methodTypeArguments, out var expectedArgumentTypes))
            {
                scoredMatches.Add((candidate, score, returnType, methodTypeArguments, expectedArgumentTypes));
            }
        }

        if (scoredMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.NoMatchingOverload;
            errorMessage = $"no matching overload for extension method '{memberName}' on receiver type '{receiverType}' with argument types ({string.Join(", ", callArguments.Select(a => a.Type))})";
            return false;
        }

        var bestScore = scoredMatches.Min(m => m.Score);
        var matches = scoredMatches.Where(m => m.Score == bestScore).ToList();
        var compatibleMatches = matches.Where(m => m.ReturnType != TypeSymbols.Error).ToList();
        compatibleMatches = compatibleMatches
            .GroupBy(m => m.Method.FullName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        if (compatibleMatches.Count > 1)
        {
            var nonGenericMatches = compatibleMatches.Where(m => !m.Method.HasGenericParameters).ToList();
            if (nonGenericMatches.Count > 0)
            {
                compatibleMatches = nonGenericMatches;
            }
        }
        if (compatibleMatches.Count == 0)
        {
            error = StaticClrMethodResolutionError.UnsupportedReturnType;
            errorMessage = $"extension method '{memberName}' returns unsupported CLR type '{matches[0].Method.ReturnType.FullName}'";
            return false;
        }

        if (compatibleMatches.Count > 1)
        {
            error = StaticClrMethodResolutionError.AmbiguousOverload;
            errorMessage = $"ambiguous extension method call '{memberName}' for receiver type '{receiverType}'";
            return false;
        }

        var selected = compatibleMatches[0];
        var declaringType = NormalizeTypeName(selected.Method.DeclaringType.FullName);
        var methodPath = declaringType + "." + selected.Method.Name;
        binding = new StaticClrMethodBinding(methodPath, selected.ExpectedArgumentTypes, selected.ReturnType, selected.Method, selected.MethodTypeArguments);
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
            if (!TryMapClrTypeReferenceToKongType(property.PropertyType, out var propertyType))
            {
                error = StaticClrMethodResolutionError.UnsupportedReturnType;
                errorMessage = $"static member '{memberPath}' has unsupported CLR type '{property.PropertyType.FullName}'";
                return false;
            }

            binding = new StaticClrValueBinding(memberPath, propertyType, property.GetMethod!, null);
            return true;
        }

        var field = fields[0];
        if (!TryMapClrTypeReferenceToKongType(field.FieldType, out var fieldType))
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
            .Where(m => m.IsPublic && m.IsStatic && m.Name == methodName)
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

    private static IReadOnlyList<MethodDefinition> GetExtensionMethodCandidates(IReadOnlyCollection<string> importedNamespaces, string memberName)
    {
        var cacheKey = BuildExtensionMethodCacheKey(importedNamespaces, memberName);
        if (ExtensionMethodCandidatesCache.TryGetValue(cacheKey, out var cachedCandidates))
        {
            return cachedCandidates;
        }

        var namespaceSet = new HashSet<string>(importedNamespaces, StringComparer.Ordinal);
        var candidates = new List<MethodDefinition>();

        foreach (var assembly in Assemblies.Value)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    CollectExtensionMethodsFromType(type, namespaceSet, memberName, candidates);
                }
            }
        }

        var resolvedCandidates = candidates.ToArray();
        ExtensionMethodCandidatesCache[cacheKey] = resolvedCandidates;
        return resolvedCandidates;
    }

    private static string BuildExtensionMethodCacheKey(IReadOnlyCollection<string> importedNamespaces, string memberName)
    {
        var namespaces = importedNamespaces
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal);
        return string.Join(";", namespaces) + "|" + memberName;
    }

    private static void CollectExtensionMethodsFromType(
        TypeDefinition type,
        IReadOnlySet<string> importedNamespaces,
        string memberName,
        List<MethodDefinition> candidates)
    {
        if (!string.IsNullOrEmpty(type.Namespace) &&
            importedNamespaces.Contains(type.Namespace) &&
            type.IsAbstract && type.IsSealed)
        {
            foreach (var method in type.Methods)
            {
                if (!method.IsPublic || !method.IsStatic || method.Name != memberName || method.Parameters.Count == 0)
                {
                    continue;
                }

                var isExtensionMethod = method.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute") ||
                                        type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");
                if (isExtensionMethod)
                {
                    candidates.Add(method);
                }
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            CollectExtensionMethodsFromType(nestedType, importedNamespaces, memberName, candidates);
        }
    }

    private static bool TryGetCandidateMatch(
        MethodDefinition method,
        IReadOnlyList<ClrCallArgument> arguments,
        out int score,
        out TypeSymbol returnType,
        out IReadOnlyList<TypeSymbol> methodTypeArguments,
        out IReadOnlyList<TypeSymbol> expectedArgumentTypes)
    {
        score = int.MaxValue;
        returnType = TypeSymbols.Error;
        methodTypeArguments = [];
        expectedArgumentTypes = [];
        var hasLambdaPlaceholders = arguments.Any(a => a.Type is InferredLambdaPlaceholderTypeSymbol);

        var inference = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        if (!TryInferGenericArguments(method, arguments, inference, hasLambdaPlaceholders))
        {
            return false;
        }

        if (!TryGetParameterMatchScore(method, arguments, inference, out score))
        {
            return false;
        }

        if (!TryGetExpectedArgumentTypes(method, arguments, inference, out expectedArgumentTypes))
        {
            return false;
        }

        if (!TryMapClrTypeReferenceToKongType(method.ReturnType, inference, out returnType))
        {
            returnType = TypeSymbols.Error;
        }

        if (method.HasGenericParameters)
        {
            var resolvedArguments = new List<TypeSymbol>(method.GenericParameters.Count);
            foreach (var genericParameter in method.GenericParameters)
            {
                if (!inference.TryGetValue(genericParameter.Name, out var argumentType))
                {
                    if (!hasLambdaPlaceholders)
                    {
                        return false;
                    }

                    argumentType = new GenericParameterTypeSymbol(genericParameter.Name);
                }

                resolvedArguments.Add(argumentType);
            }

            methodTypeArguments = resolvedArguments;
        }

        return true;
    }

    private static bool TryGetExpectedArgumentTypes(
        MethodDefinition method,
        IReadOnlyList<ClrCallArgument> arguments,
        IReadOnlyDictionary<string, TypeSymbol> inference,
        out IReadOnlyList<TypeSymbol> expectedArgumentTypes)
    {
        expectedArgumentTypes = [];
        var resolved = new List<TypeSymbol>(arguments.Count);
        var parameters = method.Parameters;
        var hasParamsArray = HasParamsArray(method, inference, out var paramsElementType);
        var fixedParameterCount = hasParamsArray ? parameters.Count - 1 : parameters.Count;

        for (var i = 0; i < arguments.Count; i++)
        {
            if (i < fixedParameterCount)
            {
                if (!TryGetParameterTypeAndModifier(parameters[i], inference, out var parameterType, out _) || parameterType == TypeSymbols.Void)
                {
                    return false;
                }

                resolved.Add(parameterType);
                continue;
            }

            if (!hasParamsArray || paramsElementType == TypeSymbols.Error)
            {
                return false;
            }

            resolved.Add(paramsElementType);
        }

        expectedArgumentTypes = resolved;
        return true;
    }

    private static bool TryInferGenericArguments(
        MethodDefinition method,
        IReadOnlyList<ClrCallArgument> arguments,
        Dictionary<string, TypeSymbol> inference,
        bool allowPartialInference)
    {
        if (!method.HasGenericParameters)
        {
            return true;
        }

        var parameters = method.Parameters;
        var hasParamsArray = HasParamsArray(method, inference, out var _);
        var fixedCount = hasParamsArray ? parameters.Count - 1 : parameters.Count;
        var directCount = Math.Min(arguments.Count, fixedCount);

        for (var i = 0; i < directCount; i++)
        {
            var parameterType = parameters[i].ParameterType is ByReferenceType byReferenceType
                ? byReferenceType.ElementType
                : parameters[i].ParameterType;
            if (!TryInferGenericFromClrType(parameterType, arguments[i].Type, inference))
            {
                return false;
            }
        }

        if (hasParamsArray && parameters[^1].ParameterType is Mono.Cecil.ArrayType paramsArray)
        {
            for (var i = fixedCount; i < arguments.Count; i++)
            {
                if (!TryInferGenericFromClrType(paramsArray.ElementType, arguments[i].Type, inference))
                {
                    return false;
                }
            }
        }

        foreach (var genericParameter in method.GenericParameters)
        {
            if (!inference.ContainsKey(genericParameter.Name) && !allowPartialInference)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryInferGenericFromClrType(TypeReference parameterType, TypeSymbol argumentType, Dictionary<string, TypeSymbol> inference)
    {
        if (parameterType is GenericParameter genericParameter)
        {
            if (inference.TryGetValue(genericParameter.Name, out var existing))
            {
                return TypeEquals(existing, argumentType);
            }

            inference[genericParameter.Name] = argumentType;
            return true;
        }

        if (parameterType is ByReferenceType byReferenceType)
        {
            return TryInferGenericFromClrType(byReferenceType.ElementType, argumentType, inference);
        }

        if (parameterType is Mono.Cecil.ArrayType arrayType)
        {
            return argumentType is ArrayTypeSymbol arrayArgument &&
                   TryInferGenericFromClrType(arrayType.ElementType, arrayArgument.ElementType, inference);
        }

        if (parameterType is GenericInstanceType genericInstanceType)
        {
            if (TryInferGenericFromDelegateType(genericInstanceType, argumentType, inference))
            {
                return true;
            }

            if (genericInstanceType.GenericArguments.Count == 1 &&
                genericInstanceType.ElementType.FullName is "System.Collections.Generic.IEnumerable`1" or
                    "System.Linq.IOrderedEnumerable`1" or
                    "System.Linq.IQueryable`1" &&
                argumentType is ArrayTypeSymbol arrayArgument)
            {
                return TryInferGenericFromClrType(genericInstanceType.GenericArguments[0], arrayArgument.ElementType, inference);
            }

            if (genericInstanceType.GenericArguments.Count == 1 &&
                genericInstanceType.ElementType.FullName is "System.Collections.Generic.IEnumerable`1" or
                    "System.Linq.IOrderedEnumerable`1" or
                    "System.Linq.IQueryable`1" &&
                argumentType is ClrGenericTypeSymbol { TypeArguments.Count: 1 } clrGenericArgumentForSequence)
            {
                return TryInferGenericFromClrType(genericInstanceType.GenericArguments[0], clrGenericArgumentForSequence.TypeArguments[0], inference);
            }

            if (argumentType is ClrGenericTypeSymbol clrGenericArgument &&
                string.Equals(NormalizeTypeName(genericInstanceType.ElementType.FullName), clrGenericArgument.GenericTypeName, StringComparison.Ordinal) &&
                genericInstanceType.GenericArguments.Count == clrGenericArgument.TypeArguments.Count)
            {
                for (var i = 0; i < genericInstanceType.GenericArguments.Count; i++)
                {
                    if (!TryInferGenericFromClrType(genericInstanceType.GenericArguments[i], clrGenericArgument.TypeArguments[i], inference))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return true;
    }

    private static bool TryInferGenericFromDelegateType(GenericInstanceType parameterType, TypeSymbol argumentType, Dictionary<string, TypeSymbol> inference)
    {
        var delegateName = NormalizeTypeName(parameterType.ElementType.FullName);
        if (delegateName == "System.Action")
        {
            return argumentType is FunctionTypeSymbol { ParameterTypes.Count: 0, ReturnType: var returnType } && returnType == TypeSymbols.Void;
        }

        if (!delegateName.StartsWith("System.Func`", StringComparison.Ordinal) &&
            !delegateName.StartsWith("System.Action`", StringComparison.Ordinal))
        {
            return false;
        }

        if (argumentType is not FunctionTypeSymbol functionType)
        {
            return false;
        }

        var expectedParameterCount = delegateName.StartsWith("System.Func`", StringComparison.Ordinal)
            ? parameterType.GenericArguments.Count - 1
            : parameterType.GenericArguments.Count;
        if (functionType.ParameterTypes.Count != expectedParameterCount)
        {
            return false;
        }

        for (var i = 0; i < expectedParameterCount; i++)
        {
            if (!TryInferGenericFromClrType(parameterType.GenericArguments[i], functionType.ParameterTypes[i], inference))
            {
                return false;
            }
        }

        if (delegateName.StartsWith("System.Func`", StringComparison.Ordinal))
        {
            return TryInferGenericFromClrType(parameterType.GenericArguments[^1], functionType.ReturnType, inference);
        }

        return functionType.ReturnType == TypeSymbols.Void;
    }

    private static bool TryGetParameterMatchScore(
        MethodDefinition method,
        IReadOnlyList<ClrCallArgument> arguments,
        IReadOnlyDictionary<string, TypeSymbol> inference,
        out int score)
    {
        score = 0;

        var parameters = method.Parameters;
        var hasParamsArray = HasParamsArray(method, inference, out var paramsElementType);
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
            TryScoreDirectOrOptionalCall(parameters, arguments, inference, out var directScore))
        {
            bestScore = directScore;
        }

        if (hasParamsArray && paramsElementType != TypeSymbols.Error &&
            arguments.Count >= fixedParameterCount &&
            TryScoreExpandedParamsCall(parameters, fixedParameterCount, paramsElementType, arguments, inference, out var expandedScore))
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
        IReadOnlyDictionary<string, TypeSymbol> inference,
        out int score)
    {
        score = 0;

        if (arguments.Count > parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            if (!TryGetParameterTypeAndModifier(parameters[i], inference, out var parameterType, out var expectedModifier) ||
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
        IReadOnlyDictionary<string, TypeSymbol> inference,
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

            if (!TryGetParameterTypeAndModifier(parameters[i], inference, out var parameterType, out var expectedModifier) ||
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

        if (TypeEquals(source, target))
        {
            score = 0;
            return true;
        }

        if (source is InferredLambdaPlaceholderTypeSymbol placeholder &&
            target is FunctionTypeSymbol placeholderTargetFunction &&
            placeholder.ParameterCount == placeholderTargetFunction.ParameterTypes.Count)
        {
            score = 40;
            return true;
        }

        if (source is FunctionTypeSymbol sourceFunction && target is FunctionTypeSymbol targetFunction)
        {
            if (sourceFunction.ParameterTypes.Count != targetFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < sourceFunction.ParameterTypes.Count; i++)
            {
                if (!TypeEquals(sourceFunction.ParameterTypes[i], targetFunction.ParameterTypes[i]))
                {
                    return false;
                }
            }

            if (TryGetConversionScore(sourceFunction.ReturnType, targetFunction.ReturnType, out var returnScore))
            {
                score = returnScore + 1;
                return true;
            }

            return false;
        }

        if (source is ArrayTypeSymbol sourceArray &&
            target is ClrGenericTypeSymbol targetGeneric &&
            targetGeneric.TypeArguments.Count == 1 &&
            targetGeneric.GenericTypeName is "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.IReadOnlyList`1" or "System.Collections.Generic.IReadOnlyCollection`1" &&
            TypeEquals(sourceArray.ElementType, targetGeneric.TypeArguments[0]))
        {
            score = 1;
            return true;
        }

        if (source is ArrayTypeSymbol &&
            target is ClrNominalTypeSymbol nominalTarget &&
            nominalTarget.ClrTypeFullName is "System.Collections.IEnumerable")
        {
            score = 2;
            return true;
        }

        if (source is ClrGenericTypeSymbol sourceGeneric &&
            target is ClrGenericTypeSymbol targetGenericType &&
            sourceGeneric.TypeArguments.Count == 1 &&
            targetGenericType.TypeArguments.Count == 1 &&
            targetGenericType.GenericTypeName is "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.IReadOnlyList`1" or "System.Collections.Generic.IReadOnlyCollection`1" &&
            TypeEquals(sourceGeneric.TypeArguments[0], targetGenericType.TypeArguments[0]))
        {
            score = 1;
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
            if (!TryMapClrTypeReferenceToKongType(parameter.ParameterType, null, out var parameterType))
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

        if (type is ClrNominalTypeSymbol nominalType)
        {
            clrTypeName = nominalType.ClrTypeFullName;
            return true;
        }

        if (type is ClrGenericTypeSymbol genericType)
        {
            clrTypeName = genericType.GenericTypeName;
            return true;
        }

        if (type is FunctionTypeSymbol functionType)
        {
            return TryMapFunctionTypeToClrFullName(functionType, out clrTypeName);
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

    private static bool TryMapFunctionTypeToClrFullName(FunctionTypeSymbol functionType, out string clrTypeName)
    {
        if (functionType.ReturnType == TypeSymbols.Void)
        {
            clrTypeName = functionType.ParameterTypes.Count switch
            {
                0 => "System.Action",
                <= 16 => $"System.Action`{functionType.ParameterTypes.Count}",
                _ => string.Empty,
            };

            return clrTypeName.Length > 0;
        }

        if (functionType.ParameterTypes.Count > 16)
        {
            clrTypeName = string.Empty;
            return false;
        }

        clrTypeName = $"System.Func`{functionType.ParameterTypes.Count + 1}";
        return true;
    }

    private static bool TryMapClrTypeReferenceToKongType(TypeReference clrType, out TypeSymbol type)
    {
        return TryMapClrTypeReferenceToKongType(clrType, null, out type);
    }

    private static bool TryMapClrTypeReferenceToKongType(
        TypeReference clrType,
        IReadOnlyDictionary<string, TypeSymbol>? genericArguments,
        out TypeSymbol type)
    {
        if (clrType is GenericParameter genericParameter)
        {
            if (genericArguments != null && genericArguments.TryGetValue(genericParameter.Name, out var inferredType))
            {
                type = inferredType;
                return true;
            }

            type = new GenericParameterTypeSymbol(genericParameter.Name);
            return true;
        }

        if (clrType is ByReferenceType byReferenceType)
        {
            return TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, genericArguments, out type);
        }

        if (clrType is Mono.Cecil.ArrayType arrayType)
        {
            if (!TryMapClrTypeReferenceToKongType(arrayType.ElementType, genericArguments, out var elementType))
            {
                type = TypeSymbols.Error;
                return false;
            }

            type = new ArrayTypeSymbol(elementType);
            return true;
        }

        if (clrType is GenericInstanceType genericInstanceType)
        {
            if (TryMapFuncOrActionType(genericInstanceType, genericArguments, out type))
            {
                return true;
            }

            var genericTypeName = NormalizeTypeName(genericInstanceType.ElementType.FullName);

            var genericTypeArguments = new List<TypeSymbol>(genericInstanceType.GenericArguments.Count);
            foreach (var genericArgument in genericInstanceType.GenericArguments)
            {
                if (!TryMapClrTypeReferenceToKongType(genericArgument, genericArguments, out var mappedArgument))
                {
                    type = TypeSymbols.Error;
                    return false;
                }

                genericTypeArguments.Add(mappedArgument);
            }

            type = new ClrGenericTypeSymbol(genericTypeName, genericTypeArguments);
            return true;
        }

        var normalized = NormalizeTypeName(clrType.FullName);
        if (TryMapClrFullNameToKongType(normalized, out type))
        {
            return true;
        }

        type = TypeSymbols.Error;
        return false;
    }

    private static bool TryMapFuncOrActionType(
        GenericInstanceType genericInstanceType,
        IReadOnlyDictionary<string, TypeSymbol>? genericArguments,
        out TypeSymbol type)
    {
        var normalizedName = NormalizeTypeName(genericInstanceType.ElementType.FullName);
        if (normalizedName == "System.Action")
        {
            type = new FunctionTypeSymbol([], TypeSymbols.Void);
            return true;
        }

        if (normalizedName.StartsWith("System.Action`", StringComparison.Ordinal))
        {
            var parameterTypes = new List<TypeSymbol>(genericInstanceType.GenericArguments.Count);
            foreach (var genericArgument in genericInstanceType.GenericArguments)
            {
                if (!TryMapClrTypeReferenceToKongType(genericArgument, genericArguments, out var parameterType))
                {
                    type = TypeSymbols.Error;
                    return false;
                }

                parameterTypes.Add(parameterType);
            }

            type = new FunctionTypeSymbol(parameterTypes, TypeSymbols.Void);
            return true;
        }

        if (normalizedName.StartsWith("System.Func`", StringComparison.Ordinal) && genericInstanceType.GenericArguments.Count > 0)
        {
            var parameterTypes = new List<TypeSymbol>(genericInstanceType.GenericArguments.Count - 1);
            for (var i = 0; i < genericInstanceType.GenericArguments.Count - 1; i++)
            {
                if (!TryMapClrTypeReferenceToKongType(genericInstanceType.GenericArguments[i], genericArguments, out var parameterType))
                {
                    type = TypeSymbols.Error;
                    return false;
                }

                parameterTypes.Add(parameterType);
            }

            if (!TryMapClrTypeReferenceToKongType(genericInstanceType.GenericArguments[^1], genericArguments, out var returnType))
            {
                type = TypeSymbols.Error;
                return false;
            }

            type = new FunctionTypeSymbol(parameterTypes, returnType);
            return true;
        }

        type = TypeSymbols.Error;
        return false;
    }

    private static bool HasParamsArray(MethodDefinition method, out TypeSymbol elementType)
    {
        return HasParamsArray(method, null, out elementType);
    }

    private static bool HasParamsArray(MethodDefinition method, IReadOnlyDictionary<string, TypeSymbol>? genericArguments, out TypeSymbol elementType)
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

        return TryMapClrTypeReferenceToKongType(paramsArrayType.ElementType, genericArguments, out elementType);
    }

    private static bool IsOptionalParameter(ParameterDefinition parameter)
    {
        return parameter.IsOptional || parameter.HasDefault;
    }

    private static bool TryGetParameterTypeAndModifier(
        ParameterDefinition parameter,
        IReadOnlyDictionary<string, TypeSymbol>? genericArguments,
        out TypeSymbol parameterType,
        out CallArgumentModifier modifier)
    {
        modifier = CallArgumentModifier.None;
        if (parameter.ParameterType is ByReferenceType byReferenceType)
        {
            if (!TryMapClrTypeReferenceToKongType(byReferenceType.ElementType, genericArguments, out parameterType))
            {
                return false;
            }

            modifier = parameter.IsOut ? CallArgumentModifier.Out : CallArgumentModifier.Ref;
            return true;
        }

        return TryMapClrTypeReferenceToKongType(parameter.ParameterType, genericArguments, out parameterType);
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

    private static bool TypeEquals(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is ArrayTypeSymbol leftArray && right is ArrayTypeSymbol rightArray)
        {
            return TypeEquals(leftArray.ElementType, rightArray.ElementType);
        }

        if (left is FunctionTypeSymbol leftFunction && right is FunctionTypeSymbol rightFunction)
        {
            if (!TypeEquals(leftFunction.ReturnType, rightFunction.ReturnType) ||
                leftFunction.ParameterTypes.Count != rightFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftFunction.ParameterTypes.Count; i++)
            {
                if (!TypeEquals(leftFunction.ParameterTypes[i], rightFunction.ParameterTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is ClrGenericTypeSymbol leftClrGeneric && right is ClrGenericTypeSymbol rightClrGeneric)
        {
            if (!string.Equals(leftClrGeneric.GenericTypeName, rightClrGeneric.GenericTypeName, StringComparison.Ordinal) ||
                leftClrGeneric.TypeArguments.Count != rightClrGeneric.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftClrGeneric.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftClrGeneric.TypeArguments[i], rightClrGeneric.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return left == right;
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
