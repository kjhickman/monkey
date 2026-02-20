using Kong.Cli;

namespace Kong.Tests;

public class ClrPhase1ExecutorTests
{
    [Fact]
    public void TestExecutesSimpleAdditionExpression()
    {
        var unit = Parse("1 + 1;");
        var executor = new ClrPhase1Executor();

        var result = executor.Execute(unit);

        Assert.True(result.Executed);
        Assert.Equal(2, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestExecutesNestedArithmeticExpression()
    {
        var unit = Parse("(1 + 2) * 3;");
        var executor = new ClrPhase1Executor();

        var result = executor.Execute(unit);

        Assert.True(result.Executed);
        Assert.Equal(9, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestExecutesProgramWithLetBinding()
    {
        var unit = Parse("let x = 2; x + 3;");
        var executor = new ClrPhase1Executor();

        var result = executor.Execute(unit);

        Assert.True(result.Executed);
        Assert.Equal(5, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReturnsSemanticDiagnosticsWhenTypeCheckFails()
    {
        var unit = Parse("x;");
        var executor = new ClrPhase1Executor();

        var result = executor.Execute(unit);

        Assert.False(result.Executed);
        Assert.False(result.IsUnsupported);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N001");
    }

    [Fact]
    public void TestReturnsUnsupportedForNonIntTopLevelResult()
    {
        var unit = Parse("true;");
        var executor = new ClrPhase1Executor();

        var result = executor.Execute(unit);

        Assert.False(result.Executed);
        Assert.True(result.IsUnsupported);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "IR001");
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
