using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public static class TypeAnnotationBinder
{
    public static TypeSymbol? Bind(ITypeNode typeNode, DiagnosticBag diagnostics)
    {
        return typeNode switch
        {
            NamedType namedType => BindNamedType(namedType, diagnostics),
            ArrayType arrayType => BindArrayType(arrayType, diagnostics),
            FunctionType functionType => BindFunctionType(functionType, diagnostics),
            _ => BindUnknownType(typeNode, diagnostics),
        };
    }

    private static TypeSymbol? BindNamedType(NamedType namedType, DiagnosticBag diagnostics)
    {
        var type = TypeSymbols.TryGetPrimitive(namedType.Name);
        if (type != null)
        {
            return type;
        }

        diagnostics.Report(namedType.Span, $"unknown type '{namedType.Name}'", "T001");
        return null;
    }

    private static TypeSymbol? BindArrayType(ArrayType arrayType, DiagnosticBag diagnostics)
    {
        var elementType = Bind(arrayType.ElementType, diagnostics);
        if (elementType == null)
        {
            return null;
        }

        return new ArrayTypeSymbol(elementType);
    }

    private static TypeSymbol? BindFunctionType(FunctionType functionType, DiagnosticBag diagnostics)
    {
        var parameterTypes = new List<TypeSymbol>(functionType.ParameterTypes.Count);
        foreach (var parameterTypeNode in functionType.ParameterTypes)
        {
            var parameterType = Bind(parameterTypeNode, diagnostics);
            if (parameterType == null)
            {
                return null;
            }

            parameterTypes.Add(parameterType);
        }

        var returnType = Bind(functionType.ReturnType, diagnostics);
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
