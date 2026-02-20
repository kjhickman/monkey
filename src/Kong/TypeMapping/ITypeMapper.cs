namespace Kong.TypeMapping;

using Mono.Cecil;

public interface ITypeMapper
{
    TypeReference? TryMapKongType(
        TypeSymbol kongType,
        ModuleDefinition module,
        DiagnosticBag diagnostics);

    bool IsTypeSupported(TypeSymbol type);
}
