using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class NameResolverTests
{
    [Fact]
    public void TestResolvesIdentifiersAndBuiltins()
    {
        var unit = Parse("let x = 1; let y = x; y; len([x, y]);");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letY = Assert.IsType<LetStatement>(unit.Statements[1]);
        var xInLet = Assert.IsType<Identifier>(letY.Value);
        Assert.True(result.IdentifierSymbols.TryGetValue(xInLet, out var xSymbol));
        Assert.Equal(NameSymbolKind.Global, xSymbol.Kind);
        Assert.Equal("x", xSymbol.Name);

        var yExpression = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var yIdentifier = Assert.IsType<Identifier>(yExpression.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(yIdentifier, out var ySymbol));
        Assert.Equal(NameSymbolKind.Global, ySymbol.Kind);
        Assert.Equal("y", ySymbol.Name);

        var callExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var callExpression = Assert.IsType<CallExpression>(callExpressionStatement.Expression);
        var lenIdentifier = Assert.IsType<Identifier>(callExpression.Function);
        Assert.True(result.IdentifierSymbols.TryGetValue(lenIdentifier, out var lenSymbol));
        Assert.Equal(NameSymbolKind.Builtin, lenSymbol.Kind);
        Assert.Equal("len", lenSymbol.Name);
    }

    [Fact]
    public void TestReportsDuplicateLetDeclaration()
    {
        var unit = Parse("let x = 1; let x = 2;");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Single(result.Diagnostics.All);
        Assert.Equal("N002", result.Diagnostics.All[0].Code);
        Assert.Equal("duplicate declaration of 'x'", result.Diagnostics.All[0].Message);
    }

    [Fact]
    public void TestReportsDuplicateParameterDeclaration()
    {
        var unit = Parse("fn(x, x) { x; };");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Single(result.Diagnostics.All);
        Assert.Equal("N002", result.Diagnostics.All[0].Code);
        Assert.Equal("duplicate declaration of 'x'", result.Diagnostics.All[0].Message);
    }

    [Fact]
    public void TestReportsUndefinedVariable()
    {
        var unit = Parse("let x = y;");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Single(result.Diagnostics.All);
        Assert.Equal("N001", result.Diagnostics.All[0].Code);
        Assert.Equal("undefined variable 'y'", result.Diagnostics.All[0].Message);
    }

    [Fact]
    public void TestUsesBlockScopeForDeclarations()
    {
        var unit = Parse("let x = 1; if (true) { let x = 2; x; }; x;");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var ifExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var ifExpression = Assert.IsType<IfExpression>(ifExpressionStatement.Expression);
        var innerExpressionStatement = Assert.IsType<ExpressionStatement>(ifExpression.Consequence.Statements[1]);
        var innerXIdentifier = Assert.IsType<Identifier>(innerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(innerXIdentifier, out var innerSymbol));
        Assert.Equal(NameSymbolKind.Local, innerSymbol.Kind);

        var outerExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var outerXIdentifier = Assert.IsType<Identifier>(outerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(outerXIdentifier, out var outerSymbol));
        Assert.Equal(NameSymbolKind.Global, outerSymbol.Kind);

        Assert.NotEqual(innerSymbol.DeclarationSpan, outerSymbol.DeclarationSpan);
    }

    [Fact]
    public void TestCapturesOuterFunctionVariable()
    {
        var unit = Parse("let f = fn(x) { let g = fn() { x; }; g; };");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var outerLet = Assert.IsType<LetStatement>(unit.Statements[0]);
        var outerFunction = Assert.IsType<FunctionLiteral>(outerLet.Value);
        var innerLet = Assert.IsType<LetStatement>(outerFunction.Body.Statements[0]);
        var innerFunction = Assert.IsType<FunctionLiteral>(innerLet.Value);

        var captures = result.GetCapturedSymbols(innerFunction);
        Assert.Single(captures);
        Assert.Equal("x", captures[0].Name);
        Assert.Equal(NameSymbolKind.Parameter, captures[0].Kind);
    }

    [Fact]
    public void TestResolvesIdentifiersWithTypeAnnotations()
    {
        var unit = Parse("let x: int = 1; let y: int = x; y;");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letY = Assert.IsType<LetStatement>(unit.Statements[1]);
        var xInLet = Assert.IsType<Identifier>(letY.Value);
        Assert.True(result.IdentifierSymbols.TryGetValue(xInLet, out var xSymbol));
        Assert.Equal(NameSymbolKind.Global, xSymbol.Kind);
        Assert.Equal("x", xSymbol.Name);

        var yExpression = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var yIdentifier = Assert.IsType<Identifier>(yExpression.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(yIdentifier, out var ySymbol));
        Assert.Equal(NameSymbolKind.Global, ySymbol.Kind);
        Assert.Equal("y", ySymbol.Name);
    }

    [Fact]
    public void TestResolvesTypedFunctionParameter()
    {
        var unit = Parse("let f = fn(x: int) -> int { x; }; f(1);");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letF = Assert.IsType<LetStatement>(unit.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(letF.Value);
        var bodyExpr = Assert.IsType<ExpressionStatement>(function.Body.Statements[0]);
        var xIdentifier = Assert.IsType<Identifier>(bodyExpr.Expression);

        Assert.True(result.IdentifierSymbols.TryGetValue(xIdentifier, out var xSymbol));
        Assert.Equal(NameSymbolKind.Parameter, xSymbol.Kind);
        Assert.Equal("x", xSymbol.Name);
    }

    [Fact]
    public void TestPredeclaresTopLevelNamedFunctionsForForwardReferences()
    {
        var unit = Parse("fn A() -> int { B(); } fn B() -> int { 1; } A();");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var declarationA = Assert.IsType<FunctionDeclaration>(unit.Statements[0]);
        var callInAStmt = Assert.IsType<ExpressionStatement>(declarationA.Body.Statements[0]);
        var callInA = Assert.IsType<CallExpression>(callInAStmt.Expression);
        var bIdentifier = Assert.IsType<Identifier>(callInA.Function);

        Assert.True(result.IdentifierSymbols.TryGetValue(bIdentifier, out var bSymbol));
        Assert.Equal(NameSymbolKind.Global, bSymbol.Kind);
        Assert.Equal("B", bSymbol.Name);
    }

    [Fact]
    public void TestCapturesOuterTypedFunctionParameter()
    {
        var unit = Parse("let f = fn(x: int) -> int { let g = fn() -> int { x; }; g(); };");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var outerLet = Assert.IsType<LetStatement>(unit.Statements[0]);
        var outerFunction = Assert.IsType<FunctionLiteral>(outerLet.Value);
        var innerLet = Assert.IsType<LetStatement>(outerFunction.Body.Statements[0]);
        var innerFunction = Assert.IsType<FunctionLiteral>(innerLet.Value);

        var captures = result.GetCapturedSymbols(innerFunction);
        Assert.Single(captures);
        Assert.Equal("x", captures[0].Name);
        Assert.Equal(NameSymbolKind.Parameter, captures[0].Kind);
    }

    [Fact]
    public void TestUsesBlockScopeWithTypeAnnotations()
    {
        var unit = Parse("let x: int = 1; if (true) { let x: int = 2; x; }; x;");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var ifExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var ifExpression = Assert.IsType<IfExpression>(ifExpressionStatement.Expression);
        var innerExpressionStatement = Assert.IsType<ExpressionStatement>(ifExpression.Consequence.Statements[1]);
        var innerXIdentifier = Assert.IsType<Identifier>(innerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(innerXIdentifier, out var innerSymbol));
        Assert.Equal(NameSymbolKind.Local, innerSymbol.Kind);

        var outerExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var outerXIdentifier = Assert.IsType<Identifier>(outerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(outerXIdentifier, out var outerSymbol));
        Assert.Equal(NameSymbolKind.Global, outerSymbol.Kind);

        Assert.NotEqual(innerSymbol.DeclarationSpan, outerSymbol.DeclarationSpan);
    }

    [Fact]
    public void TestDoesNotReportUndefinedVariableForStaticMethodPath()
    {
        var unit = Parse("System.Console.WriteLine(1);");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);
    }

    private static CompilationUnit Parse(string input)
    {
        var lexer = new Lexer(input);
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();

        if (!parser.Diagnostics.HasErrors)
        {
            return unit;
        }

        var message = $"parser has {parser.Diagnostics.Count} errors\n";
        foreach (var diagnostic in parser.Diagnostics.All)
        {
            message += $"parser error: \"{diagnostic.Message}\"\n";
        }

        Assert.Fail(message);
        return null!;
    }
}
