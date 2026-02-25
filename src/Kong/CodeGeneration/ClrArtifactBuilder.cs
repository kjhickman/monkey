using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class ClrArtifactBuilder
{
    public void Build(string assemblyName, string outputAssembly)
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

        var writeLine = module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!);
        var il = mainMethod.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "Hello world");
        il.Emit(OpCodes.Call, writeLine);
        il.Emit(OpCodes.Ret);

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
