namespace Kong.Tests.Integration;

public class TypeErrorTests
{
    [Theory]
    [InlineData("puts([1, \"two\"]);", "array elements must have the same type")]
    [InlineData("puts({1: 1, \"two\": 2});", "hash map keys and values must have the same type")]
    [InlineData("puts(-true);", "cannot apply operator '-' to type bool")]
    [InlineData("puts(!1);", "cannot apply operator '!' to type int")]
    [InlineData("puts(1 + \"two\");", "cannot apply operator '+'")]
    [InlineData("puts(1 == true);", "cannot compare types int and bool")]
    [InlineData("puts(1 != false);", "cannot compare types int and bool")]
    [InlineData("puts(if (1) { 10 } else { 20 });", "if condition must be of type Boolean")]
    [InlineData("puts(if (true) { 1 } else { \"two\" });", "if branches must have the same type")]
    [InlineData("let x = puts(1);", "cannot assign an expression with no value")]
    public void TestTypeErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }

    [Theory]
    [InlineData("puts([1, 2, 3][\"zero\"]);", "array index must be Int64")]
    [InlineData("puts({1: 2}[\"one\"]);", "hash map index must be int")]
    [InlineData("puts(5[0]);", "index operator not supported")]
    [InlineData("puts(\"abc\"[0]);", "index operator not supported")]
    public void TestProvableIndexTypeErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
