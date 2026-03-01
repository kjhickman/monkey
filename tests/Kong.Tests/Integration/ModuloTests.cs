namespace Kong.Tests.Integration;

public class ModuloTests
{
    [Theory]
    [InlineData("puts(10 % 3);", "1")]
    [InlineData("puts(7 % 2);", "1")]
    [InlineData("puts(9 % 3);", "0")]
    [InlineData("puts(0 % 5);", "0")]
    public async Task TestIntModulo(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(5.5 % 2.0);", "1.5")]
    [InlineData("puts(9.0 % 4.0);", "1")]
    public async Task TestDoubleModulo(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Fact]
    public async Task TestModuloLetBinding()
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr("let r = 17 % 5; puts(r);");
        Assert.Equal("2", clrOutput);
    }

    [Fact]
    public async Task TestModuloInFunction()
    {
        var source = "let is_even = fn(n: int) { n % 2 == 0 }; puts(is_even(4)); puts(is_even(7));";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal($"True{Environment.NewLine}False", clrOutput);
    }
}
