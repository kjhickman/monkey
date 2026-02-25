using System.Diagnostics;
using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Name = "run", Description = "Run a file", Parent = typeof(Root))]
public class Run
{
    [CliArgument(Description = "File to run", Required = true)]
    public string File { get; set; } = null!;

    public async Task RunAsync()
    {
        var assemblyPath = await Build.BuildAsync(File);
        if (assemblyPath == null) return;
        
        var (exitCode, stdOut, stdErr) = await RunArtifact(assemblyPath);
        if (!string.IsNullOrEmpty(stdOut))
        {
            Console.Out.Write(stdOut);
        }

        if (!string.IsNullOrEmpty(stdErr))
        {
            Console.Error.Write(stdErr);
        }

        if (exitCode != 0)
        {
            Environment.ExitCode = exitCode;
            return;
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunArtifact(string assemblyPath)
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
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdOut, stdErr);
    }
}
