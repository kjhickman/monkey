using System.Diagnostics;
using Kong.Compilation;

namespace Kong.Tests.Integration;

internal static class IntegrationTestHarness
{
    public static async Task<string> CompileAndRunOnClr(string source)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-clr-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new Compiler();
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

    public static string CompileWithExpectedError(string source)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-clr-compile-error-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new Compiler();
            var compileError = compiler.CompileToAssembly(source, "program", assemblyPath);
            Assert.NotNull(compileError);
            return compileError!;
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    public static async Task<string> CompileAndRunOnClrExpectRuntimeError(string source)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-clr-runtime-error-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new Compiler();
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
            Assert.True(process.ExitCode != 0, $"Expected runtime failure, but process exited with code 0. Output: {stdOut}");

            return $"{stdOut}\n{stdErr}".Trim();
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
