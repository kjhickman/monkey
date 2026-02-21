using DotMake.CommandLine;
using Kong.CodeGeneration;

namespace Kong.Cli.Commands;

[CliCommand(Name = "build", Description = "Build a runnable .NET artifact", Parent = typeof(Root))]
public class BuildFile
{
    [CliArgument(Description = "File to build", Required = true)]
    public string File { get; set; } = null!;

    public void Run(CliContext context)
    {
        if (!Compilation.TryCompileModules(File, out var modules, out var unit, out _, out var typeCheck, out var diagnostics))
        {
            Compilation.PrintDiagnostics(diagnostics);
            return;
        }

        if (!Compilation.ValidateProgramEntrypoint(unit, typeCheck, out var entryDiagnostics))
        {
            Compilation.PrintDiagnostics(entryDiagnostics);
            return;
        }

        var outputDirectory = ResolveOutputDirectory();
        var assemblyName = ResolveAssemblyName();

        var builder = new ClrArtifactBuilder();
        var result = builder.BuildArtifact(modules, outputDirectory, assemblyName);
        if (!result.Built)
        {
            Compilation.PrintDiagnostics(result.Diagnostics);
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
