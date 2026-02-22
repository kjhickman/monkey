using Kong.Semantic.TypeMapping;
using Kong.Common;
using Kong.Semantic;
using Mono.Cecil;
using CecilArrayType = Mono.Cecil.ArrayType;

namespace Kong.Tests.Semantic;

public class TypeMapperTests
{
    private ModuleDefinition CreateTestModule()
    {
        var assemblyName = new AssemblyNameDefinition("Test", new Version(1, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, "Test", ModuleKind.Console);
        return assembly.MainModule;
    }

    private IReadOnlyDictionary<string, TypeDefinition> CreateEmptyDelegateMap()
    {
        return new Dictionary<string, TypeDefinition>();
    }

    [Fact]
    public void TryMapKongType_Int_MapsToInt32()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Int, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Int32, mapped);
    }

    [Fact]
    public void TryMapKongType_Long_MapsToInt64()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Long, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Int64, mapped);
    }

    [Fact]
    public void TryMapKongType_Double_MapsToDouble()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Double, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Double, mapped);
    }

    [Fact]
    public void TryMapKongType_Char_MapsToChar()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Char, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Char, mapped);
    }

    [Fact]
    public void TryMapKongType_Byte_MapsToByte()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Byte, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Byte, mapped);
    }

    [Fact]
    public void TryMapKongType_Bool_MapsToBoolean()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Bool, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Boolean, mapped);
    }

    [Fact]
    public void TryMapKongType_String_MapsToString()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.String, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.String, mapped);
    }

    [Fact]
    public void TryMapKongType_Void_MapsToVoid()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();

        var mapped = mapper.TryMapKongType(TypeSymbols.Void, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.Equal(module.TypeSystem.Void, mapped);
    }

    [Fact]
    public void TryMapKongType_IntArray_MapsToInt32Array()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();
        var intArrayType = new ArrayTypeSymbol(TypeSymbols.Int);

        var mapped = mapper.TryMapKongType(intArrayType, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.IsType<CecilArrayType>(mapped);
        var arrayType = (CecilArrayType)mapped;
        Assert.Equal(module.TypeSystem.Int32, arrayType.ElementType);
    }

    [Fact]
    public void TryMapKongType_StringArray_MapsToStringArray()
    {
        var module = CreateTestModule();
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var diagnostics = new DiagnosticBag();
        var stringArrayType = new ArrayTypeSymbol(TypeSymbols.String);

        var mapped = mapper.TryMapKongType(stringArrayType, module, diagnostics);

        Assert.NotNull(mapped);
        Assert.IsType<CecilArrayType>(mapped);
        var arrayType = (CecilArrayType)mapped;
        Assert.Equal(module.TypeSystem.String, arrayType.ElementType);
    }

    [Fact]
    public void IsTypeSupported_Int_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Int));
    }

    [Fact]
    public void IsTypeSupported_Bool_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Bool));
    }

    [Fact]
    public void IsTypeSupported_Long_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Long));
    }

    [Fact]
    public void IsTypeSupported_Double_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Double));
    }

    [Fact]
    public void IsTypeSupported_Char_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Char));
    }

    [Fact]
    public void IsTypeSupported_Byte_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.Byte));
    }

    [Fact]
    public void IsTypeSupported_String_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());

        Assert.True(mapper.IsTypeSupported(TypeSymbols.String));
    }

    [Fact]
    public void IsTypeSupported_IntArray_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var intArrayType = new ArrayTypeSymbol(TypeSymbols.Int);

        Assert.True(mapper.IsTypeSupported(intArrayType));
    }

    [Fact]
    public void IsTypeSupported_StringArray_ReturnsTrue()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var stringArrayType = new ArrayTypeSymbol(TypeSymbols.String);

        Assert.True(mapper.IsTypeSupported(stringArrayType));
    }

    [Fact]
    public void IsTypeSupported_FunctionType_ReturnsTrueIfAllParametersSupported()
    {
        var mapper = new DefaultTypeMapper(CreateEmptyDelegateMap());
        var functionType = new FunctionTypeSymbol([TypeSymbols.Int], TypeSymbols.String);

        Assert.True(mapper.IsTypeSupported(functionType));
    }
}
