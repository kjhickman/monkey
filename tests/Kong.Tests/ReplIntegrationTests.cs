namespace Kong.Tests;

public class ReplIntegrationTests
{
    [Fact]
    public void TestReplReportsInferenceDiagnosticAndContinues()
    {
        var input = new StringReader("let x = if (true) { 1 };\n1 + 1;\n");
        var output = new StringWriter();

        Repl.Start(input, output);

        var text = output.ToString();
        Assert.Contains("[T119]", text);
        Assert.Contains("2", text);
    }

    [Fact]
    public void TestReplFailsFastOnSemanticDiagnosticsBeforeCompiler()
    {
        var input = new StringReader("foobar;\n");
        var output = new StringWriter();

        Repl.Start(input, output);

        var text = output.ToString();
        Assert.Contains("[N001]", text);
        Assert.DoesNotContain("[C001]", text);
    }
}
