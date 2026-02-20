using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class ControlFlowAnalyzerTests
{
    [Fact]
    public void TestFunctionWithExplicitReturnHasNoControlFlowDiagnostics()
    {
        var (_, result) = ParseResolveAndCheck("fn() -> int { return 1; };");

        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code is "T117" or "T118");
    }

    [Fact]
    public void TestFunctionWithIfElseReturningHasNoMissingReturnDiagnostic()
    {
        var (_, result) = ParseResolveAndCheck("fn(x: bool) -> int { if (x) { return 1; } else { return 2; } };");

        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T117");
    }

    [Fact]
    public void TestFunctionMissingReturnPathReportsDiagnostic()
    {
        var (_, result) = ParseResolveAndCheck("fn(x: bool) -> int { if (x) { return 1; } };");

        Assert.Contains(result.Diagnostics.All, d => d.Code == "T117");
    }

    [Fact]
    public void TestEmptyNonVoidFunctionReportsMissingReturnDiagnostic()
    {
        var (_, result) = ParseResolveAndCheck("fn() -> int { };");

        Assert.Contains(result.Diagnostics.All, d => d.Code == "T117");
    }

    [Fact]
    public void TestTailExpressionSatisfiesReturnPath()
    {
        var (_, result) = ParseResolveAndCheck("fn() -> int { 42; };");

        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T117");
    }

    [Fact]
    public void TestReportsUnreachableCodeAfterReturn()
    {
        var (_, result) = ParseResolveAndCheck("fn() -> int { return 1; 2; };");

        var diagnostic = Assert.Single(result.Diagnostics.All, d => d.Code == "T118");
        Assert.Equal(Severity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void TestReportsUnreachableCodeAfterIfElseBothReturn()
    {
        var (_, result) = ParseResolveAndCheck("fn(x: bool) -> int { if (x) { return 1; } else { return 2; } 3; };");

        Assert.Contains(result.Diagnostics.All, d => d.Code == "T118");
    }

    [Fact]
    public void TestCodeAfterPartialReturnPathRemainsReachable()
    {
        var (_, result) = ParseResolveAndCheck("fn(x: bool) -> int { if (x) { return 1; } 2; };");

        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T118");
        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T117");
    }

    private static (CompilationUnit Unit, TypeCheckResult Result) ParseResolveAndCheck(string input)
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

        return (unit, result);
    }
}
