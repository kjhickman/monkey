using Kong.Cli.Commands;

namespace Kong.Tests;

public class RunCommandIntegrationTests
{
    [Fact]
    public void TestRunCommandReportsInferenceDiagnostic()
    {
        var filePath = CreateTempProgram("let x = if (true) { 1 };");
        try
        {
            var (stdout, stderr) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[T119]", stderr);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandFailsFastBeforeCompilerDiagnostics()
    {
        var filePath = CreateTempProgram("foobar;");
        try
        {
            var (stdout, stderr) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[N001]", stderr);
            Assert.DoesNotContain("[C001]", stderr);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesValidProgram()
    {
        var filePath = CreateTempProgram("let x = 2; x + 3;");
        try
        {
            var (stdout, stderr) = ExecuteRunCommand(filePath);
            Assert.Contains("5", stdout);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesSimpleAdditionWithClrPhase1Backend()
    {
        var filePath = CreateTempProgram("1 + 1;");
        try
        {
            var (stdout, stderr) = ExecuteRunCommand(filePath);
            Assert.Contains("2", stdout);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    private static string CreateTempProgram(string source)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kong-run-test-{Guid.NewGuid():N}.kg");
        System.IO.File.WriteAllText(filePath, source);
        return filePath;
    }

    private static (string Stdout, string Stderr) ExecuteRunCommand(string filePath)
    {
        var command = new RunFile { File = filePath };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            command.Run(null!);
            return (stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
