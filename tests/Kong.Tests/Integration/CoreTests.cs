namespace Kong.Tests.Integration;

public class CoreTests
{
    [Theory]
    [InlineData("1", 1L)]
    [InlineData("2", 2L)]
    [InlineData("1 + 2", 3L)]
    [InlineData("1 - 2", -1L)]
    [InlineData("1 * 2", 2L)]
    [InlineData("4 / 2", 2L)]
    [InlineData("50 / 2 * 2 + 10 - 5", 55L)]
    [InlineData("5 + 5 + 5 + 5 - 10", 10L)]
    [InlineData("2 * 2 * 2 * 2 * 2", 32L)]
    [InlineData("5 * 2 + 10", 20L)]
    [InlineData("5 + 2 * 10", 25L)]
    [InlineData("5 * (2 + 10)", 60L)]
    [InlineData("-5", -5L)]
    [InlineData("-10", -10L)]
    [InlineData("-50 + 100 + -50", 0L)]
    [InlineData("[1, 2, 3][0] + 1", 2L)]
    [InlineData("(5 + 10 * 2 + 15 / 3) * 2 + -10", 50L)]
    public async Task TestIntegerArithmetic(string source, long expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let one = 1; puts(one);", 1L)]
    [InlineData("let one = 1; let two = 2; puts(one + two);", 3L)]
    [InlineData("let one = 1; let two = one + one; puts(one + two);", 3L)]
    public async Task TestLetBindings(string source, long expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let one = 1 let two = 2 puts(one + two)", "3")]
    [InlineData("let one = 1; let two = 2 puts(one + two)", "3")]
    [InlineData("let one = 1 let two = 2; puts(one + two)", "3")]
    public async Task TestSemicolonsAreOptionalEvenOnOneLine(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("1 > 2", false)]
    [InlineData("1 < 1", false)]
    [InlineData("1 > 1", false)]
    [InlineData("1 == 1", true)]
    [InlineData("1 != 1", false)]
    [InlineData("1 == 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("true == true", true)]
    [InlineData("false == false", true)]
    [InlineData("true == false", false)]
    [InlineData("true != false", true)]
    [InlineData("false != true", true)]
    [InlineData("(1 < 2) == true", true)]
    [InlineData("(1 < 2) == false", false)]
    [InlineData("(1 > 2) == true", false)]
    [InlineData("(1 > 2) == false", true)]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    [InlineData("!!true", true)]
    [InlineData("!!false", false)]
    [InlineData("!(1 < 2)", false)]
    [InlineData("!((1 + 2) == 4)", true)]
    [InlineData("if (1 < 2) { true } else { false }", true)]
    [InlineData("if (1 > 2) { true } else { false }", false)]
    public async Task TestBooleanExpressions(string source, bool expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("puts(if (true) { 10 } else { 20 });", "10")]
    [InlineData("puts(if (false) { 10 } else { 20 });", "20")]
    [InlineData("puts(if (1 < 2) { 10 } else { 20 });", "10")]
    [InlineData("puts(if (1 > 2) { 10 } else { 20 });", "20")]
    [InlineData("puts(if (!(1 > 2)) { 10 } else { 20 });", "10")]
    [InlineData("puts(if (1 < 2) { 10 + 5 } else { 20 + 5 });", "15")]
    [InlineData("puts(if (true) { if (false) { 1 } else { 2 } } else { 3 });", "2")]
    [InlineData("let x = 5; puts(if (x > 10) { x } else { x + 1 });", "6")]
    public async Task TestConditionals(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("if (true) { 10 }; puts(99);", "99")]
    [InlineData("if (false) { 10 }; puts(99);", "99")]
    [InlineData("let x = 1; if (x == 1) { x + 2 }; puts(x + 10);", "11")]
    [InlineData("if (true) { if (false) { 10 }; 20 }; puts(30);", "30")]
    [InlineData("let f = fn() { if (true) { puts(1); } }; f();", "1")]
    public async Task TestIfWithoutElseAsStatement(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("\"monkey\"", "monkey")]
    [InlineData("\"mon\" + \"key\"", "monkey")]
    [InlineData("\"mon\" + \"key\" + \"banana\"", "monkeybanana")]
    public async Task TestStringExpressions(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }
}
