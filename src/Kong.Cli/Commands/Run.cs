using DotMake.CommandLine;
using System.Diagnostics;
using Kong.CodeGeneration;

namespace Kong.Cli.Commands;

[CliCommand(Name = "run", Description = "Run a file", Parent = typeof(Root))]
public class RunFile
{
    [CliArgument(Description = "File to run", Required = true)]
    public string File { get; set; } = null!;

    public void Run(CliContext context)
    {
        if (!Compilation.TryCompile(File, out var unit, out var names, out var typeCheck, out var diagnostics))
        {
            Compilation.PrintDiagnostics(diagnostics);
            return;
        }

        if (!Compilation.ValidateProgramEntrypoint(unit, typeCheck, out var entryDiagnostics))
        {
            Compilation.PrintDiagnostics(entryDiagnostics);
            return;
        }

        var assemblyName = Path.GetFileNameWithoutExtension(File);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "kong-run", Guid.NewGuid().ToString("N"));

        var builder = new ClrArtifactBuilder();
        var build = builder.BuildArtifact(unit, typeCheck, outputDirectory, assemblyName, names);
        if (!build.Built || build.AssemblyPath == null)
        {
            Compilation.PrintDiagnostics(build.Diagnostics);
            return;
        }

        var run = RunArtifact(build.AssemblyPath);
        if (!string.IsNullOrEmpty(run.StdOut))
        {
            Console.Out.Write(run.StdOut);
        }

        if (!string.IsNullOrEmpty(run.StdErr))
        {
            Console.Error.Write(run.StdErr);
        }

        if (run.ExitCode != 0)
        {
            Environment.ExitCode = run.ExitCode;
            return;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunArtifact(string assemblyPath)
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
}
