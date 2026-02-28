namespace Kong.Tests.Integration;

public class CollectionTests
{
    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("[1, 2, 3]", "[1, 2, 3]")]
    [InlineData("[1 + 2, 3 * 4, 5 + 6]", "[3, 12, 11]")]
    public async Task TestArrayLiterals(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{1: 2, 2: 3}", "{1: 2, 2: 3}")]
    [InlineData("{1 + 1: 2 * 2, 3 + 3: 4 * 4}", "{2: 4, 6: 16}")]
    public async Task TestHashLiterals(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("[1, 2, 3][1]", "2")]
    [InlineData("[1, 2, 3][0 + 2]", "3")]
    [InlineData("[[1, 1, 1]][0][0]", "1")]
    [InlineData("{1: 1, 2: 2}[1]", "1")]
    [InlineData("{1: 1, 2: 2}[2]", "2")]
    public async Task TestIndexExpressions(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }
}
