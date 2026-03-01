namespace Kong.Tests.Integration;

public class ComparisonTests
{
    [Theory]
    [InlineData("1 <= 1", "True")]
    [InlineData("1 <= 2", "True")]
    [InlineData("2 <= 1", "False")]
    [InlineData("1 >= 1", "True")]
    [InlineData("2 >= 1", "True")]
    [InlineData("1 >= 2", "False")]
    public async Task TestIntLessGreaterOrEqual(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("1.0 <= 1.0", "True")]
    [InlineData("1.0 <= 2.0", "True")]
    [InlineData("2.0 <= 1.0", "False")]
    [InlineData("1.0 >= 1.0", "True")]
    [InlineData("2.0 >= 1.0", "True")]
    [InlineData("1.0 >= 2.0", "False")]
    public async Task TestDoubleLessGreaterOrEqual(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData(@"""abc"" <= ""abd""", "True")]
    [InlineData(@"""abc"" <= ""abc""", "True")]
    [InlineData(@"""abd"" <= ""abc""", "False")]
    [InlineData(@"""abd"" >= ""abc""", "True")]
    [InlineData(@"""abc"" >= ""abc""", "True")]
    [InlineData(@"""abc"" >= ""abd""", "False")]
    public async Task TestStringLessGreaterOrEqual(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("'a' <= 'b'", "True")]
    [InlineData("'a' <= 'a'", "True")]
    [InlineData("'b' <= 'a'", "False")]
    [InlineData("'b' >= 'a'", "True")]
    [InlineData("'a' >= 'a'", "True")]
    [InlineData("'a' >= 'b'", "False")]
    public async Task TestCharLessGreaterOrEqual(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Fact]
    public async Task TestLessEqualInCondition()
    {
        var source = "let clamp = fn(x: int, lo: int, hi: int) { if (x >= lo && x <= hi) { true } else { false } }; puts(clamp(5, 1, 10)); puts(clamp(1, 1, 10)); puts(clamp(10, 1, 10));";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("True\nTrue\nTrue", clrOutput);
    }
}
