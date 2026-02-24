using System.Diagnostics;
using System.Text;
using Kong.Common;

namespace Kong.Cli.NativeAot;

public interface INativeAotPublisher
{
    NativeAotPublishResult Publish(
        string assemblyPath,
        string outputDirectory,
        string assemblyName,
        string runtimeIdentifier,
        string configuration);
}

public sealed class NativeAotPublishResult
{
    public bool Published { get; set; }
    public string? BinaryPath { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}

public sealed class NativeAotPublisher : INativeAotPublisher
{
    public NativeAotPublishResult Publish(
        string assemblyPath,
        string outputDirectory,
        string assemblyName,
        string runtimeIdentifier,
        string configuration)
    {
        var result = new NativeAotPublishResult();

        if (!File.Exists(assemblyPath))
        {
            result.Diagnostics.Report(Span.Empty, $"generated assembly not found: {assemblyPath}", "CLI022");
            return result;
        }

        var kongAssemblyPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "Kong.dll");
        if (!File.Exists(kongAssemblyPath))
        {
            result.Diagnostics.Report(Span.Empty, $"required runtime assembly not found: {kongAssemblyPath}", "CLI022");
            return result;
        }

        var hostProjectDirectory = Path.Combine(Path.GetTempPath(), "kong-native-aot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hostProjectDirectory);

        try
        {
            var hostProjectPath = Path.Combine(hostProjectDirectory, "Kong.NativeHost.csproj");
            var hostProgramPath = Path.Combine(hostProjectDirectory, "Program.cs");
            var generatedAssemblyForAotPath = Path.Combine(hostProjectDirectory, "Kong.Generated.dll");

            File.Copy(assemblyPath, generatedAssemblyForAotPath, overwrite: true);

            File.WriteAllText(hostProjectPath, BuildHostProject(generatedAssemblyForAotPath, kongAssemblyPath, assemblyName));
            File.WriteAllText(hostProgramPath, BuildHostProgram());

            Directory.CreateDirectory(outputDirectory);
            var publish = ExecuteDotnetPublish(hostProjectPath, outputDirectory, runtimeIdentifier, configuration);
            if (!publish.Succeeded)
            {
                result.Diagnostics.Report(
                    Span.Empty,
                    $"native AOT publish failed for RID '{runtimeIdentifier}'. dotnet output:\n{publish.Output}",
                    "CLI022");
                return result;
            }

            var extension = runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
                ? ".exe"
                : string.Empty;
            var binaryPath = Path.Combine(outputDirectory, assemblyName + extension);
            if (!File.Exists(binaryPath))
            {
                result.Diagnostics.Report(
                    Span.Empty,
                    $"native AOT publish completed but binary was not found at '{binaryPath}'",
                    "CLI022");
                return result;
            }

            result.Published = true;
            result.BinaryPath = binaryPath;
            return result;
        }
        finally
        {
            if (Directory.Exists(hostProjectDirectory))
            {
                Directory.Delete(hostProjectDirectory, recursive: true);
            }
        }
    }

    private static (bool Succeeded, string Output) ExecuteDotnetPublish(
        string hostProjectPath,
        string outputDirectory,
        string runtimeIdentifier,
        string configuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(hostProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(runtimeIdentifier);
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("/p:PublishAot=true");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);

        using var process = Process.Start(startInfo)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            output.AppendLine(stdOut.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            output.AppendLine(stdErr.TrimEnd());
        }

        return (process.ExitCode == 0, output.ToString().Trim());
    }

    private static string BuildHostProject(string assemblyPath, string kongAssemblyPath, string assemblyName)
    {
        var assemblyHintPath = EscapeForXml(assemblyPath);
        var kongHintPath = EscapeForXml(kongAssemblyPath);
        var escapedAssemblyName = EscapeForXml(assemblyName);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AssemblyName>{{escapedAssemblyName}}</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="Kong.Generated">
              <HintPath>{{assemblyHintPath}}</HintPath>
            </Reference>
            <Reference Include="Kong">
              <HintPath>{{kongHintPath}}</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """;
    }

    private static string BuildHostProgram()
    {
        return """
        return Kong.Generated.Program.__KongEntryPoint();
        """;
    }

    private static string EscapeForXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
