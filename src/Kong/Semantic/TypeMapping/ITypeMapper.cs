namespace Kong.Semantic.TypeMapping;

using Mono.Cecil;
using Kong.Common;
using Kong.Semantic;

public interface ITypeMapper
{
    TypeReference? TryMapKongType(
        TypeSymbol kongType,
        ModuleDefinition module,
        DiagnosticBag diagnostics);

    bool IsTypeSupported(TypeSymbol type);
}
