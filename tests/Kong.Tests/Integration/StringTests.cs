namespace Kong.Tests.Integration;

public class StringTests
{
    [Theory]
    [InlineData("\"hello\" == \"hello\"", true)]
    [InlineData("\"hello\" == \"world\"", false)]
    [InlineData("\"hello\" != \"world\"", true)]
    [InlineData("\"hello\" != \"hello\"", false)]
    public async Task TestStringEqualityWithLiterals(string source, bool expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let sa = \"hel\" + \"lo\"; let sb = \"hello\"; puts(sa == sb);", "True")]
    [InlineData("let sa = \"hel\" + \"lo\"; let sb = \"world\"; puts(sa == sb);", "False")]
    [InlineData("let sa = \"hel\" + \"lo\"; let sb = \"hello\"; puts(sa != sb);", "False")]
    [InlineData("let sa = \"foo\" + \"bar\"; let sb = \"foo\" + \"baz\"; puts(sa != sb);", "True")]
    public async Task TestStringEqualityWithConcatenatedStrings(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("\"abc\" < \"abd\"", true)]
    [InlineData("\"abc\" < \"abc\"", false)]
    [InlineData("\"b\" > \"a\"", true)]
    [InlineData("\"a\" > \"b\"", false)]
    [InlineData("\"abc\" > \"abc\"", false)]
    [InlineData("\"apple\" < \"banana\"", true)]
    public async Task TestStringOrdering(string source, bool expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let greet = fn(name: string) { \"hello \" + name }; puts(greet(\"world\"));", "hello world")]
    [InlineData("let upper = fn(s: string) { s + \"!\" }; puts(upper(\"hi\"));", "hi!")]
    public async Task TestFunctionsWithStringArguments(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }
}
