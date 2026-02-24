using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class NameResolverTests
{
    [Fact]
    public void TestResolvesIdentifiersAndStaticClrCall()
    {
        var unit = Parse("namespace Test let x = 1 let y = x y System.Math.Abs(x)");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letY = Assert.IsType<LetStatement>(unit.Statements[2]);
        var xInLet = Assert.IsType<Identifier>(letY.Value);
        Assert.True(result.IdentifierSymbols.TryGetValue(xInLet, out var xSymbol));
        Assert.Equal(NameSymbolKind.Global, xSymbol.Kind);
        Assert.Equal("x", xSymbol.Name);

        var yExpression = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var yIdentifier = Assert.IsType<Identifier>(yExpression.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(yIdentifier, out var ySymbol));
        Assert.Equal(NameSymbolKind.Global, ySymbol.Kind);
        Assert.Equal("y", ySymbol.Name);

        var callExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[4]);
        var callExpression = Assert.IsType<CallExpression>(callExpressionStatement.Expression);
        var methodAccess = Assert.IsType<MemberAccessExpression>(callExpression.Function);
        var typeAccess = Assert.IsType<MemberAccessExpression>(methodAccess.Object);
        var systemIdentifier = Assert.IsType<Identifier>(typeAccess.Object);
        Assert.False(result.IdentifierSymbols.ContainsKey(systemIdentifier));
    }

    [Fact]
    public void TestReportsDuplicateLetDeclaration()
    {
        var unit = Parse("namespace Test let x = 1 let x = 2");

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
        var unit = Parse("namespace Test fn(x, x) { x }");

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
        var unit = Parse("namespace Test let x = y");

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
        var unit = Parse("namespace Test let x = 1 if (true) { let x = 2 x } x");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var ifExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var ifExpression = Assert.IsType<IfExpression>(ifExpressionStatement.Expression);
        var innerExpressionStatement = Assert.IsType<ExpressionStatement>(ifExpression.Consequence.Statements[1]);
        var innerXIdentifier = Assert.IsType<Identifier>(innerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(innerXIdentifier, out var innerSymbol));
        Assert.Equal(NameSymbolKind.Local, innerSymbol.Kind);

        var outerExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var outerXIdentifier = Assert.IsType<Identifier>(outerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(outerXIdentifier, out var outerSymbol));
        Assert.Equal(NameSymbolKind.Global, outerSymbol.Kind);

        Assert.NotEqual(innerSymbol.DeclarationSpan, outerSymbol.DeclarationSpan);
    }

    [Fact]
    public void TestCapturesOuterFunctionVariable()
    {
        var unit = Parse("namespace Test let f = fn(x) { let g = fn() { x } g }");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var outerLet = Assert.IsType<LetStatement>(unit.Statements[1]);
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
        var unit = Parse("namespace Test let x: int = 1 let y: int = x y");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letY = Assert.IsType<LetStatement>(unit.Statements[2]);
        var xInLet = Assert.IsType<Identifier>(letY.Value);
        Assert.True(result.IdentifierSymbols.TryGetValue(xInLet, out var xSymbol));
        Assert.Equal(NameSymbolKind.Global, xSymbol.Kind);
        Assert.Equal("x", xSymbol.Name);

        var yExpression = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var yIdentifier = Assert.IsType<Identifier>(yExpression.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(yIdentifier, out var ySymbol));
        Assert.Equal(NameSymbolKind.Global, ySymbol.Kind);
        Assert.Equal("y", ySymbol.Name);
    }

    [Fact]
    public void TestResolvesTypedFunctionParameter()
    {
        var unit = Parse("namespace Test let f = fn(x: int) -> int { x } f(1)");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var letF = Assert.IsType<LetStatement>(unit.Statements[1]);
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
        var unit = Parse("namespace Test fn A() -> int { B() } fn B() -> int { 1 } A()");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var declarationA = Assert.IsType<FunctionDeclaration>(unit.Statements[1]);
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
        var unit = Parse("namespace Test let f = fn(x: int) -> int { let g = fn() -> int { x } g() }");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var outerLet = Assert.IsType<LetStatement>(unit.Statements[1]);
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
        var unit = Parse("namespace Test let x: int = 1 if (true) { let x: int = 2 x } x");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);

        var ifExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var ifExpression = Assert.IsType<IfExpression>(ifExpressionStatement.Expression);
        var innerExpressionStatement = Assert.IsType<ExpressionStatement>(ifExpression.Consequence.Statements[1]);
        var innerXIdentifier = Assert.IsType<Identifier>(innerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(innerXIdentifier, out var innerSymbol));
        Assert.Equal(NameSymbolKind.Local, innerSymbol.Kind);

        var outerExpressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var outerXIdentifier = Assert.IsType<Identifier>(outerExpressionStatement.Expression);
        Assert.True(result.IdentifierSymbols.TryGetValue(outerXIdentifier, out var outerSymbol));
        Assert.Equal(NameSymbolKind.Global, outerSymbol.Kind);

        Assert.NotEqual(innerSymbol.DeclarationSpan, outerSymbol.DeclarationSpan);
    }

    [Fact]
    public void TestDoesNotReportUndefinedVariableForStaticMethodPath()
    {
        var unit = Parse("namespace Test System.Console.WriteLine(1)");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestResolvesTopLevelImportAlias()
    {
        var unit = Parse("import System import System.Console import System.Math namespace Test");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains("System", result.ImportedNamespaces);
        Assert.Contains("System.Console", result.ImportedNamespaces);
        Assert.Contains("System.Math", result.ImportedNamespaces);
        Assert.Equal("System.Console", result.ImportedTypeAliases["Console"]);
        Assert.Equal("System.Math", result.ImportedTypeAliases["Math"]);
        Assert.Equal("System", result.ImportedTypeAliases["System"]);
    }

    [Fact]
    public void TestReportsImportAliasConflict()
    {
        var unit = Parse("import System.Console import Foo.Console namespace Test");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N004");
    }

    [Fact]
    public void TestReportsImportInsideFunctionBody()
    {
        var unit = Parse("namespace Test fn Main() { import System.Console }");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N003");
    }

    [Fact]
    public void TestReportsImportAfterTopLevelFunctionDeclaration()
    {
        var unit = Parse("namespace Test fn Main() { } import System");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N005");
    }

    [Fact]
    public void TestReportsMissingRequiredFileScopedNamespace()
    {
        var lexer = new Lexer("let x = 1");
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N006");
    }

    [Fact]
    public void TestResolvesClassInterfaceAndImplBodies()
    {
        var unit = Parse("namespace Test class User { name: string } interface IGreeter { fn Greet(self) } impl User { init(name: string) { self.name = name } fn Greet(self) { self.name } } impl IGreeter for User { fn Greet(self) { self.name } }");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains("User", result.DeclaredClasses);
        Assert.Contains("IGreeter", result.DeclaredInterfaces);
    }

    [Fact]
    public void TestReportsImplTargetTypeNotDeclared()
    {
        var unit = Parse("namespace Test impl Missing { fn A(self) { } }");

        var resolver = new NameResolver();
        var result = resolver.Resolve(unit);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N011");
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
