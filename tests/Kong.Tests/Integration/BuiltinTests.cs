namespace Kong.Tests.Integration;

public class BuiltinTests
{
    [Theory]
    [InlineData("puts(\"hello\");", "hello")]
    [InlineData("puts(1, true, \"x\");", "1\nTrue\nx")]
    [InlineData("puts([1, 2], {1: 2});", "[1, 2]\n{1: 2}")]
    public async Task TestPutsBuiltin(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(len(\"\"));", "0")]
    [InlineData("puts(len(\"four\"));", "4")]
    [InlineData("puts(len(\"hello world\"));", "11")]
    [InlineData("puts(len([1, 2, 3]));", "3")]
    [InlineData("puts(len([]));", "0")]
    public async Task TestLenBuiltin(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(len(1));", "argument to `len` not supported")]
    [InlineData("puts(len(\"one\", \"two\"));", "wrong number of arguments. got=2, want=1")]
    public void TestLenBuiltinErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }

    [Theory]
    [InlineData("puts(push([], 1));", "[1]")]
    [InlineData("puts(push([1, 2], 3));", "[1, 2, 3]")]
    [InlineData("let a = [1, 2]; let b = push(a, 3); puts(a); puts(b);", "[1, 2]\n[1, 2, 3]")]
    public async Task TestPushBuiltin(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(push(1, 1));", "argument to `push` must be ARRAY")]
    [InlineData("puts(push([1], 2, 3));", "wrong number of arguments. got=3, want=2")]
    public void TestPushBuiltinErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
