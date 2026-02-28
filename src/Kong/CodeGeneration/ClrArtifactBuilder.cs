using Mono.Cecil;
using Kong.Diagnostics;
using Kong.Lowering;

namespace Kong.CodeGeneration;

public class ClrArtifactBuilder
{
    public CodegenResult Build(LoweringResult loweringResult, string assemblyName, string outputAssembly)
    {
        var diagnostics = new DiagnosticBag();
        var (assembly, module, mainMethod) = CreateProgramScaffold(assemblyName);

        var compileErr = CompileMainBody(loweringResult, module, mainMethod);
        if (compileErr is not null)
        {
            diagnostics.Add(CompilationStage.CodeGeneration, compileErr);
            return new CodegenResult(outputAssembly, diagnostics);
        }

        WriteArtifacts(assembly, outputAssembly);
        return new CodegenResult(outputAssembly, diagnostics);
    }

    private static (AssemblyDefinition Assembly, ModuleDefinition Module, MethodDefinition MainMethod) CreateProgramScaffold(string assemblyName)
    {
        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)),
            assemblyName,
            ModuleKind.Console
        );

        var module = assembly.MainModule;

        var programType = new TypeDefinition(
            string.Empty,
            "Program",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
            module.TypeSystem.Object
        );
        module.Types.Add(programType);

        var mainMethod = new MethodDefinition(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void
        );
        mainMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, new ArrayType(module.TypeSystem.String)));
        programType.Methods.Add(mainMethod);
        module.EntryPoint = mainMethod;

        return (assembly, module, mainMethod);
    }

    private static string? CompileMainBody(LoweringResult loweringResult, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var emitter = new ClrEmitter();
        return emitter.CompileProgramToMain(loweringResult.BoundProgram, module, mainMethod);
    }

    private static void WriteArtifacts(AssemblyDefinition assembly, string outputAssembly)
    {
        var outputDirectory = Path.GetDirectoryName(outputAssembly);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        assembly.Write(outputAssembly);
        WriteRuntimeConfig(outputAssembly);
    }

    private static void WriteRuntimeConfig(string outputAssembly)
    {
        var runtimeConfigPath = Path.ChangeExtension(outputAssembly, "runtimeconfig.json");
        var runtimeVersion = Environment.Version;

        var contents = $$"""
        {
          "runtimeOptions": {
            "tfm": "net{{runtimeVersion.Major}}.{{runtimeVersion.Minor}}",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "{{runtimeVersion.Major}}.{{runtimeVersion.Minor}}.0"
            }
          }
        }
        """;

        File.WriteAllText(runtimeConfigPath, contents);
    }
}
