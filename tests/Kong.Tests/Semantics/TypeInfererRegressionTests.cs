using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantics;
using Kong.Semantics.Symbols;

namespace Kong.Tests.Semantics;

public class TypeInfererRegressionTests
{
    [Fact]
    public void InferTypes_ResolvesForwardTopLevelFunctionReturnTypes()
    {
        var program = Parse("let a = fn() { b() }; let b = fn() { true }; puts(a());");
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        Assert.True(types.TryGetFunctionSignature("a", out var signature));
        Assert.Equal(TypeSymbol.Bool, signature.ReturnType);
    }

    [Fact]
    public void InferTypes_ReportsTypeMismatchForFunctionLiteralCallee()
    {
        var program = Parse("let value = (fn(x: int) { x })(true);");
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        Assert.Contains(types.GetErrors(), e => e.Contains("Type error: argument 1"));
    }

    [Fact]
    public void InferTypes_ReportsInvalidTopLevelParameterAnnotations()
    {
        var program = Parse("let identity = fn(x: banana) { x }; identity(1);");
        var inferer = new TypeInferer();

        var types = inferer.InferTypes(program);

        Assert.Contains(types.GetErrors(), e => e.Contains("invalid type annotation for parameter 'x': unknown type 'banana'"));
    }

    private static Program Parse(string source)
    {
        var parser = new Parser(new Lexer(source));
        var program = parser.ParseProgram();
        Assert.Empty(parser.Errors());
        return program;
    }
}
