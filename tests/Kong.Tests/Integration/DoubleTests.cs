namespace Kong.Tests.Integration;

public class DoubleTests
{
    [Theory]
    [InlineData("puts(3.14);", "3.14")]
    [InlineData("puts(1.0);", "1")]
    [InlineData("puts(0.5);", "0.5")]
    public async Task TestDoubleLiteralPuts(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let x = 3.14; puts(x);", "3.14")]
    [InlineData("let x = 0.5; puts(x);", "0.5")]
    public async Task TestDoubleLetBinding(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(1.5 + 2.5);", "4")]
    [InlineData("puts(5.0 - 1.5);", "3.5")]
    [InlineData("puts(2.0 * 3.0);", "6")]
    [InlineData("puts(7.0 / 2.0);", "3.5")]
    public async Task TestDoubleArithmetic(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("1.0 == 1.0", "True")]
    [InlineData("1.0 == 2.0", "False")]
    [InlineData("1.0 != 2.0", "True")]
    [InlineData("1.0 < 2.0", "True")]
    [InlineData("2.0 > 1.0", "True")]
    public async Task TestDoubleComparisons(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Fact]
    public async Task TestDoubleParameter()
    {
        var source = "let double_it = fn(x: double) { x * 2.0 }; puts(double_it(3.5));";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("7", clrOutput);
    }
}
