namespace Kong.Semantic.TypeMapping;

using Mono.Cecil;
using Kong.Common;
using Kong.Semantic;

public class DefaultTypeMapper : ITypeMapper
{
    private readonly IReadOnlyDictionary<string, TypeDefinition> _delegateTypeMap;
    private readonly IReadOnlyDictionary<string, TypeDefinition> _enumTypeMap;
    private readonly IReadOnlyDictionary<string, TypeDefinition> _classTypeMap;
    private readonly IReadOnlyDictionary<string, TypeDefinition> _interfaceTypeMap;

    public DefaultTypeMapper(
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap,
        IReadOnlyDictionary<string, TypeDefinition>? enumTypeMap = null,
        IReadOnlyDictionary<string, TypeDefinition>? classTypeMap = null,
        IReadOnlyDictionary<string, TypeDefinition>? interfaceTypeMap = null)
    {
        _delegateTypeMap = delegateTypeMap;
        _enumTypeMap = enumTypeMap ?? new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        _classTypeMap = classTypeMap ?? new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        _interfaceTypeMap = interfaceTypeMap ?? new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
    }

    public TypeReference? TryMapKongType(
        TypeSymbol kongType,
        ModuleDefinition module,
        DiagnosticBag diagnostics)
    {
        if (kongType == TypeSymbols.Int)
        {
            return module.TypeSystem.Int32;
        }

        if (kongType == TypeSymbols.Long)
        {
            return module.TypeSystem.Int64;
        }

        if (kongType == TypeSymbols.Double)
        {
            return module.TypeSystem.Double;
        }

        if (kongType == TypeSymbols.Char)
        {
            return module.TypeSystem.Char;
        }

        if (kongType == TypeSymbols.Byte)
        {
            return module.TypeSystem.Byte;
        }

        if (kongType == TypeSymbols.SByte)
        {
            return module.TypeSystem.SByte;
        }

        if (kongType == TypeSymbols.Short)
        {
            return module.TypeSystem.Int16;
        }

        if (kongType == TypeSymbols.UShort)
        {
            return module.TypeSystem.UInt16;
        }

        if (kongType == TypeSymbols.UInt)
        {
            return module.TypeSystem.UInt32;
        }

        if (kongType == TypeSymbols.ULong)
        {
            return module.TypeSystem.UInt64;
        }

        if (kongType == TypeSymbols.NInt)
        {
            return module.TypeSystem.IntPtr;
        }

        if (kongType == TypeSymbols.NUInt)
        {
            return module.TypeSystem.UIntPtr;
        }

        if (kongType == TypeSymbols.Float)
        {
            return module.TypeSystem.Single;
        }

        if (kongType == TypeSymbols.Decimal)
        {
            return module.ImportReference(typeof(decimal));
        }

        if (kongType == TypeSymbols.Bool)
        {
            return module.TypeSystem.Boolean;
        }

        if (kongType == TypeSymbols.String)
        {
            return module.TypeSystem.String;
        }

        if (kongType == TypeSymbols.Void)
        {
            return module.TypeSystem.Void;
        }

        if (kongType is ArrayTypeSymbol arrayType)
        {
            var mappedElement = TryMapKongType(arrayType.ElementType, module, diagnostics);
            if (mappedElement == null)
            {
                return null;
            }

            return new ArrayType(mappedElement);
        }

        if (kongType is FunctionTypeSymbol functionType)
        {
            if (_delegateTypeMap.TryGetValue(functionType.Name, out var delegateType))
            {
                return delegateType;
            }

            diagnostics.Report(Span.Empty, $"CLR backend is missing delegate type for '{functionType}'", "IL001");
            return null;
        }

        if (kongType is ClrNominalTypeSymbol nominalType)
        {
            if (!ConstructorClrResolver.TryResolveTypeDefinition(nominalType.ClrTypeFullName, out var typeDefinition))
            {
                diagnostics.Report(Span.Empty, $"CLR backend could not load runtime type '{nominalType.ClrTypeFullName}'", "IL001");
                return null;
            }

            return module.ImportReference(typeDefinition);
        }

        if (kongType is EnumTypeSymbol enumType)
        {
            if (_enumTypeMap.TryGetValue(enumType.EnumName, out var enumDefinition))
            {
                return module.ImportReference(enumDefinition);
            }

            diagnostics.Report(Span.Empty, $"CLR backend is missing generated enum type for '{enumType.EnumName}'", "IL001");
            return null;
        }

        if (kongType is ClassTypeSymbol classType)
        {
            if (_classTypeMap.TryGetValue(classType.ClassName, out var classDefinition))
            {
                return module.ImportReference(classDefinition);
            }

            diagnostics.Report(Span.Empty, $"CLR backend is missing generated class type for '{classType.ClassName}'", "IL001");
            return null;
        }

        if (kongType is InterfaceTypeSymbol interfaceType)
        {
            if (_interfaceTypeMap.TryGetValue(interfaceType.InterfaceName, out var interfaceDefinition))
            {
                return module.ImportReference(interfaceDefinition);
            }

            return module.TypeSystem.Object;
        }

        diagnostics.Report(Span.Empty, $"CLR backend does not support type '{kongType}'", "IL001");
        return null;
    }

    public bool IsTypeSupported(TypeSymbol type)
    {
        if (type == TypeSymbols.Void)
        {
            return true;
        }

        if (type == TypeSymbols.Int ||
            type == TypeSymbols.Long ||
            type == TypeSymbols.Double ||
            type == TypeSymbols.Char ||
            type == TypeSymbols.Byte ||
            type == TypeSymbols.SByte ||
            type == TypeSymbols.Short ||
            type == TypeSymbols.UShort ||
            type == TypeSymbols.UInt ||
            type == TypeSymbols.ULong ||
            type == TypeSymbols.NInt ||
            type == TypeSymbols.NUInt ||
            type == TypeSymbols.Float ||
            type == TypeSymbols.Decimal ||
            type == TypeSymbols.Bool ||
            type == TypeSymbols.String)
        {
            return true;
        }

        if (type is ArrayTypeSymbol arrayType)
        {
            return IsTypeSupported(arrayType.ElementType);
        }

        if (type is FunctionTypeSymbol functionType)
        {
            return functionType.ParameterTypes.All(IsTypeSupported) &&
                   IsTypeSupported(functionType.ReturnType);
        }

        if (type is ClrNominalTypeSymbol)
        {
            return true;
        }

        if (type is EnumTypeSymbol)
        {
            return true;
        }

        if (type is ClassTypeSymbol)
        {
            return true;
        }

        if (type is InterfaceTypeSymbol)
        {
            return true;
        }

        return false;
    }
}
