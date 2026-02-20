namespace Kong.Tests;

public class DiagnosticTests
{
    [Fact]
    public void TestDiagnosticBagReport()
    {
        var bag = new DiagnosticBag();

        Assert.Equal(0, bag.Count);
        Assert.False(bag.HasErrors);

        bag.Report(Span.Empty, "test error", "T001");

        Assert.Equal(1, bag.Count);
        Assert.True(bag.HasErrors);
        Assert.Equal("test error", bag.All[0].Message);
        Assert.Equal("T001", bag.All[0].Code);
        Assert.Equal(Severity.Error, bag.All[0].Severity);
        Assert.Equal(Span.Empty, bag.All[0].Span);
    }

    [Fact]
    public void TestDiagnosticBagWarningDoesNotCountAsError()
    {
        var bag = new DiagnosticBag();
        bag.Report(Span.Empty, "just a warning", "T002", Severity.Warning);

        Assert.Equal(1, bag.Count);
        Assert.False(bag.HasErrors);
    }

    [Fact]
    public void TestDiagnosticBagAddRange()
    {
        var bag1 = new DiagnosticBag();
        bag1.Report(Span.Empty, "error one", "T001");

        var bag2 = new DiagnosticBag();
        bag2.Report(Span.Empty, "error two", "T002");

        bag1.AddRange(bag2);

        Assert.Equal(2, bag1.Count);
        Assert.Equal("error one", bag1.All[0].Message);
        Assert.Equal("error two", bag1.All[1].Message);
    }

    [Fact]
    public void TestDiagnosticToStringWithSpan()
    {
        var span = new Span(new Position(1, 5), new Position(1, 10));
        var diag = new Diagnostic(span, Severity.Error, "bad token", "P001");

        var str = diag.ToString();
        Assert.Contains("bad token", str);
        Assert.Contains("P001", str);
        Assert.Contains("line 1, column 5", str);
    }

    [Fact]
    public void TestDiagnosticToStringWithEmptySpan()
    {
        var diag = new Diagnostic(Span.Empty, Severity.Error, "stack overflow", "R004");

        var str = diag.ToString();
        Assert.Contains("stack overflow", str);
        Assert.Contains("R004", str);
        Assert.DoesNotContain("at", str);
    }

    [Fact]
    public void TestParserProducesDiagnosticsOnError()
    {
        var l = new Lexer("let = 5;");
        var p = new Parser(l);
        p.ParseCompilationUnit();

        Assert.True(p.Diagnostics.HasErrors);
        var diag = p.Diagnostics.All[0];
        Assert.Equal("P001", diag.Code);
        Assert.NotEqual(Span.Empty, diag.Span);
    }

    [Fact]
    public void TestParserNoPrefixParseFnDiagnostic()
    {
        var l = new Lexer("=;");
        var p = new Parser(l);
        p.ParseCompilationUnit();

        Assert.True(p.Diagnostics.HasErrors);
        // Should have a P002 diagnostic for no prefix parse function
        var hasP002 = false;
        foreach (var d in p.Diagnostics.All)
        {
            if (d.Code == "P002") hasP002 = true;
        }
        Assert.True(hasP002, "expected P002 diagnostic for no prefix parse function");
    }

}
