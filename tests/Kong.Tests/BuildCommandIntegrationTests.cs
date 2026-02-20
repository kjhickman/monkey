using System.Diagnostics;
using Kong.Cli.Commands;

namespace Kong.Tests;

public class BuildCommandIntegrationTests
{
    [Fact]
    public void TestBuildCommandCreatesRunnableArtifact()
    {
        var sourcePath = CreateTempProgram("let x = 40; x + 2;");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDir);
            var command = new BuildFile
            {
                File = sourcePath,
            };

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workingDir);
                Console.SetOut(stdout);
                Console.SetError(stderr);
                command.Run(null!);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            var assemblyName = Path.GetFileNameWithoutExtension(sourcePath);
            var outputDir = Path.Combine(workingDir, "dist", assemblyName);
            var assemblyPath = Path.Combine(outputDir, $"{assemblyName}.dll");
            var runtimeConfigPath = Path.Combine(outputDir, $"{assemblyName}.runtimeconfig.json");
            Assert.True(System.IO.File.Exists(assemblyPath));
            Assert.True(System.IO.File.Exists(runtimeConfigPath));

            var run = RunDotnet(assemblyPath);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("42", run.StdOut.Trim());
            Assert.Equal(string.Empty, run.StdErr.Trim());
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }

            System.IO.File.Delete(sourcePath);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunDotnet(string assemblyPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{assemblyPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    private static string CreateTempProgram(string source)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}.kg");
        System.IO.File.WriteAllText(filePath, source);
        return filePath;
    }
}
