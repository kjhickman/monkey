using DotMake.CommandLine;
using Kong.CodeGeneration;
using Kong.Common;
using Kong.Cli.NativeAot;
using System.Runtime.InteropServices;

namespace Kong.Cli.Commands;

[CliCommand(Name = "build", Description = "Build a runnable .NET artifact", Parent = typeof(Root))]
public class BuildFile
{
    [CliArgument(Description = "File to build", Required = true)]
    public string File { get; set; } = null!;

    [CliOption(Description = "Publish a native AOT executable")]
    public bool Native { get; set; }

    [CliOption(Description = "Target runtime identifier (defaults to current machine)")]
    public string Rid { get; set; } = string.Empty;

    [CliOption(Description = "Build configuration for native publish")]
    public string Configuration { get; set; } = "Release";

    public INativeAotPublisher NativeAotPublisher { get; set; } = new NativeAotPublisher();

    public void Run(CliContext context)
    {
        if (!ValidateNativeOptions(out var optionDiagnostics))
        {
            Compilation.PrintDiagnostics(optionDiagnostics);
            return;
        }

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

        if (!Native)
        {
            Console.WriteLine(result.AssemblyPath);
            return;
        }

        if (result.AssemblyPath == null)
        {
            var buildDiagnostics = new DiagnosticBag();
            buildDiagnostics.Report(Span.Empty, "native publish requires generated assembly path", "CLI021");
            Compilation.PrintDiagnostics(buildDiagnostics);
            return;
        }

        var runtimeIdentifier = string.IsNullOrWhiteSpace(Rid)
            ? RuntimeInformation.RuntimeIdentifier
            : Rid.Trim();

        var publishDirectory = Path.Combine(outputDirectory, "native", runtimeIdentifier);
        var publishResult = NativeAotPublisher.Publish(
            result.AssemblyPath,
            publishDirectory,
            assemblyName,
            runtimeIdentifier,
            Configuration);

        if (!publishResult.Published || publishResult.BinaryPath == null)
        {
            Compilation.PrintDiagnostics(publishResult.Diagnostics);
            return;
        }

        Console.WriteLine(publishResult.BinaryPath);
    }

    private bool ValidateNativeOptions(out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        if (!Native && !string.IsNullOrWhiteSpace(Rid))
        {
            diagnostics.Report(Span.Empty, "'--rid' requires '--native'", "CLI021");
        }

        if (!Native && !string.Equals(Configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Report(Span.Empty, "'--configuration' requires '--native'", "CLI021");
        }

        if (Native && !string.Equals(Configuration, "Release", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Report(Span.Empty, "'--configuration' must be 'Release' or 'Debug'", "CLI021");
        }

        return !diagnostics.HasErrors;
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
