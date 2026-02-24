using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Tests;

namespace Kong.Tests.Semantic;

public class ProgramValidatorTests
{
    [Fact]
    public void TestReportsTopLevelStatementAndMissingMain()
    {
        var (unit, typeCheck) = ParseResolveAndCheck("let x = 1");

        var diagnostics = ProgramValidator.ValidateEntrypoint(unit, typeCheck);

        Assert.Contains(diagnostics.All, d => d.Code == "CLI002");
        Assert.Contains(diagnostics.All, d => d.Code == "CLI003");
    }

    [Fact]
    public void TestReportsDuplicateMainFunctions()
    {
        var (unit, typeCheck) = ParseResolveAndCheck("fn Main() { } fn Main() { }");

        var diagnostics = ProgramValidator.ValidateEntrypoint(unit, typeCheck);

        Assert.Contains(diagnostics.All, d => d.Code == "CLI004");
    }

    [Fact]
    public void TestAllowsMainWithVoidOrIntReturn()
    {
        var (unitVoid, typeCheckVoid) = ParseResolveAndCheck("fn Main() { }");
        var (unitInt, typeCheckInt) = ParseResolveAndCheck("fn Main() -> int { 0 }");

        var diagnosticsVoid = ProgramValidator.ValidateEntrypoint(unitVoid, typeCheckVoid);
        var diagnosticsInt = ProgramValidator.ValidateEntrypoint(unitInt, typeCheckInt);

        Assert.False(diagnosticsVoid.HasErrors);
        Assert.False(diagnosticsInt.HasErrors);
    }

    [Fact]
    public void TestRejectsMainWithParameters()
    {
        var (unit, typeCheck) = ParseResolveAndCheck("fn Main(x: int) { }");

        var diagnostics = ProgramValidator.ValidateEntrypoint(unit, typeCheck);

        Assert.Contains(diagnostics.All, d => d.Code == "CLI005");
    }

    [Fact]
    public void TestAllowsTopLevelImports()
    {
        var (unit, typeCheck) = ParseResolveAndCheck("use System.Console fn Main() { Console.WriteLine(1) }");

        var diagnostics = ProgramValidator.ValidateEntrypoint(unit, typeCheck);

        Assert.False(diagnostics.HasErrors);
    }

    private static (CompilationUnit Unit, TypeCheckResult TypeCheck) ParseResolveAndCheck(string input)
    {
        input = TestSourceUtilities.EnsureFileScopedNamespace(input);
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
        var typeCheck = checker.Check(unit, names);

        return (unit, typeCheck);
    }

}
