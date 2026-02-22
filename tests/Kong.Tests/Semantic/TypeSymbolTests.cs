using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class TypeSymbolTests
{
    [Fact]
    public void TestPrimitiveTypeSymbols()
    {
        Assert.Equal("int", TypeSymbols.Int.Name);
        Assert.Equal("long", TypeSymbols.Long.Name);
        Assert.Equal("string", TypeSymbols.String.Name);
        Assert.Equal("bool", TypeSymbols.Bool.Name);
        Assert.Equal("void", TypeSymbols.Void.Name);
        Assert.Equal("null", TypeSymbols.Null.Name);
    }

    [Fact]
    public void TestPrimitiveTypeSymbolsAreSingletons()
    {
        Assert.Same(TypeSymbols.Int, TypeSymbols.TryGetPrimitive("int"));
        Assert.Same(TypeSymbols.Long, TypeSymbols.TryGetPrimitive("long"));
        Assert.Same(TypeSymbols.String, TypeSymbols.TryGetPrimitive("string"));
        Assert.Same(TypeSymbols.Bool, TypeSymbols.TryGetPrimitive("bool"));
        Assert.Same(TypeSymbols.Void, TypeSymbols.TryGetPrimitive("void"));
        Assert.Same(TypeSymbols.Null, TypeSymbols.TryGetPrimitive("null"));
    }

    [Fact]
    public void TestArrayTypeSymbolName()
    {
        var array = new ArrayTypeSymbol(TypeSymbols.Int);

        Assert.Equal("int[]", array.Name);
        Assert.Equal("int[]", array.ToString());
    }

    [Fact]
    public void TestFunctionTypeSymbolName()
    {
        var functionType = new FunctionTypeSymbol([TypeSymbols.Int, TypeSymbols.String], TypeSymbols.Bool);

        Assert.Equal("fn(int, string) -> bool", functionType.Name);
        Assert.Equal("fn(int, string) -> bool", functionType.ToString());
    }
}
