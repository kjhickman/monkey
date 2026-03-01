namespace Kong.Tests.Integration;

public class VarTests
{
    [Theory]
    [InlineData("var x = 5; puts(x);", "5")]
    [InlineData("var x = true; puts(x);", "True")]
    [InlineData("var x = \"hello\"; puts(x);", "hello")]
    [InlineData("var x = 3.14; puts(x);", "3.14")]
    public async Task TestVarDeclaration(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Fact]
    public async Task TestVarReassignment()
    {
        var source = "var x = 1; x = 2; puts(x);";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("2", clrOutput);
    }

    [Fact]
    public async Task TestMultipleReassignments()
    {
        var source = "var x = 0; x = 1; x = 2; x = 3; puts(x);";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("3", clrOutput);
    }

    [Fact]
    public async Task TestReassignmentWithExpression()
    {
        var source = "var x = 10; x = x + 5; puts(x);";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("15", clrOutput);
    }

    [Fact]
    public async Task TestReassignmentOfBool()
    {
        var source = "var flag = false; flag = true; puts(flag);";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("True", clrOutput);
    }

    [Fact]
    public async Task TestVarInsideFunction()
    {
        var source = "let counter = fn() { var n = 0; n = n + 1; n = n + 1; n }; puts(counter());";
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal("2", clrOutput);
    }

    [Theory]
    [InlineData("let x = 1; x = 2; puts(x);", "cannot assign to immutable variable 'x' (declared with 'let')")]
    [InlineData("y = 5; puts(y);", "undefined variable: 'y'")]
    public void TestAssignmentErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
