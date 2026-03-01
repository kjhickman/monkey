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
        var types = AnalyzeAndGetTypes(program);

        Assert.True(types.TryGetFunctionSignature("a", out var signature));
        Assert.Equal(TypeSymbol.Bool, signature.ReturnType);
    }

    [Fact]
    public void InferTypes_ReportsTypeMismatchForFunctionLiteralCallee()
    {
        var program = Parse("let value = (fn(x: int) { x })(true);");
        var types = AnalyzeAndGetTypes(program);

        Assert.Contains(types.GetErrors(), e => e.Message.Contains("Type error: argument 1"));
    }

    [Fact]
    public void InferTypes_ReportsInvalidTopLevelParameterAnnotations()
    {
        var program = Parse("let identity = fn(x: banana) { x }; identity(1);");
        var analyzer = new SemanticAnalyzer();

        var result = analyzer.Analyze(program);

        Assert.Contains(result.Errors, e => e.Message.Contains("invalid type annotation for parameter 'x': unknown type 'banana'"));
    }

    [Fact]
    public void ValidateFunctionTypeAnnotations_ReportsCheckerErrors()
    {
        var program = Parse("puts(len(1));");
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);

        Assert.Contains(result.Errors, e => e.Message.Contains("argument to `len` not supported"));
    }

    [Fact]
    public void Analyze_ReportsHashMapKeyTypeMismatch()
    {
        var program = Parse("let get = fn(h: map[string]int) { h[1] }; puts(get({\"answer\": 42}));");
        var analyzer = new SemanticAnalyzer();

        var result = analyzer.Analyze(program);

        Assert.Contains(result.Errors, e => e.Message.Contains("hash map index must be string"));
    }

    private static Program Parse(string source)
    {
        var parser = new Parser(new Lexer(source));
        var program = parser.ParseProgram();
        Assert.Empty(parser.Errors());
        return program;
    }

    private static SemanticModel AnalyzeAndGetTypes(Program program)
    {
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);
        Assert.NotNull(result.Types);
        return result.Types!;
    }

    [Fact]
    public void InferTypes_CharLiteralHasCharType()
    {
        var program = Parse("let c = 'a';");
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InferTypes_CharParameterAnnotationIsRecognized()
    {
        var program = Parse("let identity = fn(c: char) { c }; identity('z');");
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InferTypes_DoubleLiteralHasDoubleType()
    {
        var program = Parse("let x = 3.14;");
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InferTypes_DoubleParameterAnnotationIsRecognized()
    {
        var program = Parse("let identity = fn(x: double) { x }; identity(1.0);");
        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void InferTypes_DoubleArithmeticResultIsDouble()
    {
        var program = Parse("let add = fn() { 1.5 + 2.5 };");
        var types = AnalyzeAndGetTypes(program);

        Assert.True(types.TryGetFunctionSignature("add", out var sig));
        Assert.Equal(TypeSymbol.Double, sig.ReturnType);
    }
}
