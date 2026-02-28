using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantics;
using Kong.Semantics.Symbols;

namespace Kong.Tests.Semantics;

public class TypeInfererSymbolTests
{
    [Fact]
    public void InferTypes_TracksStructuredFunctionParameterTypes()
    {
        var program = Parse("let get = fn(h: map[string]int, values: int[]) { h[\"answer\"] }; ");
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        Assert.True(types.TryGetFunctionSignature("get", out var signature));
        var mapType = Assert.IsType<MapTypeSymbol>(signature.ParameterTypes[0]);
        Assert.Equal(TypeSymbol.String, mapType.KeyType);
        Assert.Equal(TypeSymbol.Int, mapType.ValueType);

        var arrayType = Assert.IsType<ArrayTypeSymbol>(signature.ParameterTypes[1]);
        Assert.Equal(TypeSymbol.Int, arrayType.ElementType);
    }

    [Fact]
    public void InferTypes_DoesNotOverrideTopLevelFunctionSignaturesWithNestedFunctions()
    {
        const string source = "let add = fn(a: int, b: int) { a + b }; let wrapper = fn() { let add = fn(x: int) { x + 1 }; add(41); }; puts(wrapper());";
        var program = Parse(source);
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        Assert.True(types.TryGetFunctionSignature("add", out var signature));
        Assert.Equal(2, signature.ParameterTypes.Count);
        Assert.Equal(TypeSymbol.Int, signature.ParameterTypes[0]);
        Assert.Equal(TypeSymbol.Int, signature.ParameterTypes[1]);
        Assert.Equal(TypeSymbol.Int, signature.ReturnType);
    }

    [Fact]
    public void InferTypes_ExposesRichNodeTypesAndKongTypeCompatibility()
    {
        var program = Parse("let nums = [1, 2, 3];");
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        var letStatement = Assert.IsType<LetStatement>(program.Statements[0]);
        var arrayLiteral = Assert.IsType<ArrayLiteral>(letStatement.Value);
        var arrayType = Assert.IsType<ArrayTypeSymbol>(types.GetNodeTypeSymbol(arrayLiteral));

        Assert.Equal(TypeSymbol.Int, arrayType.ElementType);
        Assert.Equal(KongType.Array, types.GetNodeType(arrayLiteral));
    }

    private static Program Parse(string source)
    {
        var parser = new Parser(new Lexer(source));
        var program = parser.ParseProgram();
        Assert.Empty(parser.Errors());
        return program;
    }
}
