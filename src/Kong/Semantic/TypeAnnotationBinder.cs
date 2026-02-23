using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public static class TypeAnnotationBinder
{
    public static TypeSymbol? Bind(
        ITypeNode typeNode,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes = null)
    {
        return typeNode switch
        {
            NamedType namedType => BindNamedType(namedType, diagnostics, namedTypes),
            GenericType genericType => BindGenericType(genericType, diagnostics, namedTypes),
            ArrayType arrayType => BindArrayType(arrayType, diagnostics, namedTypes),
            FunctionType functionType => BindFunctionType(functionType, diagnostics, namedTypes),
            _ => BindUnknownType(typeNode, diagnostics),
        };
    }

    private static TypeSymbol? BindNamedType(
        NamedType namedType,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes)
    {
        var type = TypeSymbols.TryGetPrimitive(namedType.Name);
        if (type != null)
        {
            return type;
        }

        if (namedTypes != null && namedTypes.TryGetValue(namedType.Name, out var namedTypeSymbol))
        {
            if (namedTypeSymbol is EnumTypeSymbol enumType && enumType.TypeArguments.Count > 0)
            {
                diagnostics.Report(namedType.Span,
                    $"generic enum '{namedType.Name}' requires {enumType.TypeArguments.Count} type argument(s)",
                    "T001");
                return null;
            }

            if (namedTypeSymbol is ClassTypeSymbol classType && classType.TypeArguments.Count > 0)
            {
                diagnostics.Report(namedType.Span,
                    $"generic class '{namedType.Name}' requires {classType.TypeArguments.Count} type argument(s)",
                    "T001");
                return null;
            }

            if (namedTypeSymbol is InterfaceTypeSymbol interfaceType && interfaceType.TypeArguments.Count > 0)
            {
                diagnostics.Report(namedType.Span,
                    $"generic interface '{namedType.Name}' requires {interfaceType.TypeArguments.Count} type argument(s)",
                    "T001");
                return null;
            }

            return namedTypeSymbol;
        }

        diagnostics.Report(namedType.Span, $"unknown type '{namedType.Name}'", "T001");
        return null;
    }

    private static TypeSymbol? BindGenericType(
        GenericType genericType,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes)
    {
        if (namedTypes == null || !namedTypes.TryGetValue(genericType.Name, out var genericTarget))
        {
            diagnostics.Report(genericType.Span, $"unknown generic type '{genericType.Name}'", "T001");
            return null;
        }

        if (genericTarget is EnumTypeSymbol enumType)
        {
            if (genericType.TypeArguments.Count != enumType.TypeArguments.Count)
            {
                diagnostics.Report(genericType.Span,
                    $"wrong number of type arguments for '{genericType.Name}': expected {enumType.TypeArguments.Count}, got {genericType.TypeArguments.Count}",
                    "T001");
                return null;
            }

            var typeArguments = new List<TypeSymbol>(genericType.TypeArguments.Count);
            foreach (var typeArgumentNode in genericType.TypeArguments)
            {
                var typeArgument = Bind(typeArgumentNode, diagnostics, namedTypes);
                if (typeArgument == null)
                {
                    return null;
                }

                typeArguments.Add(typeArgument);
            }

            return enumType with { TypeArguments = typeArguments };
        }

        if (genericTarget is ClassTypeSymbol classType)
        {
            if (genericType.TypeArguments.Count != classType.TypeArguments.Count)
            {
                diagnostics.Report(genericType.Span,
                    $"wrong number of type arguments for '{genericType.Name}': expected {classType.TypeArguments.Count}, got {genericType.TypeArguments.Count}",
                    "T001");
                return null;
            }

            var typeArguments = new List<TypeSymbol>(genericType.TypeArguments.Count);
            foreach (var typeArgumentNode in genericType.TypeArguments)
            {
                var typeArgument = Bind(typeArgumentNode, diagnostics, namedTypes);
                if (typeArgument == null)
                {
                    return null;
                }

                typeArguments.Add(typeArgument);
            }

            return new ClassTypeSymbol(classType.ClassName, typeArguments);
        }

        if (genericTarget is InterfaceTypeSymbol interfaceType)
        {
            if (genericType.TypeArguments.Count != interfaceType.TypeArguments.Count)
            {
                diagnostics.Report(genericType.Span,
                    $"wrong number of type arguments for '{genericType.Name}': expected {interfaceType.TypeArguments.Count}, got {genericType.TypeArguments.Count}",
                    "T001");
                return null;
            }

            var typeArguments = new List<TypeSymbol>(genericType.TypeArguments.Count);
            foreach (var typeArgumentNode in genericType.TypeArguments)
            {
                var typeArgument = Bind(typeArgumentNode, diagnostics, namedTypes);
                if (typeArgument == null)
                {
                    return null;
                }

                typeArguments.Add(typeArgument);
            }

            return new InterfaceTypeSymbol(interfaceType.InterfaceName, typeArguments);
        }

        diagnostics.Report(genericType.Span, $"type '{genericType.Name}' does not accept type arguments", "T001");
        return null;
    }

    private static TypeSymbol? BindArrayType(
        ArrayType arrayType,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes)
    {
        var elementType = Bind(arrayType.ElementType, diagnostics, namedTypes);
        if (elementType == null)
        {
            return null;
        }

        return new ArrayTypeSymbol(elementType);
    }

    private static TypeSymbol? BindFunctionType(
        FunctionType functionType,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeSymbol>? namedTypes)
    {
        var parameterTypes = new List<TypeSymbol>(functionType.ParameterTypes.Count);
        foreach (var parameterTypeNode in functionType.ParameterTypes)
        {
            var parameterType = Bind(parameterTypeNode, diagnostics, namedTypes);
            if (parameterType == null)
            {
                return null;
            }

            parameterTypes.Add(parameterType);
        }

        var returnType = Bind(functionType.ReturnType, diagnostics, namedTypes);
        if (returnType == null)
        {
            return null;
        }

        return new FunctionTypeSymbol(parameterTypes, returnType);
    }

    private static TypeSymbol? BindUnknownType(ITypeNode typeNode, DiagnosticBag diagnostics)
    {
        diagnostics.Report(typeNode.Span, $"unsupported type syntax '{typeNode.TokenLiteral()}'", "T002");
        return null;
    }
}
