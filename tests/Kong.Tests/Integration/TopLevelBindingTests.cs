namespace Kong.Tests.Integration;

public class TopLevelBindingTests
{
    [Theory]
    [InlineData("let x = 1; let x = 2; puts(x);", "duplicate top-level binding: x")]
    [InlineData("let x = 1; let x = fn() { 2 }; puts(x());", "duplicate top-level binding: x")]
    [InlineData("let x = fn() { 1 }; let x = 2; puts(x);", "duplicate top-level binding: x")]
    [InlineData("let x = fn() { 1 }; let x = fn() { 2 }; puts(x());", "duplicate top-level function definition: x")]
    public void TestDuplicateTopLevelBindingsAreCompilerErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
