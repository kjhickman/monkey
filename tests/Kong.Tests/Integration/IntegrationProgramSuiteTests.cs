using Kong.Cli.Commands;

namespace Kong.Tests.Integration;

[Collection("CLI Integration")]
public sealed class IntegrationProgramSuiteTests
{
    public static IEnumerable<object[]> Scenarios()
    {
        var scenariosRoot = Path.Combine(GetRepositoryRoot(), "tests", "Kong.IntegrationPrograms");
        if (!Directory.Exists(scenariosRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(scenariosRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var mainPath = Path.Combine(directory, "main.kg");
            var expectedStdoutPath = Path.Combine(directory, "expected.stdout");
            if (!File.Exists(mainPath) || !File.Exists(expectedStdoutPath))
            {
                continue;
            }

            yield return [Path.GetFileName(directory), directory];
        }
    }

    [Fact]
    public void TestIntegrationProgramSuiteHasScenarios()
    {
        Assert.NotEmpty(Scenarios());
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TestIntegrationProgramScenario(string scenarioName, string scenarioDirectory)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        var expectedStdout = NormalizeOutput(File.ReadAllText(Path.Combine(scenarioDirectory, "expected.stdout")));
        var expectedStderrPath = Path.Combine(scenarioDirectory, "expected.stderr");
        var expectedExitCodePath = Path.Combine(scenarioDirectory, "expected.exitcode");

        var expectedStderr = File.Exists(expectedStderrPath)
            ? NormalizeOutput(File.ReadAllText(expectedStderrPath))
            : string.Empty;
        var expectedExitCode = File.Exists(expectedExitCodePath)
            ? int.Parse(File.ReadAllText(expectedExitCodePath).Trim(), System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        var mainPath = Path.Combine(scenarioDirectory, "main.kg");
        var (stdout, stderr, exitCode) = ExecuteRunCommand(mainPath);

        Assert.Equal(expectedStdout, NormalizeOutput(stdout));
        Assert.Equal(expectedStderr, NormalizeOutput(stderr));
        Assert.Equal(expectedExitCode, exitCode);
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

    private static string NormalizeOutput(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string GetRepositoryRoot()
    {
        var fromCurrentDirectory = TryFindRepositoryRoot(Directory.GetCurrentDirectory());
        if (fromCurrentDirectory != null)
        {
            return fromCurrentDirectory;
        }

        var fromBaseDirectory = TryFindRepositoryRoot(AppContext.BaseDirectory);
        if (fromBaseDirectory != null)
        {
            return fromBaseDirectory;
        }

        throw new InvalidOperationException("Could not locate repository root from test directories.");
    }

    private static string? TryFindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            var scenariosPath = Path.Combine(current.FullName, "tests", "Kong.IntegrationPrograms");
            if (Directory.Exists(scenariosPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
