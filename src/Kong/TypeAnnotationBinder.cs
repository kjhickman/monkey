namespace Kong;

public static class TypeAnnotationBinder
{
    public static TypeSymbol? Bind(ITypeNode typeNode, DiagnosticBag diagnostics)
    {
        return typeNode switch
        {
            NamedType namedType => BindNamedType(namedType, diagnostics),
            ArrayType arrayType => BindArrayType(arrayType, diagnostics),
            _ => BindUnknownType(typeNode, diagnostics),
        };
    }

    private static TypeSymbol? BindNamedType(NamedType namedType, DiagnosticBag diagnostics)
    {
        var type = TypeSymbols.TryGetBuiltin(namedType.Name);
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

    private static TypeSymbol? BindUnknownType(ITypeNode typeNode, DiagnosticBag diagnostics)
    {
        diagnostics.Report(typeNode.Span, $"unsupported type syntax '{typeNode.TokenLiteral()}'", "T002");
        return null;
    }
}
