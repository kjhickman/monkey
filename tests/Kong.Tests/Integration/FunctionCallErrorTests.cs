namespace Kong.Tests.Integration;

public class FunctionCallErrorTests
{
    [Theory]
    [InlineData("let f = fn(x: int) { x + 1 }; puts(f(true));", "argument 1 for f expects int, got bool")]
    [InlineData("let f = fn(x: int) { x + 1 }; puts(f(1, 2));", "wrong number of arguments for f: want=1, got=2")]
    [InlineData("let f = fn(x: int, y: int) { x + y }; puts(f(1));", "wrong number of arguments for f: want=2, got=1")]
    [InlineData("let mk = fn(x: int) { fn(y: int) { x + y } }; let c = mk(1); puts(c(true));", "argument 1 for c expects int, got bool")]
    public void TestFunctionCallErrors(string source, string expectedError)
    {
        var compileError = IntegrationTestHarness.CompileWithExpectedError(source);
        Assert.Contains(expectedError, compileError);
    }
}
