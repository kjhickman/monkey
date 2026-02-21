using Kong.Cli.Commands;

namespace Kong.Tests.Integration;

public class RunCommandIntegrationTests
{
    [Fact]
    public void TestRunCommandReportsInferenceDiagnostic()
    {
        var filePath = CreateTempProgram("fn Main() { let x = if (true) { 1 }; }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[T119]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandFailsFastBeforeCompilerDiagnostics()
    {
        var filePath = CreateTempProgram("fn Main() { foobar; }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.DoesNotContain("ERROR:", stdout);
            Assert.Contains("[N001]", stderr);
            Assert.DoesNotContain("[C001]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesValidProgram()
    {
        var filePath = CreateTempProgram("fn Main() { let x = 2; x + 3; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesSimpleAdditionWithClrPhase1Backend()
    {
        var filePath = CreateTempProgram("fn Main() { 1 + 1; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesFunctionCallWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { fn(x: int) -> int { return x + 1; }(5); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesArrayAndStaticClrProgramWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { let xs: int[] = [1, 2, 3]; System.Console.WriteLine(xs[0]); System.Math.Abs(-4); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("1", stdout);
            Assert.DoesNotContain("[IR001]", stderr);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesClosureProgramWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { let f = fn(outer: int) -> int { let g = fn(x: int) -> int { x + outer }; g(5); }; f(10); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesNamedFunctionDeclarationProgram()
    {
        var filePath = CreateTempProgram("fn Add(x: int, y: int) -> int { x + y; } fn Main() { Add(20, 22); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandPropagatesMainIntExitCodeSilently()
    {
        var filePath = CreateTempProgram("fn Main() -> int { 7; }");
        try
        {
            var (_, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(7, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrOutput()
    {
        var filePath = CreateTempProgram("fn Main() { System.Console.WriteLine(42); }");
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("42", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrWriteLineOutput()
    {
        var filePath = CreateTempProgram("fn Main() { System.Console.WriteLine(42); }");
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("42", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsImportedStaticClrWriteLineOutput()
    {
        var filePath = CreateTempProgram("import System.Console; fn Main() { Console.WriteLine(42); }");
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("42", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandReportsUnsupportedIfWithoutElseBeforeLowering()
    {
        var filePath = CreateTempProgram("fn Main() { if (true) { 1 }; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("[CLI007]", stderr);
            Assert.DoesNotContain("[IR001]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandDoesNotFallbackToVmByDefault()
    {
        var filePath = CreateTempProgram("\"hello\";");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("[CLI002]", stderr);
            Assert.Contains("[CLI003]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateTempProgram(string source)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kong-run-test-{Guid.NewGuid():N}.kg");
        File.WriteAllText(filePath, source);
        return filePath;
    }

    private static (string Stdout, string Stderr, int ExitCode) ExecuteRunCommand(string filePath)
    {
        var command = new RunFile { File = filePath };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            command.Run(null!);
            return (stdout.ToString(), stderr.ToString(), Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
