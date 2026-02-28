using Mono.Cecil;
using Kong.Parsing;
using Kong.Semantics;

namespace Kong.CodeGeneration;

public class ClrArtifactBuilder
{
    public string? Build(Program program, string assemblyName, string outputAssembly)
    {
        var (assembly, module, mainMethod) = CreateProgramScaffold(assemblyName);

        var compileErr = CompileMainBody(program, module, mainMethod);
        if (compileErr is not null)
        {
            return compileErr;
        }

        WriteArtifacts(assembly, outputAssembly);
        return null;
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

    private static string? CompileMainBody(Program program, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var inferer = new TypeInferer();

        var annotationErrors = inferer.ValidateFunctionTypeAnnotations(program);
        if (annotationErrors.Count > 0)
        {
            return string.Join(Environment.NewLine, annotationErrors);
        }

        var types = inferer.InferTypes(program);
        var typeErrors = types.GetErrors().ToList();
        if (typeErrors.Count > 0)
        {
            return string.Join(Environment.NewLine, typeErrors);
        }

        var ilCompiler = new IlCompiler();
        return ilCompiler.CompileProgramToMain(program, types, module, mainMethod);
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
