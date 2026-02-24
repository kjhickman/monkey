using System.Diagnostics;
using Kong.Cli.Commands;
using Kong.Tests;

namespace Kong.Tests.Integration;

[Collection("CLI Integration")]
public class BuildCommandIntegrationTests
{
    [Fact]
    public void TestBuildCommandCreatesRunnableArtifact()
    {
        var sourcePath = CreateTempProgram("fn Main() { let x = 40 x + 2 }");
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
            Assert.True(File.Exists(assemblyPath));
            Assert.True(File.Exists(runtimeConfigPath));

            var run = RunDotnet(assemblyPath);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(string.Empty, run.StdOut.Trim());
            Assert.Equal(string.Empty, run.StdErr.Trim());
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }

            DeleteTempProgram(sourcePath);
        }
    }

    [Fact]
    public void TestBuiltArtifactUsesMainIntAsExitCode()
    {
        var sourcePath = CreateTempProgram("fn Main() -> int { 5 }");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDir);
            var command = new BuildFile { File = sourcePath };

            var originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workingDir);
                command.Run(null!);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }

            var assemblyName = Path.GetFileNameWithoutExtension(sourcePath);
            var outputDir = Path.Combine(workingDir, "dist", assemblyName);
            var assemblyPath = Path.Combine(outputDir, $"{assemblyName}.dll");

            var run = RunDotnet(assemblyPath);
            Assert.Equal(5, run.ExitCode);
            Assert.Equal(string.Empty, run.StdOut.Trim());
            Assert.Equal(string.Empty, run.StdErr.Trim());
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }

            DeleteTempProgram(sourcePath);
        }
    }

    [Fact]
    public void TestBuildCommandRejectsPathImportSyntax()
    {
        var missingName = $"missing-{Guid.NewGuid():N}.kg";
        var sourcePath = CreateTempProgram($"use \"./{missingName}\" fn Main() {{ 1 }}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDir);
            var (_, stdErr) = ExecuteBuildCommand(sourcePath, workingDir);
            Assert.Contains("[P001]", stdErr);
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }

            DeleteTempProgram(sourcePath);
        }
    }

    [Fact]
    public void TestBuildCommandReportsDuplicateTopLevelFunctionAcrossModules()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-module-test-{Guid.NewGuid():N}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(workingDir);

            var utilPath = Path.Combine(tempDirectory, "util.kg");
            var mainPath = Path.Combine(tempDirectory, "main.kg");
            File.WriteAllText(utilPath, "module Shared fn Add(x: int, y: int) -> int { x + y }");
            File.WriteAllText(mainPath, "module Shared fn Add(x: int, y: int) -> int { x - y } fn Main() { Add(1, 2) }");

            var (_, stdErr) = ExecuteBuildCommand(mainPath, workingDir);
            Assert.Contains("[CLI016]", stdErr);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }

    [Fact]
    public void TestBuildCommandReportsUnknownFunctionWithoutNamespaceImport()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-module-test-{Guid.NewGuid():N}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(workingDir);

            var utilPath = Path.Combine(tempDirectory, "util.kg");
            var mainPath = Path.Combine(tempDirectory, "main.kg");
            File.WriteAllText(utilPath, "module Helpers fn Add(x: int, y: int) -> int { x + y }");
            File.WriteAllText(mainPath, "module App fn Main() { Add(1, 2) }");

            var (_, stdErr) = ExecuteBuildCommand(mainPath, workingDir);
            Assert.Contains("[N001]", stdErr);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }

    [Fact]
    public void TestBuildCommandReportsPrivateFunctionAccessAcrossNamespaces()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-module-test-{Guid.NewGuid():N}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(workingDir);

            var utilPath = Path.Combine(tempDirectory, "util.kg");
            var mainPath = Path.Combine(tempDirectory, "main.kg");
            File.WriteAllText(utilPath, "module Helpers fn Add(x: int, y: int) -> int { x + y }");
            File.WriteAllText(mainPath, "use Helpers module App fn Main() { Add(1, 2) }");

            var (_, stdErr) = ExecuteBuildCommand(mainPath, workingDir);
            Assert.Contains("[CLI019]", stdErr);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }

    [Fact]
    public void TestBuildCommandSupportsNamespaceImportFromAnotherKongFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-module-test-{Guid.NewGuid():N}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(workingDir);

            var utilPath = Path.Combine(tempDirectory, "util.kg");
            var mainPath = Path.Combine(tempDirectory, "main.kg");
            File.WriteAllText(utilPath, "module Util public fn Add(x: int, y: int) -> int { x + y }");
            File.WriteAllText(mainPath, "use Util module App fn Main() { Add(20, 22) }");

            var command = new BuildFile { File = mainPath };
            var originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workingDir);
                command.Run(null!);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }

            var outputDir = Path.Combine(workingDir, "dist", "main");
            var assemblyPath = Path.Combine(outputDir, "main.dll");
            Assert.True(File.Exists(assemblyPath));

            var run = RunDotnet(assemblyPath);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(string.Empty, run.StdErr.Trim());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
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

    private static (string StdOut, string StdErr) ExecuteBuildCommand(string sourcePath, string workingDir)
    {
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
            return (stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static string CreateTempProgram(string source)
    {
        var programDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(programDirectory);
        var filePath = Path.Combine(programDirectory, "main.kg");
        File.WriteAllText(filePath, TestSourceUtilities.EnsureFileScopedNamespace(source));
        return filePath;
    }

    private static void DeleteTempProgram(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var directory = Path.GetDirectoryName(filePath);
        if (directory != null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
