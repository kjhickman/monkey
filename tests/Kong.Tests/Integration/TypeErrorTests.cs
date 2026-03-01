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

    [Theory]
    [InlineData("let x = 1; x = 2; puts(x);", "cannot assign to immutable variable 'x' (declared with 'let')")]
    [InlineData("let x = \"hello\"; x = \"world\"; puts(x);", "cannot assign to immutable variable 'x' (declared with 'let')")]
    public void TestLetVariableReassignmentIsCompileError(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }

    [Theory]
    [InlineData("puts(z);", "Undefined variable: z")]
    [InlineData("let f = fn() { undeclared }; puts(f());", "Undefined variable: undeclared")]
    public void TestUndefinedVariableErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }

    [Theory]
    [InlineData("puts(if (true) { puts(1) } else { puts(2) });", "if/else used as an expression must produce a value")]
    public void TestVoidIfElseExpressionError(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }

    [Theory]
    [InlineData("let f = fn(x) { x + 1 }; puts(f(5));", "expected ':' after function parameter name")]
    [InlineData("let f = fn(x: blorp) { x }; puts(f(1));", "invalid type annotation for parameter 'x'")]
    public void TestMissingOrInvalidTypeAnnotationErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
