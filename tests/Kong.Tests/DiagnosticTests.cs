using Kong.Diagnostics;
using Kong.Token;

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
        var l = new Lexer.Lexer("let = 5;");
        var p = new Parser.Parser(l);
        p.ParseProgram();

        Assert.True(p.Diagnostics.HasErrors);
        var diag = p.Diagnostics.All[0];
        Assert.Equal("P001", diag.Code);
        Assert.NotEqual(Span.Empty, diag.Span);
    }

    [Fact]
    public void TestParserNoPrefixParseFnDiagnostic()
    {
        var l = new Lexer.Lexer("=;");
        var p = new Parser.Parser(l);
        p.ParseProgram();

        Assert.True(p.Diagnostics.HasErrors);
        // Should have a P002 diagnostic for no prefix parse function
        var hasP002 = false;
        foreach (var d in p.Diagnostics.All)
        {
            if (d.Code == "P002") hasP002 = true;
        }
        Assert.True(hasP002, "expected P002 diagnostic for no prefix parse function");
    }

    [Fact]
    public void TestCompilerProducesDiagnosticsOnUndefinedVariable()
    {
        var l = new Lexer.Lexer("x;");
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();

        var compiler = new Compiler.Compiler();
        compiler.Compile(program);

        Assert.True(compiler.Diagnostics.HasErrors);
        var diag = compiler.Diagnostics.All[0];
        Assert.Equal("C001", diag.Code);
        Assert.Contains("x", diag.Message);
    }

    [Fact]
    public void TestVmProducesDiagnosticsOnWrongArgCount()
    {
        var l = new Lexer.Lexer("fn() { 1; }(1);");
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();

        var compiler = new Compiler.Compiler();
        compiler.Compile(program);
        Assert.False(compiler.Diagnostics.HasErrors);

        var vm = new Vm.Vm(compiler.GetBytecode());
        vm.Run();

        Assert.True(vm.Diagnostics.HasErrors);
        var diag = vm.Diagnostics.All[0];
        Assert.Equal("R002", diag.Code);
        Assert.Contains("wrong number of arguments", diag.Message);
        Assert.Equal(Span.Empty, diag.Span);
    }
}
