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
    [InlineData("[\"a\", \"b\", \"c\"]", "[a, b, c]")]
    [InlineData("[\"hello\", \"world\"]", "[hello, world]")]
    public async Task TestStringArrayLiterals(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("[true, false, true]", "[True, False, True]")]
    [InlineData("[false, false]", "[False, False]")]
    public async Task TestBoolArrayLiterals(string source, string expected)
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
    [InlineData("{\"a\": 1, \"b\": 2}[\"a\"]", "1")]
    [InlineData("{\"key\": \"value\"}[\"key\"]", "value")]
    [InlineData("{\"x\": 10, \"y\": 20}[\"y\"]", "20")]
    public async Task TestStringKeyedHashLiterals(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("{true: 1, false: 0}[true]", "1")]
    [InlineData("{true: 1, false: 0}[false]", "0")]
    public async Task TestBoolKeyedHashLiterals(string source, string expected)
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
    [InlineData("[\"a\", \"b\", \"c\"][0]", "a")]
    [InlineData("[\"a\", \"b\", \"c\"][2]", "c")]
    [InlineData("[true, false][0]", "True")]
    [InlineData("[true, false][1]", "False")]
    public async Task TestIndexExpressions(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }
}
