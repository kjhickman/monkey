using DotMake.CommandLine;
using Kong.CodeGeneration;

namespace Kong.Cli.Commands;

[CliCommand(Name = "build", Description = "Build a file", Parent = typeof(Root))]
public class Build
{
    [CliArgument(Description = "File to build", Required = true)]
    public string File { get; set; } = null!;

    public async Task RunAsync()
    {
        await BuildAsync(File);
    }

    // todo: use some result type instead of null to indicate failure
    public static async Task<string?> BuildAsync(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return null;
        }

        var assemblyName = Path.GetFileNameWithoutExtension(filePath);
        var assemblyPath = GetOutputAssemblyPath(filePath);
        var builder = new ClrArtifactBuilder();
        builder.Build(assemblyName, assemblyPath);
        return assemblyPath;
    }

    public static string GetOutputAssemblyPath(string inputFile)
    {
        return Path.Combine(
            Path.GetDirectoryName(inputFile)!,
            "target",
            Path.GetFileNameWithoutExtension(inputFile) + ".dll"
            );
    }
}
