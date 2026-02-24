using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Tests;

namespace Kong.Tests.Integration;

public class SemanticGoldenTests
{
    [Theory]
    [InlineData("let y: int = z", "N001")]
    [InlineData("fn(x) -> int { return x }", "T105")]
    [InlineData("fn() -> int { if (true) { return 1 } }", "T117")]
    [InlineData("fn() -> int { return 1 2 }", "T118")]
    [InlineData("let x = if (true) { 1 }", "T119")]
    [InlineData("let xs = []", "T120")]
    public void TestSemanticDiagnosticsGoldenCodes(string input, string expectedCode)
    {
        var result = ParseResolveAndCheck(input);

        Assert.Contains(result.Diagnostics.All, d => d.Code == expectedCode);
    }

    [Fact]
    public void TestValidTypedProgramHasNoSemanticErrors()
    {
        var result = ParseResolveAndCheck("let x = 1 let y: int = x + 2 y");

        Assert.False(result.Diagnostics.HasErrors);
    }

    private static TypeCheckResult ParseResolveAndCheck(string input)
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
        return checker.Check(unit, names);
    }

}
