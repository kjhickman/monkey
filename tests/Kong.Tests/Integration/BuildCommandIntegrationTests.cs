using System.Diagnostics;
using Kong.Cli.Commands;

namespace Kong.Tests.Integration;

public class BuildCommandIntegrationTests
{
    [Fact]
    public void TestBuildCommandCreatesRunnableArtifact()
    {
        var sourcePath = CreateTempProgram("fn Main() { let x = 40; x + 2; }");
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

            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void TestBuiltArtifactUsesMainIntAsExitCode()
    {
        var sourcePath = CreateTempProgram("fn Main() -> int { 5; }");
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

            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void TestBuildCommandSupportsPathImportFromAnotherKongFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-build-module-test-{Guid.NewGuid():N}");
        var workingDir = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(workingDir);

            var utilPath = Path.Combine(tempDirectory, "util.kg");
            var mainPath = Path.Combine(tempDirectory, "main.kg");
            File.WriteAllText(utilPath, "namespace Util; fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(20, 22); }");

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

    private static string CreateTempProgram(string source)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kong-build-test-{Guid.NewGuid():N}.kg");
        File.WriteAllText(filePath, EnsureFileScopedNamespace(source));
        return filePath;
    }

    private static string EnsureFileScopedNamespace(string source)
    {
        if (source.Contains("namespace "))
        {
            return source;
        }

        var insertIndex = 0;
        while (true)
        {
            var remainder = source[insertIndex..].TrimStart();
            var skipped = source[insertIndex..].Length - remainder.Length;
            insertIndex += skipped;

            if (!source[insertIndex..].StartsWith("import "))
            {
                break;
            }

            var semicolonIndex = source.IndexOf(';', insertIndex);
            if (semicolonIndex < 0)
            {
                break;
            }

            insertIndex = semicolonIndex + 1;
        }

        return source.Insert(insertIndex, " namespace Test; ");
    }
}
