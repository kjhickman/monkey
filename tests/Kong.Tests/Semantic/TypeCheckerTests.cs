using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class TypeCheckerTests
{
    [Fact]
    public void TestTypedLetAndIdentifierUsage()
    {
        var (unit, names, result) = ParseResolveAndCheck("let x: int = 6; let y: int = x; y;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var yStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var yIdentifier = Assert.IsType<Identifier>(yStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(yIdentifier, out var yType));
        Assert.Equal(TypeSymbols.Int, yType);
    }

    [Fact]
    public void TestTypedFunctionCall()
    {
        var input = "fn(x: int, y: int) -> int { return x + y; }(1, 2);";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestInfersLetTypeFromInitializer()
    {
        var (unit, _, result) = ParseResolveAndCheck("let x = 6; x;");

        Assert.False(result.Diagnostics.HasErrors);

        var xLet = Assert.IsType<LetStatement>(unit.Statements[0]);
        Assert.True(result.VariableTypes.TryGetValue(xLet, out var xType));
        Assert.Equal(TypeSymbols.Int, xType);

        var xUse = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var xIdentifier = Assert.IsType<Identifier>(xUse.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(xIdentifier, out var xUseType));
        Assert.Equal(TypeSymbols.Int, xUseType);
    }

    [Fact]
    public void TestCannotInferLetTypeFromNullInitializer()
    {
        var (_, _, result) = ParseResolveAndCheck("let x = if (true) { 1 }; ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T119");
    }

    [Fact]
    public void TestCannotInferLetTypeFromEmptyArrayInitializer()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs = []; ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T120");
    }

    [Fact]
    public void TestMissingFunctionParameterTypeAnnotationReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn(x) -> int { return x; };");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T105");
    }

    [Fact]
    public void TestReturnTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn() -> int { return true; };");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T104");
    }

    [Fact]
    public void TestInfixTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = 1 + true;");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T103");
    }

    [Fact]
    public void TestCallArgumentTypeMismatchReportsDiagnostic()
    {
        var input = "fn(x: int) -> int { return x; }(true);";
        var (_, _, result) = ParseResolveAndCheck(input);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
    }

    [Fact]
    public void TestArrayElementMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs: int[] = [1, true];");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T114");
    }

    [Fact]
    public void TestIndexExpressionType()
    {
        var (unit, _, result) = ParseResolveAndCheck("let xs: int[] = [1, 2, 3]; xs[0];");

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.Int, type);
    }

    [Fact]
    public void TestIncludesNameResolutionDiagnostics()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = y;");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N001");
    }

    [Fact]
    public void TestNamedFunctionDeclarationWithImplicitVoidReturnType()
    {
        var (_, names, result) = ParseResolveAndCheck("fn Main() { } let x: int = 1; x;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T106");
    }

    [Fact]
    public void TestForwardReferenceAcrossNamedFunctionDeclarations()
    {
        var input = "fn A() -> int { B(); } fn B() -> int { 1; } A();";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCall()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.Abs(-42);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Int, callType);
    }

    [Fact]
    public void TestReportsUnsupportedStaticClrMethodReturnType()
    {
        var (_, _, result) = ParseResolveAndCheck("System.Guid.NewGuid();");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T122");
    }

    private static (CompilationUnit Unit, NameResolution Names, TypeCheckResult Result) ParseResolveAndCheck(string input)
    {
        var lexer = new Lexer(input);
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();

        if (parser.Diagnostics.HasErrors)
        {
            var message = $"parser has {parser.Diagnostics.Count} errors\n";
            foreach (var diagnostic in parser.Diagnostics.All)
            {
                message += $"parser error: \"{diagnostic.Message}\"\n";
            }
            Assert.Fail(message);
        }

        var resolver = new NameResolver();
        var names = resolver.Resolve(unit);

        var checker = new TypeChecker();
        var result = checker.Check(unit, names);

        return (unit, names, result);
    }
}
