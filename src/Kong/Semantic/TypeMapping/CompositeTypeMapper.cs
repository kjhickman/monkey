namespace Kong.Semantic.TypeMapping;

using Mono.Cecil;
using Kong.Common;
using Kong.Semantic;

public class CompositeTypeMapper : ITypeMapper
{
    private readonly ITypeMapper _primary;
    private readonly ITypeMapper _fallback;

    public CompositeTypeMapper(ITypeMapper primary, ITypeMapper fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public TypeReference? TryMapKongType(
        TypeSymbol kongType,
        ModuleDefinition module,
        DiagnosticBag diagnostics)
    {
        var result = _primary.TryMapKongType(kongType, module, diagnostics);
        if (result != null)
        {
            return result;
        }

        return _fallback.TryMapKongType(kongType, module, diagnostics);
    }

    public bool IsTypeSupported(TypeSymbol type)
    {
        return _primary.IsTypeSupported(type) || _fallback.IsTypeSupported(type);
    }
}
