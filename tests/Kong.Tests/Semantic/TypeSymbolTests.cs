using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class TypeSymbolTests
{
    [Fact]
    public void TestPrimitiveTypeSymbols()
    {
        Assert.Equal("int", TypeSymbols.Int.Name);
        Assert.Equal("long", TypeSymbols.Long.Name);
        Assert.Equal("double", TypeSymbols.Double.Name);
        Assert.Equal("char", TypeSymbols.Char.Name);
        Assert.Equal("byte", TypeSymbols.Byte.Name);
        Assert.Equal("sbyte", TypeSymbols.SByte.Name);
        Assert.Equal("short", TypeSymbols.Short.Name);
        Assert.Equal("ushort", TypeSymbols.UShort.Name);
        Assert.Equal("uint", TypeSymbols.UInt.Name);
        Assert.Equal("ulong", TypeSymbols.ULong.Name);
        Assert.Equal("nint", TypeSymbols.NInt.Name);
        Assert.Equal("nuint", TypeSymbols.NUInt.Name);
        Assert.Equal("float", TypeSymbols.Float.Name);
        Assert.Equal("decimal", TypeSymbols.Decimal.Name);
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
        Assert.Same(TypeSymbols.Double, TypeSymbols.TryGetPrimitive("double"));
        Assert.Same(TypeSymbols.Char, TypeSymbols.TryGetPrimitive("char"));
        Assert.Same(TypeSymbols.Byte, TypeSymbols.TryGetPrimitive("byte"));
        Assert.Same(TypeSymbols.SByte, TypeSymbols.TryGetPrimitive("sbyte"));
        Assert.Same(TypeSymbols.Short, TypeSymbols.TryGetPrimitive("short"));
        Assert.Same(TypeSymbols.UShort, TypeSymbols.TryGetPrimitive("ushort"));
        Assert.Same(TypeSymbols.UInt, TypeSymbols.TryGetPrimitive("uint"));
        Assert.Same(TypeSymbols.ULong, TypeSymbols.TryGetPrimitive("ulong"));
        Assert.Same(TypeSymbols.NInt, TypeSymbols.TryGetPrimitive("nint"));
        Assert.Same(TypeSymbols.NUInt, TypeSymbols.TryGetPrimitive("nuint"));
        Assert.Same(TypeSymbols.Float, TypeSymbols.TryGetPrimitive("float"));
        Assert.Same(TypeSymbols.Decimal, TypeSymbols.TryGetPrimitive("decimal"));
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

        Assert.Equal("(int, string) -> bool", functionType.Name);
        Assert.Equal("(int, string) -> bool", functionType.ToString());
    }
}
