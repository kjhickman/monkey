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
    // [InlineData("!(if (false) { 5; })", true)] // Unsupported expression: IfExpression
    public async Task TestBooleanExpressions(string source, bool expected)
    {
        var clrOutput = await CompileAndRunOnClr(source);
        Assert.Equal(expected.ToString(), clrOutput);
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
