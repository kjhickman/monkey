using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Name = "build", Description = "Build a runnable .NET artifact", Parent = typeof(Root))]
public class BuildFile
{
    [CliArgument(Description = "File to build", Required = true)]
    public string File { get; set; } = null!;

    public void Run(CliContext context)
    {
        if (!CommandCompilation.TryCompileFile(File, out var unit, out var names, out var typeCheck, out var diagnostics))
        {
            CommandCompilation.PrintDiagnostics(diagnostics);
            return;
        }

        if (!CommandCompilation.ValidateProgramEntrypoint(unit, typeCheck, out var entryDiagnostics))
        {
            CommandCompilation.PrintDiagnostics(entryDiagnostics);
            return;
        }

        var outputDirectory = ResolveOutputDirectory();
        var assemblyName = ResolveAssemblyName();

        var builder = new ClrPhase1Executor();
        var result = builder.BuildArtifact(unit, typeCheck, outputDirectory, assemblyName, names);
        if (!result.Built)
        {
            CommandCompilation.PrintDiagnostics(result.Diagnostics);
            return;
        }

        Console.WriteLine(result.AssemblyPath);
    }

    private string ResolveOutputDirectory()
    {
        var fileName = ResolveAssemblyName();
        return Path.Combine(Directory.GetCurrentDirectory(), "dist", fileName);
    }

    private string ResolveAssemblyName()
    {
        return Path.GetFileNameWithoutExtension(File);
    }
}
