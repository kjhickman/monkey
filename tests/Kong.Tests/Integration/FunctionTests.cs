namespace Kong.Tests.Integration;

public class FunctionTests
{
    [Theory]
    [InlineData("let fivePlusTen = fn() { 5 + 10; }; puts(fivePlusTen());", "15")]
    [InlineData("let one = fn() { 1; }; let two = fn() { 2; }; puts(one() + two());", "3")]
    [InlineData("let a = fn() { 1 }; let b = fn() { a() + 1 }; let c = fn() { b() + 1 }; puts(c());", "3")]
    public async Task TestFunctionsWithoutArguments(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let earlyExit = fn() { return 99; 100; }; puts(earlyExit());", "99")]
    [InlineData("let earlyExit = fn() { return 99; return 100; }; puts(earlyExit());", "99")]
    public async Task TestFunctionsWithReturnStatements(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let one = fn() { let one = 1; one }; puts(one());", "1")]
    [InlineData("let oneAndTwo = fn() { let one = 1; let two = 2; one + two; }; puts(oneAndTwo());", "3")]
    [InlineData("let oneAndTwo = fn() { let one = 1; let two = 2; one + two; }; let threeAndFour = fn() { let three = 3; let four = 4; three + four; }; puts(oneAndTwo() + threeAndFour());", "10")]
    [InlineData("let firstFoobar = fn() { let foobar = 50; foobar; }; let secondFoobar = fn() { let foobar = 100; foobar; }; puts(firstFoobar() + secondFoobar());", "150")]
    public async Task TestFunctionsWithLocalBindings(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let add = fn(a: int, b: int) { a + b }; puts(add(5, 3));", "8")]
    [InlineData("let identity = fn(x: int) { x }; puts(identity(42));", "42")]
    [InlineData("let sum = fn(a: int, b: int) { let c = a + b; c; }; puts(sum(1, 2) + sum(3, 4));", "10")]
    [InlineData("let sum = fn(a: int, b: int) { let c = a + b; c; }; let outer = fn() { sum(1, 2) + sum(3, 4); }; puts(outer());", "10")]
    [InlineData("let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; puts(choose(8));", "10")]
    [InlineData("let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; puts(choose(3));", "20")]
    [InlineData("let get = fn(h: map[string]int) { h[\"answer\"] }; puts(get({\"answer\": 42}));", "42")]
    public async Task TestFunctionsWithArgumentsAndBindings(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let factorial = fn(x: int) { if (x == 0) { 1 } else { x * factorial(x - 1) } }; puts(factorial(5));", "120")]
    public async Task TestRecursiveFunctions(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let newClosure = fn(a: int) { fn() { a; }; }; let closure = newClosure(99); puts(closure());", "99")]
    [InlineData("let newAdder = fn(a: int, b: int) { fn(c: int) { a + b + c }; }; let adder = newAdder(1, 2); puts(adder(8));", "11")]
    [InlineData("let newAdder = fn(a: int, b: int) { let c = a + b; fn(d: int) { c + d }; }; let adder = newAdder(1, 2); puts(adder(8));", "11")]
    [InlineData("let newAdderOuter = fn(a: int, b: int) { let c = a + b; fn(d: int) { let e = d + c; fn(f: int) { e + f; }; }; }; let newAdderInner = newAdderOuter(1, 2); let adder = newAdderInner(3); puts(adder(8));", "14")]
    [InlineData("let newClosure = fn(a: int, b: int) { let one = fn() { a; }; let two = fn() { b; }; fn() { one() + two(); }; }; let closure = newClosure(9, 90); puts(closure());", "99")]
    [InlineData("let add = fn(a: int, b: int) { a + b }; let wrapper = fn() { let add = fn(x: int) { x + 1 }; add(41); }; puts(wrapper());", "42")]
    public async Task TestClosures(string source, string expected)
    {
        var clrOutput = await IntegrationTestHarness.CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }
}
