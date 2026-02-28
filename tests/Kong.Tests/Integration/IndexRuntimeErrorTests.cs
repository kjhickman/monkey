namespace Kong.Tests.Integration;

public class IndexRuntimeErrorTests
{
    [Theory]
    [InlineData("puts([1, 2, 3][10]);", "IndexOutOfRangeException")]
    [InlineData("let get = fn(arr: int[], idx: int) { arr[idx] }; puts(get([1, 2, 3], 99));", "IndexOutOfRangeException")]
    public async Task TestArrayOutOfBoundsRaisesRuntimeError(string source, string expectedRuntimeError)
    {
        var runtimeError = await IntegrationTestHarness.CompileAndRunOnClrExpectRuntimeError(source);
        Assert.Contains(expectedRuntimeError, runtimeError);
    }

    [Theory]
    [InlineData("puts({1: 10}[2]);", "KeyNotFoundException")]
    [InlineData("let get = fn(h: map[int]int, k: int) { h[k] }; puts(get({1: 10}, 2));", "KeyNotFoundException")]
    [InlineData("puts({\"answer\": 42}[\"missing\"]);", "KeyNotFoundException")]
    public async Task TestMissingHashMapKeyRaisesRuntimeError(string source, string expectedRuntimeError)
    {
        var runtimeError = await IntegrationTestHarness.CompileAndRunOnClrExpectRuntimeError(source);
        Assert.Contains(expectedRuntimeError, runtimeError);
    }
}
