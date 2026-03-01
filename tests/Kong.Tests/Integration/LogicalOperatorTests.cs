namespace Kong.Tests.Integration;

public class LogicalOperatorTests
{
    [Theory]
    [InlineData("true && true", "True")]
    [InlineData("true && false", "False")]
    [InlineData("false && true", "False")]
    [InlineData("false && false", "False")]
    public async Task TestLogicalAnd(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("true || true", "True")]
    [InlineData("true || false", "True")]
    [InlineData("false || true", "True")]
    [InlineData("false || false", "False")]
    public async Task TestLogicalOr(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(1 > 0 && 2 > 1);", "True")]
    [InlineData("puts(1 > 0 && 2 > 3);", "False")]
    public async Task TestAndWithComparisons(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(1 > 5 || 2 > 1);", "True")]
    [InlineData("puts(1 > 5 || 2 > 3);", "False")]
    public async Task TestOrWithComparisons(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Fact]
    public async Task TestAndPrecedenceHigherThanOr()
    {
        // false || (true && true) == true, not (false || true) && true == true
        // Both happen to be true here; use a case where it matters:
        // true || false && false => true || (false && false) => true || false => true
        // If AND had lower precedence: (true || false) && false => true && false => false
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr("puts(true || false && false);");
        Assert.Equal("True", clrOutput);
    }

    [Fact]
    public async Task TestLogicalOperatorsInFunction()
    {
        var source = "let between = fn(x: int, lo: int, hi: int) { lo < x && x < hi }; puts(between(5, 1, 10)); puts(between(15, 1, 10));";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal($"True{Environment.NewLine}False", clrOutput);
    }

    [Fact]
    public async Task AndShortCircuits_WhenLeftIsFalse_RightIsNotEvaluated()
    {
        // 0 != 0 is false; short-circuit must prevent evaluating 10 % 0 (divide by zero)
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr("let x = 0; puts(x != 0 && 10 % x == 0);");
        Assert.Equal("False", clrOutput);
    }

    [Fact]
    public async Task OrShortCircuits_WhenLeftIsTrue_RightIsNotEvaluated()
    {
        // 0 == 0 is true; short-circuit must prevent evaluating 10 % 0 (divide by zero)
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr("let x = 0; puts(x == 0 || 10 % x == 0);");
        Assert.Equal("True", clrOutput);
    }
}
