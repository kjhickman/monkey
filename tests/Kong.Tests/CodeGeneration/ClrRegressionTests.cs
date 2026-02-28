using System.Diagnostics;

namespace Kong.Tests.CodeGeneration;

public class ClrRegressionTests
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
    [InlineData("(5 + 10 * 2 + 15 / 3) * 2 + -10", 50L)]
    public async Task TestIntegerArithmetic(string source, long expected)
    {
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let one = 1; puts(one);", 1L)]
    [InlineData("let one = 1; let two = 2; puts(one + two);", 3L)]
    [InlineData("let one = 1; let two = one + one; puts(one + two);", 3L)]
    public async Task TestLetBindings(string source, long expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected.ToString(), clrOutput);
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
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("if (true) { 10 }; puts(99);", "99")]
    [InlineData("if (false) { 10 }; puts(99);", "99")]
    [InlineData("let x = 1; if (x == 1) { x + 2 }; puts(x + 10);", "11")]
    [InlineData("if (true) { if (false) { 10 }; 20 }; puts(30);", "30")]
    public async Task TestIfWithoutElseAsStatement(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("\"monkey\"", "monkey")]
    [InlineData("\"mon\" + \"key\"", "monkey")]
    [InlineData("\"mon\" + \"key\" + \"banana\"", "monkeybanana")]
    public async Task TestStringExpressions(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("[1, 2, 3]", "[1, 2, 3]")]
    [InlineData("[1 + 2, 3 * 4, 5 + 6]", "[3, 12, 11]")]
    public async Task TestArrayLiterals(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{1: 2, 2: 3}", "{1: 2, 2: 3}")]
    [InlineData("{1 + 1: 2 * 2, 3 + 3: 4 * 4}", "{2: 4, 6: 16}")]
    public async Task TestHashLiterals(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("[1, 2, 3][1]", "2")]
    [InlineData("[1, 2, 3][0 + 2]", "3")]
    [InlineData("[[1, 1, 1]][0][0]", "1")]
    [InlineData("{1: 1, 2: 2}[1]", "1")]
    [InlineData("{1: 1, 2: 2}[2]", "2")]
    public async Task TestIndexExpressions(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr($"puts({source});");
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("puts(\"hello\");", "hello")]
    [InlineData("puts(1, true, \"x\");", "1\nTrue\nx")]
    [InlineData("puts([1, 2], {1: 2});", "[1, 2]\n{1: 2}")]
    public async Task TestPutsBuiltin(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let fivePlusTen = fn() { 5 + 10; }; puts(fivePlusTen());", "15")]
    [InlineData("let one = fn() { 1; }; let two = fn() { 2; }; puts(one() + two());", "3")]
    [InlineData("let a = fn() { 1 }; let b = fn() { a() + 1 }; let c = fn() { b() + 1 }; puts(c());", "3")]
    public async Task TestFunctionsWithoutArguments(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let earlyExit = fn() { return 99; 100; }; puts(earlyExit());", "99")]
    [InlineData("let earlyExit = fn() { return 99; return 100; }; puts(earlyExit());", "99")]
    public async Task TestFunctionsWithReturnStatements(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let one = fn() { let one = 1; one }; puts(one());", "1")]
    [InlineData("let oneAndTwo = fn() { let one = 1; let two = 2; one + two; }; puts(oneAndTwo());", "3")]
    [InlineData("let oneAndTwo = fn() { let one = 1; let two = 2; one + two; }; let threeAndFour = fn() { let three = 3; let four = 4; three + four; }; puts(oneAndTwo() + threeAndFour());", "10")]
    [InlineData("let firstFoobar = fn() { let foobar = 50; foobar; }; let secondFoobar = fn() { let foobar = 100; foobar; }; puts(firstFoobar() + secondFoobar());", "150")]
    public async Task TestFunctionsWithLocalBindings(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let factorial = fn(x: int) { if (x == 0) { 1 } else { x * factorial(x - 1) } }; puts(factorial(5));", "120")]
    public async Task TestRecursiveFunctions(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    private static async Task<string> CompileAndRunOnClr(string source)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-clr-vm-port-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new KongCompiler();
            var compileError = compiler.CompileToAssembly(source, "program", assemblyPath);
            Assert.Null(compileError);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{assemblyPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"dotnet exited with code {process.ExitCode}: {stdErr}");

            return stdOut.TrimEnd();
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
