namespace Kong.Tests.Integration;

public class CharTests
{
    [Theory]
    [InlineData("puts('a');", "a")]
    [InlineData("puts('z');", "z")]
    [InlineData("puts('A');", "A")]
    [InlineData("puts('0');", "0")]
    public async Task TestCharLiteralPuts(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let c = 'a'; puts(c);", "a")]
    [InlineData("let c = 'Z'; puts(c);", "Z")]
    public async Task TestCharLetBinding(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("'a' == 'a'", true)]
    [InlineData("'a' == 'b'", false)]
    [InlineData("'a' != 'b'", true)]
    [InlineData("'a' < 'b'", true)]
    [InlineData("'b' > 'a'", true)]
    public async Task TestCharComparisons(string source, bool expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }
}
