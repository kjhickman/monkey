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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("let one = 1; one", 1L)]
    [InlineData("let one = 1; let two = 2; one + two", 3L)]
    [InlineData("let one = 1; let two = one + one; one + two", 3L)]
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected.ToString(), clrOutput);
    }

    [Theory]
    [InlineData("if (true) { 10 } else { 20 }", "10")]
    [InlineData("if (false) { 10 } else { 20 }", "20")]
    [InlineData("if (1 < 2) { 10 } else { 20 }", "10")]
    [InlineData("if (1 > 2) { 10 } else { 20 }", "20")]
    [InlineData("if (!(1 > 2)) { 10 } else { 20 }", "10")]
    [InlineData("if (1 < 2) { 10 + 5 } else { 20 + 5 }", "15")]
    [InlineData("if (true) { if (false) { 1 } else { 2 } } else { 3 }", "2")]
    [InlineData("let x = 5; if (x > 10) { x } else { x + 1 }", "6")]
    public async Task TestConditionals(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("if (true) { 10 }; 99", "99")]
    [InlineData("if (false) { 10 }; 99", "99")]
    [InlineData("let x = 1; if (x == 1) { x + 2 }; x + 10", "11")]
    [InlineData("if (true) { if (false) { 10 }; 20 }; 30", "30")]
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("[1, 2, 3]", "[1, 2, 3]")]
    [InlineData("[1 + 2, 3 * 4, 5 + 6]", "[3, 12, 11]")]
    public async Task TestArrayLiterals(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{1: 2, 2: 3}", "{1: 2, 2: 3}")]
    [InlineData("{1 + 1: 2 * 2, 3 + 3: 4 * 4}", "{2: 4, 6: 16}")]
    public async Task TestHashLiterals(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
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
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let add = fn(a: int, b: int) { a + b }; add(5, 3)", "8")]
    [InlineData("let identity = fn(x: int) { x }; identity(42)", "42")]
    [InlineData("let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; choose(8)", "10")]
    [InlineData("let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; choose(3)", "20")]
    [InlineData("let get = fn(h: map[string]int) { h[\"answer\"] }; get({\"answer\": 42})", "42")]
    [InlineData("let factorial = fn(x: int) { if (x == 0) { 1 } else { x * factorial(x - 1) } }; factorial(5)", "120")]
    public async Task TestTopLevelFunctionDeclarations(string source, string expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected, clrOutput);
    }

    [Theory]
    [InlineData("let outer = fn(x: int) { let inner = fn(y: int) { y }; inner(x) }; outer(1)", "nested function declarations are not supported")]
    [InlineData("let base = 10; let addBase = fn(x: int) { x + base }; addBase(1)", "captured variables are not supported")]
    public void TestClrFunctionDeclarationLimitations(string source, string expectedError)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-clr-fn-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new KongCompiler();
            var compileError = compiler.CompileToAssembly(source, "program", assemblyPath);
            Assert.NotNull(compileError);
            Assert.Contains(expectedError, compileError);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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
