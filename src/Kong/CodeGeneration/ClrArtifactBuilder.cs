using Mono.Cecil;
using Mono.Cecil.Cil;
using Kong.Common;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Semantic.TypeMapping;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilParameterAttributes = Mono.Cecil.ParameterAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodImplAttributes = Mono.Cecil.MethodImplAttributes;

namespace Kong.CodeGeneration;

public class ClrArtifactBuildResult
{
    public bool Built { get; set; }
    public bool IsUnsupported { get; set; }
    public string? AssemblyPath { get; set; }
    public string? RuntimeConfigPath { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}

public class ClrArtifactBuilder
{
    private sealed record DisplayClassInfo(
        TypeDefinition Type,
        MethodDefinition InvokeMethod,
        IReadOnlyList<FieldDefinition> CaptureFields,
        TypeReference DelegateType,
        MethodReference DelegateCtor);

    private sealed record EnumRuntimeInfo(
        TypeDefinition Type,
        MethodDefinition Constructor,
        FieldDefinition TagField,
        IReadOnlyList<FieldDefinition> PayloadFields);

    private sealed record ClassRuntimeInfo(
        TypeDefinition Type,
        MethodDefinition Constructor,
        ClassDefinitionSymbol Definition,
        IReadOnlyDictionary<string, FieldDefinition> Fields);

    private sealed record InterfaceRuntimeInfo(
        TypeDefinition Type,
        IReadOnlyDictionary<string, MethodDefinition> Methods,
        InterfaceDefinitionSymbol Definition);

    public ClrArtifactBuildResult BuildArtifact(
        CompilationUnit unit,
        TypeCheckResult typeCheckResult,
        string outputDirectory,
        string assemblyName,
        NameResolution? nameResolution = null)
    {
        var result = new ClrArtifactBuildResult();

        var lowerer = new IrLowerer();
        var loweringResult = lowerer.Lower(unit, typeCheckResult, nameResolution);
        result.Diagnostics.AddRange(loweringResult.Diagnostics);
        if (loweringResult.Program == null)
        {
            result.IsUnsupported = result.Diagnostics.All.Count > 0 && result.Diagnostics.All.All(d => d.Code == "IR001");
            return result;
        }

        var assemblyBytes = EmitAssembly(loweringResult.Program, result.Diagnostics);
        if (assemblyBytes == null)
        {
            result.IsUnsupported = result.Diagnostics.All.All(d => d.Code == "IL001");
            return result;
        }

        Directory.CreateDirectory(outputDirectory);
        var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
        File.WriteAllBytes(assemblyPath, assemblyBytes);

        var runtimeConfigPath = Path.Combine(outputDirectory, $"{assemblyName}.runtimeconfig.json");
        var runtimeConfigJson = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "10.0.0"
            }
          }
        }
        """;
        File.WriteAllText(runtimeConfigPath, runtimeConfigJson + Environment.NewLine);

        var kongAssemblyFileName = $"{typeof(ClrArtifactBuilder).Assembly.GetName().Name}.dll";
        var kongAssemblyPath = Path.Combine(AppContext.BaseDirectory, kongAssemblyFileName);
        if (File.Exists(kongAssemblyPath))
        {
            var kongDestination = Path.Combine(outputDirectory, Path.GetFileName(kongAssemblyPath));
            File.Copy(kongAssemblyPath, kongDestination, overwrite: true);
        }

        result.Built = true;
        result.AssemblyPath = assemblyPath;
        result.RuntimeConfigPath = runtimeConfigPath;
        return result;
    }

    public ClrArtifactBuildResult BuildArtifact(
        IReadOnlyList<ModuleAnalysis> modules,
        string outputDirectory,
        string assemblyName)
    {
        var result = new ClrArtifactBuildResult();
        if (modules.Count == 0)
        {
            result.Diagnostics.Report(Span.Empty, "no modules were provided for CLR artifact build", "IL001");
            return result;
        }

        var rootModule = modules.FirstOrDefault(m => m.IsRootModule);
        if (rootModule == null)
        {
            result.Diagnostics.Report(Span.Empty, "no root module provided for CLR artifact build", "IL001");
            return result;
        }

        var lowerer = new IrLowerer();
        var loweredPrograms = new List<(ModuleAnalysis Module, IrProgram Program)>(modules.Count);

        foreach (var module in modules)
        {
            var loweringResult = lowerer.Lower(module.Unit, module.TypeCheck, module.NameResolution);
            result.Diagnostics.AddRange(loweringResult.Diagnostics);
            if (loweringResult.Program == null)
            {
                result.IsUnsupported = result.Diagnostics.All.Count > 0 && result.Diagnostics.All.All(d => d.Code == "IR001");
                return result;
            }

            loweredPrograms.Add((module, loweringResult.Program));

        }

        var rootLowered = loweredPrograms.FirstOrDefault(p => p.Module.IsRootModule);
        if (rootLowered == default)
        {
            result.Diagnostics.Report(Span.Empty, "no lowered root program generated", "IL001");
            return result;
        }

        var combinedProgram = new IrProgram
        {
            EntryPoint = rootLowered.Program.EntryPoint,
        };

        var nonRootIndex = 0;
        foreach (var (module, program) in loweredPrograms)
        {
            foreach (var pair in program.EnumDefinitions)
            {
                combinedProgram.EnumDefinitions[pair.Key] = pair.Value;
            }

            foreach (var pair in program.ClassDefinitions)
            {
                combinedProgram.ClassDefinitions[pair.Key] = pair.Value;
            }

            foreach (var pair in program.InterfaceDefinitions)
            {
                combinedProgram.InterfaceDefinitions[pair.Key] = pair.Value;
            }

            var functions = program.Functions;
            if (!module.IsRootModule)
            {
                PrefixSyntheticFunctionNames(functions, nonRootIndex);
                nonRootIndex++;
            }

            foreach (var function in functions)
            {
                combinedProgram.Functions.Add(function);
            }
        }

        var assemblyBytes = EmitAssembly(combinedProgram, result.Diagnostics);
        if (assemblyBytes == null)
        {
            result.IsUnsupported = result.Diagnostics.All.All(d => d.Code == "IL001");
            return result;
        }

        Directory.CreateDirectory(outputDirectory);
        var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
        File.WriteAllBytes(assemblyPath, assemblyBytes);

        var runtimeConfigPath = Path.Combine(outputDirectory, $"{assemblyName}.runtimeconfig.json");
        var runtimeConfigJson = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "10.0.0"
            }
          }
        }
        """;
        File.WriteAllText(runtimeConfigPath, runtimeConfigJson + Environment.NewLine);

        var kongAssemblyFileName = $"{typeof(ClrArtifactBuilder).Assembly.GetName().Name}.dll";
        var kongAssemblyPath = Path.Combine(AppContext.BaseDirectory, kongAssemblyFileName);
        if (File.Exists(kongAssemblyPath))
        {
            var kongDestination = Path.Combine(outputDirectory, Path.GetFileName(kongAssemblyPath));
            File.Copy(kongAssemblyPath, kongDestination, overwrite: true);
        }

        result.Built = true;
        result.AssemblyPath = assemblyPath;
        result.RuntimeConfigPath = runtimeConfigPath;
        return result;
    }

    private static void PrefixSyntheticFunctionNames(IReadOnlyList<IrFunction> functions, int moduleIndex)
    {
        var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var function in functions)
        {
            if (!function.Name.StartsWith("__lambda", StringComparison.Ordinal))
            {
                continue;
            }

            var newName = $"__m{moduleIndex}_{function.Name}";
            renameMap[function.Name] = newName;
            function.Name = newName;
        }

        if (renameMap.Count == 0)
        {
            return;
        }

        foreach (var function in functions)
        {
            foreach (var block in function.Blocks)
            {
                for (var i = 0; i < block.Instructions.Count; i++)
                {
                    if (block.Instructions[i] is IrCreateClosure createClosure &&
                        renameMap.TryGetValue(createClosure.FunctionName, out var renamed))
                    {
                        block.Instructions[i] = createClosure with { FunctionName = renamed };
                    }
                }
            }
        }
    }

    private static Dictionary<string, EnumRuntimeInfo>? BuildEnumRuntimeMap(
        IReadOnlyDictionary<string, EnumDefinitionSymbol> enumDefinitions,
        ModuleDefinition module,
        TypeDefinition programType,
        DiagnosticBag diagnostics)
    {
        var map = new Dictionary<string, EnumRuntimeInfo>(StringComparer.Ordinal);
        foreach (var pair in enumDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var enumDefinition = pair.Value;
            var enumType = new TypeDefinition(
                "Kong.Generated",
                $"__enum_{enumDefinition.Name}",
                CecilTypeAttributes.NestedPublic | CecilTypeAttributes.Class | CecilTypeAttributes.Sealed,
                module.TypeSystem.Object);
            programType.NestedTypes.Add(enumType);

            var ctor = new MethodDefinition(
                ".ctor",
                CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.SpecialName | CecilMethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            enumType.Methods.Add(ctor);

            var tagField = new FieldDefinition("__tag", CecilFieldAttributes.Public, module.TypeSystem.Int32);
            enumType.Fields.Add(tagField);

            var payloadFields = new List<FieldDefinition>(enumDefinition.MaxPayloadArity);
            for (var i = 0; i < enumDefinition.MaxPayloadArity; i++)
            {
                var payloadField = new FieldDefinition($"__value{i}", CecilFieldAttributes.Public, module.TypeSystem.Object);
                enumType.Fields.Add(payloadField);
                payloadFields.Add(payloadField);
            }

            var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
            var ctorIl = ctor.Body.GetILProcessor();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, objectCtor);
            ctorIl.Emit(OpCodes.Ret);

            map[enumDefinition.Name] = new EnumRuntimeInfo(enumType, ctor, tagField, payloadFields);
        }

        return map;
    }

    private static Dictionary<string, ClassRuntimeInfo>? BuildClassRuntimeMap(
        IReadOnlyDictionary<string, ClassDefinitionSymbol> classDefinitions,
        ModuleDefinition module,
        TypeDefinition programType,
        IReadOnlyDictionary<string, TypeDefinition> enumTypeMap,
        DiagnosticBag diagnostics)
    {
        var map = new Dictionary<string, ClassRuntimeInfo>(StringComparer.Ordinal);
        foreach (var pair in classDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var classDefinition = pair.Value;
            var type = new TypeDefinition(
                "Kong.Generated",
                $"__class_{classDefinition.Name}",
                CecilTypeAttributes.NestedPublic | CecilTypeAttributes.Class | CecilTypeAttributes.Sealed,
                module.TypeSystem.Object);
            programType.NestedTypes.Add(type);

            var ctor = new MethodDefinition(
                ".ctor",
                CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.SpecialName | CecilMethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            type.Methods.Add(ctor);
            var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
            var ctorIl = ctor.Body.GetILProcessor();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, objectCtor);
            ctorIl.Emit(OpCodes.Ret);

            map[classDefinition.Name] = new ClassRuntimeInfo(type, ctor, classDefinition, new Dictionary<string, FieldDefinition>(StringComparer.Ordinal));
        }

        var classTypeMap = map.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var fieldTypeMapper = new DefaultTypeMapper(
            new Dictionary<string, TypeDefinition>(StringComparer.Ordinal),
            enumTypeMap,
            classTypeMap);

        foreach (var pair in classDefinitions)
        {
            var classDefinition = pair.Value;
            var runtime = map[classDefinition.Name];
            var fields = (Dictionary<string, FieldDefinition>)runtime.Fields;
            foreach (var fieldPair in classDefinition.Fields)
            {
                var fieldType = MapType(fieldPair.Value, module, diagnostics, fieldTypeMapper);
                if (fieldType == null)
                {
                    diagnostics.Report(Span.Empty,
                        $"phase-5 CLR backend could not map field type '{fieldPair.Value}' for class '{classDefinition.Name}'",
                        "IL001");
                    return null;
                }

                var field = new FieldDefinition(fieldPair.Key, CecilFieldAttributes.Public, fieldType);
                runtime.Type.Fields.Add(field);
                fields[fieldPair.Key] = field;
            }
        }

        return map;
    }

    private static Dictionary<string, InterfaceRuntimeInfo>? BuildInterfaceRuntimeMap(
        IReadOnlyDictionary<string, InterfaceDefinitionSymbol> interfaceDefinitions,
        ModuleDefinition module,
        TypeDefinition programType,
        IReadOnlyDictionary<string, TypeDefinition> enumTypeMap,
        IReadOnlyDictionary<string, TypeDefinition> classTypeMap,
        DiagnosticBag diagnostics)
    {
        var map = new Dictionary<string, InterfaceRuntimeInfo>(StringComparer.Ordinal);
        foreach (var pair in interfaceDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var interfaceDefinition = pair.Value;
            var type = new TypeDefinition(
                "Kong.Generated",
                $"__iface_{interfaceDefinition.Name}",
                CecilTypeAttributes.NestedPublic | CecilTypeAttributes.Interface | CecilTypeAttributes.Abstract,
                null!);
            programType.NestedTypes.Add(type);
            map[interfaceDefinition.Name] = new InterfaceRuntimeInfo(type, new Dictionary<string, MethodDefinition>(StringComparer.Ordinal), interfaceDefinition);
        }

        var interfaceTypeMap = map.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var typeMapper = new DefaultTypeMapper(
            new Dictionary<string, TypeDefinition>(StringComparer.Ordinal),
            enumTypeMap,
            classTypeMap,
            interfaceTypeMap);

        foreach (var pair in interfaceDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var interfaceDefinition = pair.Value;
            var runtime = map[interfaceDefinition.Name];
            var methods = (Dictionary<string, MethodDefinition>)runtime.Methods;
            foreach (var methodPair in interfaceDefinition.Methods.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var signature = methodPair.Value;
                var returnType = MapType(signature.ReturnType, module, diagnostics, typeMapper);
                if (returnType == null)
                {
                    diagnostics.Report(
                        Span.Empty,
                        $"phase-5 CLR backend could not map return type '{signature.ReturnType}' for interface method '{interfaceDefinition.Name}.{signature.Name}'",
                        "IL001");
                    return null;
                }

                var method = new MethodDefinition(
                    signature.Name,
                    CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.NewSlot | CecilMethodAttributes.Virtual | CecilMethodAttributes.Abstract,
                    returnType);
                foreach (var parameterTypeSymbol in signature.ParameterTypes)
                {
                    var parameterType = MapType(parameterTypeSymbol, module, diagnostics, typeMapper);
                    if (parameterType == null)
                    {
                        diagnostics.Report(
                            Span.Empty,
                            $"phase-5 CLR backend could not map parameter type '{parameterTypeSymbol}' for interface method '{interfaceDefinition.Name}.{signature.Name}'",
                            "IL001");
                        return null;
                    }

                    method.Parameters.Add(new ParameterDefinition(parameterType));
                }

                runtime.Type.Methods.Add(method);
                methods[signature.Name] = method;
            }
        }

        return map;
    }

    private static byte[]? EmitAssembly(IrProgram program, DiagnosticBag diagnostics)
    {
        var assemblyName = new AssemblyNameDefinition("Kong.Generated", new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, "Kong.Generated", ModuleKind.Console);
        var module = assembly.MainModule;

        var programType = new TypeDefinition(
            "Kong.Generated",
            "Program",
            CecilTypeAttributes.Public | CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(programType);

        var allFunctions = new List<IrFunction> { program.EntryPoint };
        allFunctions.AddRange(program.Functions);
        var functionMap = allFunctions.ToDictionary(f => f.Name, f => f);
        var enumRuntimeMap = BuildEnumRuntimeMap(program.EnumDefinitions, module, programType, diagnostics);
        if (enumRuntimeMap == null)
        {
            return null;
        }

        var enumTypeMap = enumRuntimeMap.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var classRuntimeMap = BuildClassRuntimeMap(program.ClassDefinitions, module, programType, enumTypeMap, diagnostics);
        if (classRuntimeMap == null)
        {
            return null;
        }

        var classTypeMap = classRuntimeMap.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var interfaceRuntimeMap = BuildInterfaceRuntimeMap(program.InterfaceDefinitions, module, programType, enumTypeMap, classTypeMap, diagnostics);
        if (interfaceRuntimeMap == null)
        {
            return null;
        }

        var interfaceTypeMap = interfaceRuntimeMap.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var delegateTypeMap = BuildDelegateTypeMap(allFunctions, module, programType, diagnostics, enumTypeMap, classTypeMap, interfaceTypeMap);
        if (delegateTypeMap == null)
        {
            return null;
        }
        var typeMapper = new DefaultTypeMapper(delegateTypeMap, enumTypeMap, classTypeMap, interfaceTypeMap);

        var methodMap = new Dictionary<string, MethodDefinition>();
        foreach (var function in allFunctions)
        {
            var returnType = MapType(function.ReturnType, module, diagnostics, typeMapper);
            if (returnType == null)
            {
                return null;
            }

            var methodName = function == program.EntryPoint ? "Eval" : function.Name;
            var attributes = function == program.EntryPoint
                ? CecilMethodAttributes.Public | CecilMethodAttributes.Static
                : CecilMethodAttributes.Private | CecilMethodAttributes.Static;

            var method = new MethodDefinition(methodName, attributes, returnType);
            foreach (var parameter in function.Parameters)
            {
                var parameterType = MapType(parameter.Type, module, diagnostics, typeMapper);
                if (parameterType == null)
                {
                    return null;
                }

                method.Parameters.Add(new ParameterDefinition(parameter.Name, CecilParameterAttributes.None, parameterType));
            }

            programType.Methods.Add(method);
            methodMap[function.Name] = method;
        }

        if (!BuildClassMethodRuntimeSurface(classRuntimeMap, interfaceRuntimeMap, methodMap, module, diagnostics, typeMapper))
        {
            return null;
        }

        var mainMethod = new MethodDefinition(
            "__KongEntryPoint",
            CecilMethodAttributes.Public | CecilMethodAttributes.Static,
            module.TypeSystem.Int32);
        programType.Methods.Add(mainMethod);
        var mainIl = mainMethod.Body.GetILProcessor();

        if (methodMap.TryGetValue("Main", out var userMain))
        {
            mainIl.Emit(OpCodes.Call, userMain);
            if (userMain.ReturnType == module.TypeSystem.Void)
            {
                mainIl.Emit(OpCodes.Ldc_I4_0);
            }
            else
            {
                mainIl.Emit(OpCodes.Conv_I4);
            }
        }
        else
        {
            var writeLineInt = module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(int)])!);
            mainIl.Emit(OpCodes.Call, methodMap[program.EntryPoint.Name]);
            mainIl.Emit(OpCodes.Call, writeLineInt);
            mainIl.Emit(OpCodes.Ldc_I4_0);
        }

        mainIl.Emit(OpCodes.Ret);
        module.EntryPoint = mainMethod;

        var displayClassMap = new Dictionary<string, DisplayClassInfo>();
        foreach (var function in allFunctions.Where(f => f.CaptureParameterCount > 0))
        {
            var displayClass = BuildDisplayClass(function, methodMap, module, programType, diagnostics, typeMapper);
            if (displayClass == null)
            {
                return null;
            }

            displayClassMap[function.Name] = displayClass;
        }

        foreach (var function in allFunctions)
        {
            if (!EmitFunction(function, methodMap[function.Name], methodMap, functionMap, displayClassMap, enumRuntimeMap, classRuntimeMap, interfaceRuntimeMap, module, diagnostics, delegateTypeMap, typeMapper))
            {
                return null;
            }
        }

        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    private static bool BuildClassMethodRuntimeSurface(
        IReadOnlyDictionary<string, ClassRuntimeInfo> classRuntimeMap,
        IReadOnlyDictionary<string, InterfaceRuntimeInfo> interfaceRuntimeMap,
        IReadOnlyDictionary<string, MethodDefinition> methodMap,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        foreach (var pair in classRuntimeMap.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var classRuntime = pair.Value;
            var classMethods = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);

            foreach (var methodPair in classRuntime.Definition.Methods.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var methodSignature = methodPair.Value;
                if (!TryResolveClassMethodHelperMethod(classRuntime.Definition.Name, methodSignature.Name, methodMap, out var helperMethod))
                {
                    diagnostics.Report(
                        Span.Empty,
                        $"phase-5 CLR backend could not resolve helper method for '{classRuntime.Definition.Name}.{methodSignature.Name}'",
                        "IL001");
                    return false;
                }

                var returnType = MapType(methodSignature.ReturnType, module, diagnostics, typeMapper);
                if (returnType == null)
                {
                    return false;
                }

                var instanceMethod = new MethodDefinition(
                    methodSignature.Name,
                    CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.Virtual | CecilMethodAttributes.Final,
                    returnType);

                foreach (var parameterTypeSymbol in methodSignature.ParameterTypes)
                {
                    var parameterType = MapType(parameterTypeSymbol, module, diagnostics, typeMapper);
                    if (parameterType == null)
                    {
                        return false;
                    }

                    instanceMethod.Parameters.Add(new ParameterDefinition(parameterType));
                }

                var il = instanceMethod.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                foreach (var parameter in instanceMethod.Parameters)
                {
                    il.Emit(OpCodes.Ldarg, parameter);
                }

                il.Emit(OpCodes.Call, helperMethod);
                il.Emit(OpCodes.Ret);

                classRuntime.Type.Methods.Add(instanceMethod);
                classMethods[methodSignature.Name] = instanceMethod;
            }

            foreach (var interfaceName in classRuntime.Definition.ImplementedInterfaces.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!interfaceRuntimeMap.TryGetValue(interfaceName, out var interfaceRuntime))
                {
                    diagnostics.Report(
                        Span.Empty,
                        $"phase-5 CLR backend could not resolve generated interface '{interfaceName}' for class '{classRuntime.Definition.Name}'",
                        "IL001");
                    return false;
                }

                classRuntime.Type.Interfaces.Add(new InterfaceImplementation(interfaceRuntime.Type));
                foreach (var interfaceMethodPair in interfaceRuntime.Definition.Methods)
                {
                    if (!classMethods.TryGetValue(interfaceMethodPair.Key, out var classMethod) ||
                        !interfaceRuntime.Methods.TryGetValue(interfaceMethodPair.Key, out var interfaceMethod))
                    {
                        diagnostics.Report(
                            Span.Empty,
                            $"phase-5 CLR backend missing implementation for interface method '{interfaceName}.{interfaceMethodPair.Key}' on class '{classRuntime.Definition.Name}'",
                            "IL001");
                        return false;
                    }

                    _ = classMethod;
                    _ = interfaceMethod;
                }
            }
        }

        return true;
    }

    private static bool TryResolveClassMethodHelperMethod(
        string className,
        string memberName,
        IReadOnlyDictionary<string, MethodDefinition> methodMap,
        out MethodDefinition helperMethod)
    {
        var directName = $"__method_{className}_{memberName}";
        if (methodMap.TryGetValue(directName, out helperMethod!))
        {
            return true;
        }

        var interfaceName = $"__method_{className}_{memberName}_iface";
        return methodMap.TryGetValue(interfaceName, out helperMethod!);
    }

    private static bool EmitFunction(
        IrFunction function,
        MethodDefinition method,
        IReadOnlyDictionary<string, MethodDefinition> methodMap,
        IReadOnlyDictionary<string, IrFunction> functionMap,
        IReadOnlyDictionary<string, DisplayClassInfo> displayClassMap,
        IReadOnlyDictionary<string, EnumRuntimeInfo> enumRuntimeMap,
        IReadOnlyDictionary<string, ClassRuntimeInfo> classRuntimeMap,
        IReadOnlyDictionary<string, InterfaceRuntimeInfo> interfaceRuntimeMap,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap,
        ITypeMapper typeMapper)
    {
        method.Body.InitLocals = true;

        var valueLocals = new Dictionary<IrValueId, VariableDefinition>();
        foreach (var (valueId, type) in function.ValueTypes)
        {
            var variableType = MapType(type, module, diagnostics, typeMapper);
            if (variableType == null)
            {
                return false;
            }

            valueLocals[valueId] = new VariableDefinition(variableType);
            method.Body.Variables.Add(valueLocals[valueId]);
        }

        var parameterLocalIndexes = new Dictionary<IrLocalId, int>();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            parameterLocalIndexes[function.Parameters[i].LocalId] = i;
        }

        var localVariables = new Dictionary<IrLocalId, VariableDefinition>();
        foreach (var (localId, type) in function.LocalTypes)
        {
            if (parameterLocalIndexes.ContainsKey(localId))
            {
                continue;
            }

            var variableType = MapType(type, module, diagnostics, typeMapper);
            if (variableType == null)
            {
                return false;
            }

            localVariables[localId] = new VariableDefinition(variableType);
            method.Body.Variables.Add(localVariables[localId]);
        }

        var il = method.Body.GetILProcessor();
        var labels = new Dictionary<int, Instruction>();
        foreach (var block in function.Blocks)
        {
            labels[block.Id] = Instruction.Create(OpCodes.Nop);
        }

        if (function.Blocks.Count == 0)
        {
            diagnostics.Report(Span.Empty, "phase-4 CLR backend requires at least one IR block", "IL001");
            return false;
        }

        il.Emit(OpCodes.Br, labels[function.Blocks[0].Id]);

        foreach (var block in function.Blocks)
        {
            il.Append(labels[block.Id]);

            foreach (var instruction in block.Instructions)
            {
                switch (instruction)
                {
                    case IrConstInt constInt:
                        if (!function.ValueTypes.TryGetValue(constInt.Destination, out var constType))
                        {
                            diagnostics.Report(Span.Empty, "phase-5 CLR backend missing type for integer constant", "IL001");
                            return false;
                        }

                        if (constType == TypeSymbols.Int)
                        {
                            il.Emit(OpCodes.Ldc_I4, checked((int)constInt.Value));
                        }
                        else if (constType == TypeSymbols.Byte)
                        {
                            il.Emit(OpCodes.Ldc_I4, checked((int)constInt.Value));
                            il.Emit(OpCodes.Conv_U1);
                        }
                        else if (constType == TypeSymbols.Char)
                        {
                            il.Emit(OpCodes.Ldc_I4, checked((int)constInt.Value));
                            il.Emit(OpCodes.Conv_U2);
                        }
                        else if (constType == TypeSymbols.Long)
                        {
                            il.Emit(OpCodes.Ldc_I8, constInt.Value);
                        }
                        else
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend invalid integer constant type '{constType}'", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[constInt.Destination]);
                        break;

                    case IrConstDouble constDouble:
                        il.Emit(OpCodes.Ldc_R8, constDouble.Value);
                        il.Emit(OpCodes.Stloc, valueLocals[constDouble.Destination]);
                        break;

                    case IrConstBool constBool:
                        il.Emit(constBool.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Stloc, valueLocals[constBool.Destination]);
                        break;

                    case IrConstString constString:
                        il.Emit(OpCodes.Ldstr, constString.Value);
                        il.Emit(OpCodes.Stloc, valueLocals[constString.Destination]);
                        break;

                    case IrBinary binary:
                        if (!function.ValueTypes.TryGetValue(binary.Left, out var leftOperandType) ||
                            !function.ValueTypes.TryGetValue(binary.Right, out var rightOperandType))
                        {
                            diagnostics.Report(Span.Empty, "phase-5 CLR backend missing operand types for binary expression", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[binary.Left]);
                        il.Emit(OpCodes.Ldloc, valueLocals[binary.Right]);
                        switch (binary.Operator)
                        {
                            case IrBinaryOperator.Add:
                                il.Emit(OpCodes.Add);
                                break;
                            case IrBinaryOperator.Subtract:
                                il.Emit(OpCodes.Sub);
                                break;
                            case IrBinaryOperator.Multiply:
                                il.Emit(OpCodes.Mul);
                                break;
                            case IrBinaryOperator.Divide:
                                il.Emit(OpCodes.Div);
                                break;
                            case IrBinaryOperator.LessThan:
                                il.Emit(OpCodes.Clt);
                                break;
                            case IrBinaryOperator.GreaterThan:
                                il.Emit(OpCodes.Cgt);
                                break;
                            case IrBinaryOperator.Equal:
                                if (leftOperandType == TypeSymbols.String && rightOperandType == TypeSymbols.String)
                                {
                                    var stringEquals = module.ImportReference(typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
                                    il.Emit(OpCodes.Call, stringEquals);
                                }
                                else
                                {
                                    il.Emit(OpCodes.Ceq);
                                }
                                break;
                            case IrBinaryOperator.NotEqual:
                                if (leftOperandType == TypeSymbols.String && rightOperandType == TypeSymbols.String)
                                {
                                    var stringNotEquals = module.ImportReference(typeof(string).GetMethod("op_Inequality", [typeof(string), typeof(string)])!);
                                    il.Emit(OpCodes.Call, stringNotEquals);
                                }
                                else
                                {
                                    il.Emit(OpCodes.Ceq);
                                    il.Emit(OpCodes.Ldc_I4_0);
                                    il.Emit(OpCodes.Ceq);
                                }
                                break;
                            default:
                                throw new InvalidOperationException();
                        }
                        il.Emit(OpCodes.Stloc, valueLocals[binary.Destination]);
                        break;

                    case IrStoreLocal storeLocal:
                        il.Emit(OpCodes.Ldloc, valueLocals[storeLocal.Source]);
                        if (parameterLocalIndexes.TryGetValue(storeLocal.Local, out var parameterIndex))
                        {
                            il.Emit(OpCodes.Starg, method.Parameters[parameterIndex]);
                        }
                        else
                        {
                            il.Emit(OpCodes.Stloc, localVariables[storeLocal.Local]);
                        }
                        break;

                    case IrLoadLocal loadLocal:
                        if (parameterLocalIndexes.TryGetValue(loadLocal.Local, out var loadParameterIndex))
                        {
                            il.Emit(OpCodes.Ldarg, method.Parameters[loadParameterIndex]);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldloc, localVariables[loadLocal.Local]);
                        }
                        il.Emit(OpCodes.Stloc, valueLocals[loadLocal.Destination]);
                        break;

                    case IrCall call:
                    {
                        if (methodMap.TryGetValue(call.FunctionName, out var targetMethod) &&
                            functionMap.TryGetValue(call.FunctionName, out var calledFunction) &&
                            function.ValueTypes.TryGetValue(call.Destination, out var callDestinationType))
                        {
                            if (calledFunction.Parameters.Count != call.Arguments.Count)
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend argument count mismatch for function '{call.FunctionName}'",
                                    "IL001");
                                return false;
                            }

                            for (var i = 0; i < call.Arguments.Count; i++)
                            {
                                if (!function.ValueTypes.TryGetValue(call.Arguments[i], out var sourceType))
                                {
                                    diagnostics.Report(Span.Empty,
                                        "phase-5 CLR backend missing source type for function call argument",
                                        "IL001");
                                    return false;
                                }

                                EmitArgumentWithConversion(il, valueLocals[call.Arguments[i]], sourceType, calledFunction.Parameters[i].Type, module, diagnostics, typeMapper);
                            }

                            il.Emit(OpCodes.Call, targetMethod);
                            if (!EmitClassFieldLoadConversion(calledFunction.ReturnType, callDestinationType, il, module, diagnostics, typeMapper))
                            {
                                return false;
                            }
                            il.Emit(OpCodes.Stloc, valueLocals[call.Destination]);
                            break;
                        }

                        diagnostics.Report(Span.Empty, $"phase-5 CLR backend could not resolve function '{call.FunctionName}'", "IL001");
                        return false;
                    }

                    case IrStaticCall staticCall:
                    {
                        if (!function.ValueTypes.TryGetValue(staticCall.Destination, out var staticReturnType))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend missing return type for static call '{staticCall.MethodPath}'", "IL001");
                            return false;
                        }

                        if (!StaticClrMethodResolver.TryResolve(
                                staticCall.MethodPath,
                                BuildClrArguments(staticCall.ArgumentTypes, staticCall.ArgumentModifiers),
                                out var binding,
                                out _,
                                out var resolveError))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveError}", "IL001");
                            return false;
                        }

                        if (binding.ReturnType != staticReturnType)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend expected static call '{staticCall.MethodPath}' to return '{staticReturnType}', but resolver returned '{binding.ReturnType}'",
                                "IL001");
                            return false;
                        }

                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, localVariables, parameterLocalIndexes, staticCall.Arguments, staticCall.ArgumentTypes, staticCall.ArgumentModifiers, staticCall.ByRefLocals, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var staticMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Call, staticMethod);
                        il.Emit(OpCodes.Stloc, valueLocals[staticCall.Destination]);
                        break;
                    }

                    case IrCallVoid callVoid:
                        if (methodMap.TryGetValue(callVoid.FunctionName, out var targetVoidMethod) &&
                            functionMap.TryGetValue(callVoid.FunctionName, out var calledVoidFunction))
                        {
                            if (calledVoidFunction.Parameters.Count != callVoid.Arguments.Count)
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend argument count mismatch for function '{callVoid.FunctionName}'",
                                    "IL001");
                                return false;
                            }

                            for (var i = 0; i < callVoid.Arguments.Count; i++)
                            {
                                if (!function.ValueTypes.TryGetValue(callVoid.Arguments[i], out var sourceType))
                                {
                                    diagnostics.Report(Span.Empty,
                                        "phase-5 CLR backend missing source type for function call argument",
                                        "IL001");
                                    return false;
                                }

                                EmitArgumentWithConversion(il, valueLocals[callVoid.Arguments[i]], sourceType, calledVoidFunction.Parameters[i].Type, module, diagnostics, typeMapper);
                            }

                            il.Emit(OpCodes.Call, targetVoidMethod);
                            break;
                        }

                        diagnostics.Report(Span.Empty, $"phase-6 CLR backend could not resolve function '{callVoid.FunctionName}'", "IL001");
                        return false;

                    case IrStaticCallVoid staticCallVoid:
                    {
                        if (!StaticClrMethodResolver.TryResolve(
                                staticCallVoid.MethodPath,
                                BuildClrArguments(staticCallVoid.ArgumentTypes, staticCallVoid.ArgumentModifiers),
                                out var binding,
                                out _,
                                out var resolveError))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveError}", "IL001");
                            return false;
                        }

                        if (binding.ReturnType != TypeSymbols.Void)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend expected static call '{staticCallVoid.MethodPath}' to return 'void', but resolver returned '{binding.ReturnType}'",
                                "IL001");
                            return false;
                        }

                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, localVariables, parameterLocalIndexes, staticCallVoid.Arguments, staticCallVoid.ArgumentTypes, staticCallVoid.ArgumentModifiers, staticCallVoid.ByRefLocals, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var staticVoidMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Call, staticVoidMethod);
                        break;
                    }

                    case IrInstanceCall instanceCall:
                    {
                        if (!function.ValueTypes.TryGetValue(instanceCall.Destination, out var instanceReturnType))
                        {
                            diagnostics.Report(Span.Empty, "phase-5 CLR backend missing return type for instance call", "IL001");
                            return false;
                        }

                        if (!InstanceClrMemberResolver.TryResolveMethod(
                                instanceCall.ReceiverType,
                                instanceCall.MemberName,
                                BuildClrArguments(instanceCall.ArgumentTypes, instanceCall.ArgumentModifiers),
                                out var binding,
                                out _,
                                out var resolveMessage))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveMessage}", "IL001");
                            return false;
                        }

                        if (binding.ReturnType != instanceReturnType)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend expected instance call '{instanceCall.MemberName}' to return '{instanceReturnType}', but resolver returned '{binding.ReturnType}'",
                                "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[instanceCall.Receiver]);
                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, localVariables, parameterLocalIndexes, instanceCall.Arguments, instanceCall.ArgumentTypes, instanceCall.ArgumentModifiers, instanceCall.ByRefLocals, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var importedMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Callvirt, importedMethod);
                        il.Emit(OpCodes.Stloc, valueLocals[instanceCall.Destination]);
                        break;
                    }

                    case IrInstanceCallVoid instanceCallVoid:
                    {
                        if (!InstanceClrMemberResolver.TryResolveMethod(
                                instanceCallVoid.ReceiverType,
                                instanceCallVoid.MemberName,
                                BuildClrArguments(instanceCallVoid.ArgumentTypes, instanceCallVoid.ArgumentModifiers),
                                out var binding,
                                out _,
                                out var resolveMessage))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveMessage}", "IL001");
                            return false;
                        }

                        if (binding.ReturnType != TypeSymbols.Void)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend expected instance call '{instanceCallVoid.MemberName}' to return 'void', but resolver returned '{binding.ReturnType}'",
                                "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[instanceCallVoid.Receiver]);
                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, localVariables, parameterLocalIndexes, instanceCallVoid.Arguments, instanceCallVoid.ArgumentTypes, instanceCallVoid.ArgumentModifiers, instanceCallVoid.ByRefLocals, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var importedMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Callvirt, importedMethod);
                        break;
                    }

                    case IrInterfaceCall interfaceCall:
                    {
                        if (!function.ValueTypes.TryGetValue(interfaceCall.Destination, out var interfaceDestinationType))
                        {
                            diagnostics.Report(Span.Empty,
                                "phase-5 CLR backend missing destination type for interface call",
                                "IL001");
                            return false;
                        }

                        if (!interfaceRuntimeMap.TryGetValue(interfaceCall.ReceiverType.InterfaceName, out var interfaceRuntime) ||
                            !interfaceRuntime.Definition.Methods.TryGetValue(interfaceCall.MemberName, out var interfaceMethodDefinition))
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend missing interface method metadata '{interfaceCall.ReceiverType.InterfaceName}.{interfaceCall.MemberName}'",
                                "IL001");
                            return false;
                        }

                        var interfaceSubstitution = BuildTypeParameterSubstitution(interfaceRuntime.Definition.TypeParameters, interfaceCall.ReceiverType.TypeArguments);
                        _ = interfaceSubstitution;
                        var interfaceReturnType = interfaceMethodDefinition.ReturnType;

                        if (!EmitInterfaceCall(
                                interfaceCall.Receiver,
                                interfaceCall.ReceiverType,
                                interfaceCall.MemberName,
                                interfaceCall.Arguments,
                                valueLocals,
                                interfaceRuntimeMap,
                                il,
                                diagnostics))
                        {
                            return false;
                        }

                        if (!EmitClassFieldLoadConversion(interfaceReturnType, interfaceDestinationType, il, module, diagnostics, typeMapper))
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[interfaceCall.Destination]);
                        break;
                    }

                    case IrInterfaceCallVoid interfaceCallVoid:
                    {
                        if (!EmitInterfaceCall(
                                interfaceCallVoid.Receiver,
                                interfaceCallVoid.ReceiverType,
                                interfaceCallVoid.MemberName,
                                interfaceCallVoid.Arguments,
                                valueLocals,
                                interfaceRuntimeMap,
                                il,
                                diagnostics))
                        {
                            return false;
                        }

                        break;
                    }

                    case IrStaticValueGet staticValueGet:
                    {
                        var staticValueMember = ImportStaticValueMember(module, staticValueGet.MemberPath, diagnostics);
                        if (staticValueMember == null)
                        {
                            return false;
                        }

                        var importedMember = staticValueMember.Value;

                        if (importedMember.GetterMethod != null)
                        {
                            il.Emit(OpCodes.Call, importedMember.GetterMethod);
                        }
                        else if (importedMember.Field != null)
                        {
                            il.Emit(OpCodes.Ldsfld, importedMember.Field);
                        }
                        else
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend invalid static member binding for '{staticValueGet.MemberPath}'", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[staticValueGet.Destination]);
                        break;
                    }

                    case IrInstanceValueGet instanceValueGet:
                    {
                        if (instanceValueGet.ReceiverType is ClassTypeSymbol classReceiverType)
                        {
                            if (!classRuntimeMap.TryGetValue(classReceiverType.ClassName, out var classRuntime) ||
                                !classRuntime.Fields.TryGetValue(instanceValueGet.MemberName, out var classField) ||
                                !classRuntime.Definition.Fields.TryGetValue(instanceValueGet.MemberName, out var classFieldType))
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend could not resolve class field '{classReceiverType.ClassName}.{instanceValueGet.MemberName}'",
                                    "IL001");
                                return false;
                            }

                            if (!function.ValueTypes.TryGetValue(instanceValueGet.Destination, out var destinationType))
                            {
                                diagnostics.Report(Span.Empty,
                                    "phase-5 CLR backend missing destination type for class field load",
                                    "IL001");
                                return false;
                            }

                            il.Emit(OpCodes.Ldloc, valueLocals[instanceValueGet.Receiver]);
                            il.Emit(OpCodes.Ldfld, classField);
                            if (!EmitClassFieldLoadConversion(classFieldType, destinationType, il, module, diagnostics, typeMapper))
                            {
                                return false;
                            }
                            il.Emit(OpCodes.Stloc, valueLocals[instanceValueGet.Destination]);
                            break;
                        }

                        if (!InstanceClrMemberResolver.TryResolveValue(
                                instanceValueGet.ReceiverType,
                                instanceValueGet.MemberName,
                                out var binding,
                                out _,
                                out var resolveMessage))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveMessage}", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[instanceValueGet.Receiver]);
                        if (binding.PropertyGetter != null)
                        {
                            var getter = module.ImportReference(binding.PropertyGetter);
                            il.Emit(OpCodes.Callvirt, getter);
                        }
                        else if (binding.Field != null)
                        {
                            var field = module.ImportReference(binding.Field);
                            il.Emit(OpCodes.Ldfld, field);
                        }
                        else
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend invalid instance member binding for '{instanceValueGet.MemberName}'", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[instanceValueGet.Destination]);
                        break;
                    }

                    case IrInstanceValueSet instanceValueSet:
                    {
                        if (instanceValueSet.ReceiverType is ClassTypeSymbol classReceiverType)
                        {
                            if (!classRuntimeMap.TryGetValue(classReceiverType.ClassName, out var classRuntime) ||
                                !classRuntime.Fields.TryGetValue(instanceValueSet.MemberName, out var classField) ||
                                !classRuntime.Definition.Fields.TryGetValue(instanceValueSet.MemberName, out var classFieldType))
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend could not resolve class field '{classReceiverType.ClassName}.{instanceValueSet.MemberName}'",
                                    "IL001");
                                return false;
                            }

                            if (!function.ValueTypes.TryGetValue(instanceValueSet.Value, out var sourceValueType))
                            {
                                diagnostics.Report(Span.Empty,
                                    "phase-5 CLR backend missing source type for class field store",
                                    "IL001");
                                return false;
                            }

                            il.Emit(OpCodes.Ldloc, valueLocals[instanceValueSet.Receiver]);
                            il.Emit(OpCodes.Ldloc, valueLocals[instanceValueSet.Value]);
                            if (!EmitClassFieldStoreConversion(sourceValueType, classFieldType, il, module, diagnostics, typeMapper))
                            {
                                return false;
                            }
                            il.Emit(OpCodes.Stfld, classField);
                            break;
                        }

                        diagnostics.Report(Span.Empty,
                            "phase-5 CLR backend supports member assignment only for user classes",
                            "IL001");
                        return false;
                    }

                    case IrCreateClosure createClosure:
                        if (!functionMap.TryGetValue(createClosure.FunctionName, out var targetFunction) ||
                            !methodMap.TryGetValue(createClosure.FunctionName, out var targetClosureMethod))
                        {
                            diagnostics.Report(Span.Empty, $"phase-6 CLR backend could not resolve closure function '{createClosure.FunctionName}'", "IL001");
                            return false;
                        }

                        if (!function.ValueTypes.TryGetValue(createClosure.Destination, out var closureType) || closureType is not FunctionTypeSymbol closureFunctionType)
                        {
                            diagnostics.Report(Span.Empty, "phase-6 CLR backend requires function type for closure value", "IL001");
                            return false;
                        }

                        var delegateType = MapType(closureFunctionType, module, diagnostics, typeMapper);
                        if (delegateType == null)
                        {
                            return false;
                        }

                        var delegateCtor = BuildDelegateConstructor(delegateType, module);

                        if (targetFunction.CaptureParameterCount == 0)
                        {
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Ldftn, targetClosureMethod);
                            il.Emit(OpCodes.Newobj, delegateCtor);
                            il.Emit(OpCodes.Stloc, valueLocals[createClosure.Destination]);
                            break;
                        }

                        if (!displayClassMap.TryGetValue(createClosure.FunctionName, out var displayClassInfo))
                        {
                            diagnostics.Report(Span.Empty, $"phase-6 CLR backend missing display class for '{createClosure.FunctionName}'", "IL001");
                            return false;
                        }

                        var displayLocal = new VariableDefinition(displayClassInfo.Type);
                        method.Body.Variables.Add(displayLocal);

                        var displayCtor = displayClassInfo.Type.Methods.First(m => m.IsConstructor && !m.HasParameters);
                        il.Emit(OpCodes.Newobj, displayCtor);
                        il.Emit(OpCodes.Stloc, displayLocal);

                        if (createClosure.CapturedLocals.Count != displayClassInfo.CaptureFields.Count)
                        {
                            diagnostics.Report(Span.Empty, "phase-6 CLR backend capture count mismatch for closure", "IL001");
                            return false;
                        }

                        for (var captureIndex = 0; captureIndex < createClosure.CapturedLocals.Count; captureIndex++)
                        {
                            il.Emit(OpCodes.Ldloc, displayLocal);

                            var captureLocalId = createClosure.CapturedLocals[captureIndex];
                            if (parameterLocalIndexes.TryGetValue(captureLocalId, out var captureParameterIndex))
                            {
                                il.Emit(OpCodes.Ldarg, method.Parameters[captureParameterIndex]);
                            }
                            else
                            {
                                il.Emit(OpCodes.Ldloc, localVariables[captureLocalId]);
                            }

                            il.Emit(OpCodes.Stfld, displayClassInfo.CaptureFields[captureIndex]);
                        }

                        il.Emit(OpCodes.Ldloc, displayLocal);
                        il.Emit(OpCodes.Ldftn, displayClassInfo.InvokeMethod);
                        il.Emit(OpCodes.Newobj, displayClassInfo.DelegateCtor);
                        il.Emit(OpCodes.Stloc, valueLocals[createClosure.Destination]);
                        break;

                    case IrInvokeClosure invokeClosure:
                        if (!function.ValueTypes.TryGetValue(invokeClosure.Closure, out var invokeTargetType) ||
                            invokeTargetType is not FunctionTypeSymbol invokeFunctionType)
                        {
                            diagnostics.Report(Span.Empty, "phase-6 CLR backend requires function type for closure invoke", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[invokeClosure.Closure]);
                        foreach (var argument in invokeClosure.Arguments)
                        {
                            il.Emit(OpCodes.Ldloc, valueLocals[argument]);
                        }

                        var invokeMethod = BuildDelegateInvoke(invokeFunctionType, module, diagnostics, delegateTypeMap);
                        if (invokeMethod == null)
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Callvirt, invokeMethod);
                        il.Emit(OpCodes.Stloc, valueLocals[invokeClosure.Destination]);
                        break;

                    case IrInvokeClosureVoid invokeClosureVoid:
                        if (!function.ValueTypes.TryGetValue(invokeClosureVoid.Closure, out var invokeVoidTargetType) ||
                            invokeVoidTargetType is not FunctionTypeSymbol invokeVoidFunctionType)
                        {
                            diagnostics.Report(Span.Empty, "phase-6 CLR backend requires function type for closure invoke", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[invokeClosureVoid.Closure]);
                        foreach (var argument in invokeClosureVoid.Arguments)
                        {
                            il.Emit(OpCodes.Ldloc, valueLocals[argument]);
                        }

                        var invokeVoidMethod = BuildDelegateInvoke(invokeVoidFunctionType, module, diagnostics, delegateTypeMap);
                        if (invokeVoidMethod == null)
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Callvirt, invokeVoidMethod);
                        break;

                    case IrNewArray newArray:
                    {
                        var arrayElementClrType = MapType(newArray.ElementType, module, diagnostics, typeMapper);
                        if (arrayElementClrType == null)
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Ldc_I4, newArray.Elements.Count);
                        il.Emit(OpCodes.Newarr, arrayElementClrType);
                        for (var i = 0; i < newArray.Elements.Count; i++)
                        {
                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Ldc_I4, i);
                            il.Emit(OpCodes.Ldloc, valueLocals[newArray.Elements[i]]);
                            EmitStoreElement(il, newArray.ElementType);
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[newArray.Destination]);
                        break;
                    }

                    case IrArrayIndex arrayIndex:
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayIndex.Array]);
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayIndex.Index]);
                        il.Emit(OpCodes.Conv_I4);
                        EmitLoadElement(il, arrayIndex.ElementType);
                        il.Emit(OpCodes.Stloc, valueLocals[arrayIndex.Destination]);
                        break;

                    case IrArrayStore arrayStore:
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayStore.Array]);
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayStore.Index]);
                        il.Emit(OpCodes.Conv_I4);
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayStore.Value]);
                        EmitStoreElement(il, arrayStore.ElementType);
                        break;

                    case IrArrayLength arrayLength:
                        il.Emit(OpCodes.Ldloc, valueLocals[arrayLength.Array]);
                        il.Emit(OpCodes.Ldlen);
                        il.Emit(OpCodes.Conv_I4);
                        il.Emit(OpCodes.Stloc, valueLocals[arrayLength.Destination]);
                        break;

                    case IrNewObject newObject:
                    {
                        if (newObject.ObjectType is ClassTypeSymbol classObjectType)
                        {
                            if (!classRuntimeMap.TryGetValue(classObjectType.ClassName, out var classRuntime))
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend could not resolve class type '{classObjectType.ClassName}'",
                                    "IL001");
                                return false;
                            }

                            if (newObject.Arguments.Count != 0)
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend currently expects class allocation '{classObjectType.ClassName}' to use default constructor only",
                                    "IL001");
                                return false;
                            }

                            il.Emit(OpCodes.Newobj, classRuntime.Constructor);
                            il.Emit(OpCodes.Stloc, valueLocals[newObject.Destination]);
                            break;
                        }

                        if (!ConstructorClrResolver.TryResolve(
                                newObject.ObjectType is ClrNominalTypeSymbol nominal ? nominal.ClrTypeFullName : newObject.ObjectType == TypeSymbols.String ? "System.String" : string.Empty,
                                newObject.ArgumentTypes,
                                out var binding,
                                out _,
                                out var resolveError))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {resolveError}", "IL001");
                            return false;
                        }

                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, localVariables, parameterLocalIndexes, newObject.Arguments.Select(a => (IrValueId?)a).ToArray(), newObject.ArgumentTypes, BuildNoModifiers(newObject.ArgumentTypes.Count), BuildNoByRefLocals(newObject.ArgumentTypes.Count), binding.Constructor, diagnostics))
                        {
                            return false;
                        }

                        var importedConstructor = module.ImportReference(binding.Constructor);
                        il.Emit(OpCodes.Newobj, importedConstructor);
                        il.Emit(OpCodes.Stloc, valueLocals[newObject.Destination]);
                        break;
                    }

                    case IrConstructEnum constructEnum:
                    {
                        if (!enumRuntimeMap.TryGetValue(constructEnum.EnumName, out var enumRuntime))
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend could not resolve generated enum type '{constructEnum.EnumName}'",
                                "IL001");
                            return false;
                        }

                        if (constructEnum.Arguments.Count != constructEnum.ArgumentTypes.Count)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend enum constructor argument metadata mismatch for '{constructEnum.EnumName}.{constructEnum.VariantName}'",
                                "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Newobj, enumRuntime.Constructor);
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Ldc_I4, constructEnum.Tag);
                        il.Emit(OpCodes.Stfld, enumRuntime.TagField);

                        for (var i = 0; i < constructEnum.Arguments.Count; i++)
                        {
                            if (i >= enumRuntime.PayloadFields.Count)
                            {
                                diagnostics.Report(Span.Empty,
                                    $"phase-5 CLR backend enum payload slot overflow for '{constructEnum.EnumName}.{constructEnum.VariantName}'",
                                    "IL001");
                                return false;
                            }

                            var argumentType = constructEnum.ArgumentTypes[i];
                            var clrArgumentType = MapType(argumentType, module, diagnostics, typeMapper);
                            if (clrArgumentType == null)
                            {
                                return false;
                            }

                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Ldloc, valueLocals[constructEnum.Arguments[i]]);
                            if (clrArgumentType.IsValueType)
                            {
                                il.Emit(OpCodes.Box, clrArgumentType);
                            }

                            il.Emit(OpCodes.Stfld, enumRuntime.PayloadFields[i]);
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[constructEnum.Destination]);
                        break;
                    }

                    case IrGetEnumTag getEnumTag:
                    {
                        if (!enumRuntimeMap.TryGetValue(getEnumTag.EnumName, out var enumRuntime))
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend could not resolve generated enum type '{getEnumTag.EnumName}'",
                                "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[getEnumTag.EnumValue]);
                        il.Emit(OpCodes.Ldfld, enumRuntime.TagField);
                        il.Emit(OpCodes.Stloc, valueLocals[getEnumTag.Destination]);
                        break;
                    }

                    case IrGetEnumPayload getEnumPayload:
                    {
                        if (!enumRuntimeMap.TryGetValue(getEnumPayload.EnumName, out var enumRuntime))
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend could not resolve generated enum type '{getEnumPayload.EnumName}'",
                                "IL001");
                            return false;
                        }

                        if (getEnumPayload.PayloadIndex < 0 || getEnumPayload.PayloadIndex >= enumRuntime.PayloadFields.Count)
                        {
                            diagnostics.Report(Span.Empty,
                                $"phase-5 CLR backend enum payload index out of range for '{getEnumPayload.EnumName}'",
                                "IL001");
                            return false;
                        }

                        var clrPayloadType = MapType(getEnumPayload.PayloadType, module, diagnostics, typeMapper);
                        if (clrPayloadType == null)
                        {
                            return false;
                        }

                        il.Emit(OpCodes.Ldloc, valueLocals[getEnumPayload.EnumValue]);
                        il.Emit(OpCodes.Ldfld, enumRuntime.PayloadFields[getEnumPayload.PayloadIndex]);
                        if (clrPayloadType.IsValueType)
                        {
                            il.Emit(OpCodes.Unbox_Any, clrPayloadType);
                        }
                        else
                        {
                            il.Emit(OpCodes.Castclass, clrPayloadType);
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[getEnumPayload.Destination]);
                        break;
                    }

                    default:
                        diagnostics.Report(Span.Empty, "phase-4 CLR backend encountered unsupported IR instruction", "IL001");
                        return false;
                }
            }

            switch (block.Terminator)
            {
                case IrReturn ret:
                    il.Emit(OpCodes.Ldloc, valueLocals[ret.Value]);
                    il.Emit(OpCodes.Ret);
                    break;

                case IrReturnVoid:
                    il.Emit(OpCodes.Ret);
                    break;

                case IrJump jump:
                    if (!labels.TryGetValue(jump.TargetBlockId, out var jumpLabel))
                    {
                        diagnostics.Report(Span.Empty, "phase-4 CLR backend encountered invalid jump target", "IL001");
                        return false;
                    }
                    il.Emit(OpCodes.Br, jumpLabel);
                    break;

                case IrBranch branch:
                    if (!labels.TryGetValue(branch.ThenBlockId, out var thenLabel) ||
                        !labels.TryGetValue(branch.ElseBlockId, out var elseLabel))
                    {
                        diagnostics.Report(Span.Empty, "phase-4 CLR backend encountered invalid branch target", "IL001");
                        return false;
                    }

                    il.Emit(OpCodes.Ldloc, valueLocals[branch.Condition]);
                    il.Emit(OpCodes.Brtrue, thenLabel);
                    il.Emit(OpCodes.Br, elseLabel);
                    break;

                default:
                    diagnostics.Report(Span.Empty, "phase-4 CLR backend requires explicit block terminators", "IL001");
                    return false;
            }
        }

        return true;
    }

    private static TypeReference? MapType(
        TypeSymbol type,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap,
        IReadOnlyDictionary<string, TypeDefinition>? enumTypeMap = null,
        IReadOnlyDictionary<string, TypeDefinition>? classTypeMap = null,
        IReadOnlyDictionary<string, TypeDefinition>? interfaceTypeMap = null)
    {
        if (type == TypeSymbols.Int)
        {
            return module.TypeSystem.Int32;
        }

        if (type == TypeSymbols.Long)
        {
            return module.TypeSystem.Int64;
        }

        if (type == TypeSymbols.Double)
        {
            return module.TypeSystem.Double;
        }

        if (type == TypeSymbols.Char)
        {
            return module.TypeSystem.Char;
        }

        if (type == TypeSymbols.Byte)
        {
            return module.TypeSystem.Byte;
        }

        if (type == TypeSymbols.SByte)
        {
            return module.TypeSystem.SByte;
        }

        if (type == TypeSymbols.Short)
        {
            return module.TypeSystem.Int16;
        }

        if (type == TypeSymbols.UShort)
        {
            return module.TypeSystem.UInt16;
        }

        if (type == TypeSymbols.UInt)
        {
            return module.TypeSystem.UInt32;
        }

        if (type == TypeSymbols.ULong)
        {
            return module.TypeSystem.UInt64;
        }

        if (type == TypeSymbols.NInt)
        {
            return module.TypeSystem.IntPtr;
        }

        if (type == TypeSymbols.NUInt)
        {
            return module.TypeSystem.UIntPtr;
        }

        if (type == TypeSymbols.Float)
        {
            return module.TypeSystem.Single;
        }

        if (type == TypeSymbols.Decimal)
        {
            return module.ImportReference(typeof(decimal));
        }

        if (type == TypeSymbols.Bool)
        {
            return module.TypeSystem.Boolean;
        }

        if (type == TypeSymbols.String)
        {
            return module.TypeSystem.String;
        }

        if (type == TypeSymbols.Void)
        {
            return module.TypeSystem.Void;
        }

        if (type is ArrayTypeSymbol arrayType)
        {
            var mappedElement = MapType(arrayType.ElementType, module, diagnostics, delegateTypeMap, enumTypeMap, classTypeMap, interfaceTypeMap);
            return mappedElement == null ? null : new Mono.Cecil.ArrayType(mappedElement);
        }

        if (type is FunctionTypeSymbol functionType)
        {
            if (delegateTypeMap.TryGetValue(functionType.Name, out var delegateType))
            {
                return delegateType;
            }

            diagnostics.Report(Span.Empty, $"phase-6 CLR backend is missing delegate type for '{functionType}'", "IL001");
            return null;
        }

        if (type is ClrNominalTypeSymbol nominalType)
        {
            if (!ConstructorClrResolver.TryResolveTypeDefinition(nominalType.ClrTypeFullName, out var typeDefinition))
            {
                diagnostics.Report(Span.Empty, $"phase-4 CLR backend could not load runtime type '{nominalType.ClrTypeFullName}'", "IL001");
                return null;
            }

            return module.ImportReference(typeDefinition);
        }

        if (type is GenericParameterTypeSymbol)
        {
            return module.TypeSystem.Object;
        }

        if (type is EnumTypeSymbol enumType && enumTypeMap != null && enumTypeMap.TryGetValue(enumType.EnumName, out var enumTypeDefinition))
        {
            return module.ImportReference(enumTypeDefinition);
        }

        if (type is ClassTypeSymbol classType && classTypeMap != null && classTypeMap.TryGetValue(classType.ClassName, out var classTypeDefinition))
        {
            return module.ImportReference(classTypeDefinition);
        }

        if (type is InterfaceTypeSymbol interfaceType)
        {
            if (interfaceTypeMap != null && interfaceTypeMap.TryGetValue(interfaceType.InterfaceName, out var interfaceTypeDefinition))
            {
                return module.ImportReference(interfaceTypeDefinition);
            }

            return module.TypeSystem.Object;
        }

        diagnostics.Report(Span.Empty, $"phase-4 CLR backend does not support type '{type}'", "IL001");
        return null;
    }

    private static TypeReference? MapType(
        TypeSymbol type,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        return typeMapper.TryMapKongType(type, module, diagnostics);
    }

    private static Dictionary<string, TypeDefinition>? BuildDelegateTypeMap(
        IReadOnlyList<IrFunction> functions,
        ModuleDefinition module,
        TypeDefinition programType,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeDefinition> enumTypeMap,
        IReadOnlyDictionary<string, TypeDefinition> classTypeMap,
        IReadOnlyDictionary<string, TypeDefinition> interfaceTypeMap)
    {
        var functionTypes = new Dictionary<string, FunctionTypeSymbol>();
        foreach (var function in functions)
        {
            CollectFunctionTypes(function.ReturnType, functionTypes);
            foreach (var parameter in function.Parameters)
            {
                CollectFunctionTypes(parameter.Type, functionTypes);
            }

            foreach (var type in function.ValueTypes.Values)
            {
                CollectFunctionTypes(type, functionTypes);
            }

            foreach (var type in function.LocalTypes.Values)
            {
                CollectFunctionTypes(type, functionTypes);
            }
        }

        var delegateMap = new Dictionary<string, TypeDefinition>();
        foreach (var (key, _) in functionTypes)
        {
            var delegateType = new TypeDefinition(
                "Kong.Generated",
                $"__delegate_{delegateMap.Count}",
                CecilTypeAttributes.NestedPublic | CecilTypeAttributes.Sealed | CecilTypeAttributes.Class,
                module.ImportReference(typeof(MulticastDelegate)));
            programType.NestedTypes.Add(delegateType);
            delegateMap[key] = delegateType;
        }

        foreach (var (key, functionType) in functionTypes)
        {
            var delegateType = delegateMap[key];

            var ctor = new MethodDefinition(
                ".ctor",
                CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.SpecialName | CecilMethodAttributes.RTSpecialName,
                module.TypeSystem.Void)
            {
                ImplAttributes = CecilMethodImplAttributes.Runtime | CecilMethodImplAttributes.Managed,
            };
            ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            delegateType.Methods.Add(ctor);

            var returnType = MapType(functionType.ReturnType, module, diagnostics, delegateMap, enumTypeMap, classTypeMap, interfaceTypeMap);
            if (returnType == null)
            {
                return null;
            }

            var invoke = new MethodDefinition(
                "Invoke",
                CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.NewSlot | CecilMethodAttributes.Virtual,
                returnType)
            {
                ImplAttributes = CecilMethodImplAttributes.Runtime | CecilMethodImplAttributes.Managed,
            };

            foreach (var parameterType in functionType.ParameterTypes)
            {
                var mapped = MapType(parameterType, module, diagnostics, delegateMap, enumTypeMap, classTypeMap, interfaceTypeMap);
                if (mapped == null)
                {
                    return null;
                }

                invoke.Parameters.Add(new ParameterDefinition(mapped));
            }

            delegateType.Methods.Add(invoke);
        }

        return delegateMap;

        static void CollectFunctionTypes(TypeSymbol type, Dictionary<string, FunctionTypeSymbol> map)
        {
            if (type is not FunctionTypeSymbol functionType)
            {
                if (type is ArrayTypeSymbol arrayType)
                {
                    CollectFunctionTypes(arrayType.ElementType, map);
                }

                return;
            }

            if (!map.ContainsKey(functionType.Name))
            {
                map[functionType.Name] = functionType;
            }

            foreach (var parameterType in functionType.ParameterTypes)
            {
                CollectFunctionTypes(parameterType, map);
            }

            CollectFunctionTypes(functionType.ReturnType, map);
        }
    }

    private static MethodReference BuildDelegateConstructor(TypeReference delegateType, ModuleDefinition module)
    {
        var resolved = delegateType.Resolve()!;
        var ctorDefinition = resolved.Methods.First(m => m.IsConstructor && m.Parameters.Count == 2);

        if (delegateType is GenericInstanceType genericInstanceType)
        {
            return module.ImportReference(ctorDefinition, genericInstanceType);
        }

        return module.ImportReference(ctorDefinition);
    }

    private static MethodReference? BuildDelegateInvoke(
        FunctionTypeSymbol functionType,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap)
    {
        if (!delegateTypeMap.TryGetValue(functionType.Name, out var delegateType))
        {
            diagnostics.Report(Span.Empty, $"phase-6 CLR backend is missing delegate type for '{functionType}'", "IL001");
            return null;
        }

        var invokeDefinition = delegateType.Methods.FirstOrDefault(m => m.Name == "Invoke");
        if (invokeDefinition == null)
        {
            diagnostics.Report(Span.Empty, "phase-6 CLR backend could not resolve delegate invoke method", "IL001");
            return null;
        }

        return module.ImportReference(invokeDefinition);
    }

    private static DisplayClassInfo? BuildDisplayClass(
        IrFunction function,
        IReadOnlyDictionary<string, MethodDefinition> methodMap,
        ModuleDefinition module,
        TypeDefinition programType,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        if (!methodMap.TryGetValue(function.Name, out var targetMethod))
        {
            diagnostics.Report(Span.Empty, $"phase-6 CLR backend missing target method for '{function.Name}'", "IL001");
            return null;
        }

        var closureType = new FunctionTypeSymbol(
            function.Parameters.Skip(function.CaptureParameterCount).Select(p => p.Type).ToList(),
            function.ReturnType);
        var delegateType = MapType(closureType, module, diagnostics, typeMapper);
        if (delegateType == null)
        {
            return null;
        }

        var displayClass = new TypeDefinition(
            "Kong.Generated",
            $"__display_{function.Name}",
            CecilTypeAttributes.NestedPrivate | CecilTypeAttributes.Class | CecilTypeAttributes.Sealed,
            module.TypeSystem.Object);
        programType.NestedTypes.Add(displayClass);

        var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(".ctor", CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig | CecilMethodAttributes.SpecialName | CecilMethodAttributes.RTSpecialName, module.TypeSystem.Void);
        displayClass.Methods.Add(ctor);
        var ctorIl = ctor.Body.GetILProcessor();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, objectCtor);
        ctorIl.Emit(OpCodes.Ret);

        var captureFields = new List<FieldDefinition>(function.CaptureParameterCount);
        for (var i = 0; i < function.CaptureParameterCount; i++)
        {
            var captureParameter = function.Parameters[i];
            var fieldType = MapType(captureParameter.Type, module, diagnostics, typeMapper);
            if (fieldType == null)
            {
                return null;
            }

            var field = new FieldDefinition($"capture_{i}", CecilFieldAttributes.Public, fieldType);
            displayClass.Fields.Add(field);
            captureFields.Add(field);
        }

        var invokeReturnType = MapType(function.ReturnType, module, diagnostics, typeMapper);
        if (invokeReturnType == null)
        {
            return null;
        }

        var invoke = new MethodDefinition("Invoke", CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig, invokeReturnType);
        displayClass.Methods.Add(invoke);
        for (var i = function.CaptureParameterCount; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            var parameterType = MapType(parameter.Type, module, diagnostics, typeMapper);
            if (parameterType == null)
            {
                return null;
            }

            invoke.Parameters.Add(new ParameterDefinition(parameter.Name, CecilParameterAttributes.None, parameterType));
        }

        var invokeIl = invoke.Body.GetILProcessor();
        for (var i = 0; i < function.CaptureParameterCount; i++)
        {
            invokeIl.Emit(OpCodes.Ldarg_0);
            invokeIl.Emit(OpCodes.Ldfld, captureFields[i]);
        }

        for (var i = 0; i < invoke.Parameters.Count; i++)
        {
            invokeIl.Emit(OpCodes.Ldarg, invoke.Parameters[i]);
        }

        invokeIl.Emit(OpCodes.Call, targetMethod);
        invokeIl.Emit(OpCodes.Ret);

        return new DisplayClassInfo(
            displayClass,
            invoke,
            captureFields,
            delegateType,
            BuildDelegateConstructor(delegateType, module));
    }

    private static MethodReference? ImportStaticMethod(
        ModuleDefinition module,
        string methodPath,
        IReadOnlyList<TypeSymbol> argumentTypes,
        TypeSymbol returnType,
        DiagnosticBag diagnostics)
    {
        if (!StaticClrMethodResolver.TryResolve(
                methodPath,
                argumentTypes,
                out var binding,
                out _,
                out var errorMessage))
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {errorMessage}", "IL001");
            return null;
        }

        if (binding.ReturnType != returnType)
        {
            diagnostics.Report(Span.Empty,
                $"phase-5 CLR backend expected static call '{methodPath}' to return '{returnType}', but resolver returned '{binding.ReturnType}'",
                "IL001");
            return null;
        }

        return module.ImportReference(binding.MethodDefinition);
    }

    private static IReadOnlyList<ClrCallArgument> BuildClrArguments(
        IReadOnlyList<TypeSymbol> argumentTypes,
        IReadOnlyList<CallArgumentModifier> argumentModifiers)
    {
        var arguments = new ClrCallArgument[argumentTypes.Count];
        for (var i = 0; i < argumentTypes.Count; i++)
        {
            arguments[i] = new ClrCallArgument(argumentTypes[i], argumentModifiers[i]);
        }

        return arguments;
    }

    private static IReadOnlyList<CallArgumentModifier> BuildNoModifiers(int count)
    {
        return Enumerable.Repeat(CallArgumentModifier.None, count).ToArray();
    }

    private static IReadOnlyList<IrLocalId?> BuildNoByRefLocals(int count)
    {
        return Enumerable.Repeat<IrLocalId?>(null, count).ToArray();
    }

    private static bool EmitResolvedStaticCallArguments(
        ModuleDefinition module,
        ILProcessor il,
        IReadOnlyDictionary<IrValueId, VariableDefinition> valueLocals,
        IReadOnlyDictionary<IrLocalId, VariableDefinition> localVariables,
        IReadOnlyDictionary<IrLocalId, int> parameterLocalIndexes,
        IReadOnlyList<IrValueId?> arguments,
        IReadOnlyList<TypeSymbol> argumentTypes,
        IReadOnlyList<CallArgumentModifier> argumentModifiers,
        IReadOnlyList<IrLocalId?> byRefLocals,
        MethodDefinition method,
        DiagnosticBag diagnostics)
    {
        var parameters = method.Parameters;
        var hasParamsArray = parameters.Count > 0 &&
            parameters[^1].CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute") &&
            parameters[^1].ParameterType is Mono.Cecil.ArrayType;

        if (!hasParamsArray)
        {
            if (arguments.Count > parameters.Count)
            {
                diagnostics.Report(Span.Empty, $"phase-5 CLR backend too many arguments for '{method.FullName}'", "IL001");
                return false;
            }

            for (var i = 0; i < arguments.Count; i++)
            {
                if (!TryMapParameterType(parameters[i], out var parameterType))
                {
                    diagnostics.Report(Span.Empty, $"phase-5 CLR backend unsupported parameter type '{parameters[i].ParameterType.FullName}'", "IL001");
                    return false;
                }

                if (!EmitCallArgument(module, il, valueLocals, localVariables, parameterLocalIndexes, method, i, arguments[i], argumentTypes[i], argumentModifiers[i], byRefLocals[i], parameterType, parameters[i], diagnostics))
                {
                    return false;
                }
            }

            for (var i = arguments.Count; i < parameters.Count; i++)
            {
                if (!EmitDefaultArgument(il, parameters[i], diagnostics))
                {
                    return false;
                }
            }

            return true;
        }

        var fixedCount = parameters.Count - 1;
        if (arguments.Count < fixedCount)
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend too few arguments for '{method.FullName}'", "IL001");
            return false;
        }

        for (var i = 0; i < fixedCount; i++)
        {
            if (!TryMapParameterType(parameters[i], out var parameterType))
            {
                diagnostics.Report(Span.Empty, $"phase-5 CLR backend unsupported parameter type '{parameters[i].ParameterType.FullName}'", "IL001");
                return false;
            }

            if (!EmitCallArgument(module, il, valueLocals, localVariables, parameterLocalIndexes, method, i, arguments[i], argumentTypes[i], argumentModifiers[i], byRefLocals[i], parameterType, parameters[i], diagnostics))
            {
                return false;
            }
        }

        if (parameters[^1].ParameterType is not Mono.Cecil.ArrayType paramsArrayType)
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend invalid params array for '{method.FullName}'", "IL001");
            return false;
        }

        if (!TryMapType(paramsArrayType.ElementType, out var paramsElementType))
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend unsupported params element type '{paramsArrayType.ElementType.FullName}'", "IL001");
            return false;
        }

        var hasExplicitArrayArgument = arguments.Count == parameters.Count && argumentTypes[^1] is ArrayTypeSymbol providedArray && TypeEquals(providedArray.ElementType, paramsElementType) && argumentModifiers[^1] == CallArgumentModifier.None;
        if (hasExplicitArrayArgument)
        {
            if (arguments[^1] == null)
            {
                diagnostics.Report(Span.Empty, "phase-5 CLR backend params array argument requires value", "IL001");
                return false;
            }

            EmitArgumentWithConversion(il, valueLocals[arguments[^1]!.Value], argumentTypes[^1], new ArrayTypeSymbol(paramsElementType));
            return true;
        }

        var paramsCount = arguments.Count - fixedCount;
        il.Emit(OpCodes.Ldc_I4, paramsCount);
        il.Emit(OpCodes.Newarr, module.ImportReference(paramsArrayType.ElementType));

        for (var i = 0; i < paramsCount; i++)
        {
            if (argumentModifiers[fixedCount + i] != CallArgumentModifier.None || arguments[fixedCount + i] == null)
            {
                diagnostics.Report(Span.Empty, "phase-5 CLR backend does not support out/ref modifiers in params arguments", "IL001");
                return false;
            }

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            EmitArgumentWithConversion(il, valueLocals[arguments[fixedCount + i]!.Value], argumentTypes[fixedCount + i], paramsElementType);
            EmitStoreElement(il, paramsElementType);
        }

        return true;
    }

    private static bool EmitInterfaceCall(
        IrValueId receiver,
        InterfaceTypeSymbol receiverType,
        string memberName,
        IReadOnlyList<IrValueId> arguments,
        IReadOnlyDictionary<IrValueId, VariableDefinition> valueLocals,
        IReadOnlyDictionary<string, InterfaceRuntimeInfo> interfaceRuntimeMap,
        ILProcessor il,
        DiagnosticBag diagnostics)
    {
        if (!interfaceRuntimeMap.TryGetValue(receiverType.InterfaceName, out var interfaceRuntime))
        {
            diagnostics.Report(
                Span.Empty,
                $"phase-5 CLR backend could not resolve generated interface type '{receiverType.InterfaceName}'",
                "IL001");
            return false;
        }

        if (!interfaceRuntime.Methods.TryGetValue(memberName, out var interfaceMethod))
        {
            diagnostics.Report(
                Span.Empty,
                $"phase-5 CLR backend could not resolve interface method '{receiverType.InterfaceName}.{memberName}'",
                "IL001");
            return false;
        }

        il.Emit(OpCodes.Ldloc, valueLocals[receiver]);
        il.Emit(OpCodes.Castclass, interfaceRuntime.Type);
        foreach (var argument in arguments)
        {
            il.Emit(OpCodes.Ldloc, valueLocals[argument]);
        }

        il.Emit(OpCodes.Callvirt, interfaceMethod);
        return true;
    }

    private static bool EmitCallArgument(
        ModuleDefinition module,
        ILProcessor il,
        IReadOnlyDictionary<IrValueId, VariableDefinition> valueLocals,
        IReadOnlyDictionary<IrLocalId, VariableDefinition> localVariables,
        IReadOnlyDictionary<IrLocalId, int> parameterLocalIndexes,
        MethodDefinition currentMethod,
        int argumentIndex,
        IrValueId? argumentValue,
        TypeSymbol argumentType,
        CallArgumentModifier argumentModifier,
        IrLocalId? byRefLocal,
        TypeSymbol parameterType,
        ParameterDefinition targetParameter,
        DiagnosticBag diagnostics)
    {
        if (targetParameter.ParameterType is ByReferenceType)
        {
            var expectedModifier = targetParameter.IsOut ? CallArgumentModifier.Out : CallArgumentModifier.Ref;
            if (argumentModifier != expectedModifier || byRefLocal == null)
            {
                diagnostics.Report(Span.Empty,
                    $"phase-5 CLR backend byref argument {argumentIndex + 1} modifier mismatch",
                    "IL001");
                return false;
            }

            if (localVariables.TryGetValue(byRefLocal.Value, out var localVariable))
            {
                il.Emit(OpCodes.Ldloca, localVariable);
                return true;
            }

            if (parameterLocalIndexes.TryGetValue(byRefLocal.Value, out var parameterIndex))
            {
                il.Emit(OpCodes.Ldarga, currentMethod.Parameters[parameterIndex]);
                return true;
            }

            diagnostics.Report(Span.Empty,
                $"phase-5 CLR backend could not resolve byref local for argument {argumentIndex + 1}",
                "IL001");
            return false;
        }

        if (argumentModifier != CallArgumentModifier.None || argumentValue == null)
        {
            diagnostics.Report(Span.Empty,
                $"phase-5 CLR backend invalid non-byref argument at position {argumentIndex + 1}",
                "IL001");
            return false;
        }

        EmitArgumentWithConversion(il, valueLocals[argumentValue.Value], argumentType, parameterType);
        return true;
    }

    private static bool EmitClassFieldLoadConversion(
        TypeSymbol sourceType,
        TypeSymbol targetType,
        ILProcessor il,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        if (TypeEquals(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is GenericParameterTypeSymbol)
        {
            var targetClrType = MapType(targetType, module, diagnostics, typeMapper);
            if (targetClrType == null)
            {
                return false;
            }

            if (targetClrType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, targetClrType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, targetClrType);
            }
        }

        return true;
    }

    private static bool EmitClassFieldStoreConversion(
        TypeSymbol sourceType,
        TypeSymbol targetType,
        ILProcessor il,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        if (TypeEquals(sourceType, targetType))
        {
            return true;
        }

        if (targetType is GenericParameterTypeSymbol)
        {
            var sourceClrType = MapType(sourceType, module, diagnostics, typeMapper);
            if (sourceClrType == null)
            {
                return false;
            }

            if (sourceClrType.IsValueType)
            {
                il.Emit(OpCodes.Box, sourceClrType);
            }
        }

        return true;
    }

    private static void EmitArgumentWithConversion(ILProcessor il, VariableDefinition local, TypeSymbol sourceType, TypeSymbol targetType)
    {
        il.Emit(OpCodes.Ldloc, local);

        if (TypeEquals(sourceType, targetType))
        {
            return;
        }

        if (sourceType == TypeSymbols.Byte)
        {
            il.Emit(OpCodes.Conv_U1);
        }
        else if (sourceType == TypeSymbols.Char)
        {
            il.Emit(OpCodes.Conv_U2);
        }

        if (targetType == TypeSymbols.Int)
        {
            il.Emit(OpCodes.Conv_I4);
            return;
        }

        if (targetType == TypeSymbols.Long)
        {
            il.Emit(OpCodes.Conv_I8);
            return;
        }

        if (targetType == TypeSymbols.Double)
        {
            il.Emit(OpCodes.Conv_R8);
            return;
        }

        if (targetType == TypeSymbols.Byte)
        {
            il.Emit(OpCodes.Conv_U1);
            return;
        }

        if (targetType == TypeSymbols.Char)
        {
            il.Emit(OpCodes.Conv_U2);
        }
    }

    private static void EmitArgumentWithConversion(
        ILProcessor il,
        VariableDefinition local,
        TypeSymbol sourceType,
        TypeSymbol targetType,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        ITypeMapper typeMapper)
    {
        EmitArgumentWithConversion(il, local, sourceType, targetType);

        if (TypeEquals(sourceType, targetType))
        {
            return;
        }

        if (targetType is GenericParameterTypeSymbol)
        {
            var sourceClrType = MapType(sourceType, module, diagnostics, typeMapper);
            if (sourceClrType != null && sourceClrType.IsValueType)
            {
                il.Emit(OpCodes.Box, sourceClrType);
            }
        }
    }

    private static bool EmitDefaultArgument(ILProcessor il, ParameterDefinition parameter, DiagnosticBag diagnostics)
    {
        if (!TryMapParameterType(parameter, out var parameterType))
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend unsupported optional parameter type '{parameter.ParameterType.FullName}'", "IL001");
            return false;
        }

        if (!parameter.IsOptional && !parameter.HasDefault)
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend missing required argument for '{parameter.Name}'", "IL001");
            return false;
        }

        var constant = parameter.Constant;
        if (constant == null || constant is DBNull)
        {
            EmitDefaultValue(il, parameterType);
            return true;
        }

        if (parameterType == TypeSymbols.String && constant is string stringValue)
        {
            il.Emit(OpCodes.Ldstr, stringValue);
            return true;
        }

        if (parameterType == TypeSymbols.Bool && constant is bool boolValue)
        {
            il.Emit(boolValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            return true;
        }

        if (parameterType == TypeSymbols.Double && constant is double doubleValue)
        {
            il.Emit(OpCodes.Ldc_R8, doubleValue);
            return true;
        }

        if ((parameterType == TypeSymbols.Int || parameterType == TypeSymbols.Byte || parameterType == TypeSymbols.Char) && constant is IConvertible)
        {
            il.Emit(OpCodes.Ldc_I4, Convert.ToInt32(constant));
            if (parameterType == TypeSymbols.Byte)
            {
                il.Emit(OpCodes.Conv_U1);
            }
            else if (parameterType == TypeSymbols.Char)
            {
                il.Emit(OpCodes.Conv_U2);
            }

            return true;
        }

        if (parameterType == TypeSymbols.Long && constant is IConvertible)
        {
            il.Emit(OpCodes.Ldc_I8, Convert.ToInt64(constant));
            return true;
        }

        EmitDefaultValue(il, parameterType);
        return true;
    }

    private static void EmitDefaultValue(ILProcessor il, TypeSymbol parameterType)
    {
        if (parameterType == TypeSymbols.String)
        {
            il.Emit(OpCodes.Ldnull);
            return;
        }

        if (parameterType == TypeSymbols.Double)
        {
            il.Emit(OpCodes.Ldc_R8, 0d);
            return;
        }

        if (parameterType == TypeSymbols.Long)
        {
            il.Emit(OpCodes.Ldc_I8, 0L);
            return;
        }

        il.Emit(OpCodes.Ldc_I4_0);
        if (parameterType == TypeSymbols.Byte)
        {
            il.Emit(OpCodes.Conv_U1);
        }
        else if (parameterType == TypeSymbols.Char)
        {
            il.Emit(OpCodes.Conv_U2);
        }
    }

    private static void EmitStoreElement(ILProcessor il, TypeSymbol elementType)
    {
        if (elementType == TypeSymbols.Int)
        {
            il.Emit(OpCodes.Stelem_I4);
            return;
        }

        if (elementType == TypeSymbols.Long)
        {
            il.Emit(OpCodes.Stelem_I8);
            return;
        }

        if (elementType == TypeSymbols.Double)
        {
            il.Emit(OpCodes.Stelem_R8);
            return;
        }

        if (elementType == TypeSymbols.Bool || elementType == TypeSymbols.Byte)
        {
            il.Emit(OpCodes.Stelem_I1);
            return;
        }

        if (elementType == TypeSymbols.Char)
        {
            il.Emit(OpCodes.Stelem_I2);
            return;
        }

        il.Emit(OpCodes.Stelem_Ref);
    }

    private static void EmitLoadElement(ILProcessor il, TypeSymbol elementType)
    {
        if (elementType == TypeSymbols.Int)
        {
            il.Emit(OpCodes.Ldelem_I4);
            return;
        }

        if (elementType == TypeSymbols.Long)
        {
            il.Emit(OpCodes.Ldelem_I8);
            return;
        }

        if (elementType == TypeSymbols.Double)
        {
            il.Emit(OpCodes.Ldelem_R8);
            return;
        }

        if (elementType == TypeSymbols.Bool || elementType == TypeSymbols.Byte)
        {
            il.Emit(OpCodes.Ldelem_U1);
            return;
        }

        if (elementType == TypeSymbols.Char)
        {
            il.Emit(OpCodes.Ldelem_U2);
            return;
        }

        il.Emit(OpCodes.Ldelem_Ref);
    }

    private static bool TryMapParameterType(ParameterDefinition parameter, out TypeSymbol parameterType)
    {
        return TryMapType(parameter.ParameterType, out parameterType);
    }

    private static bool TryMapType(TypeReference typeReference, out TypeSymbol type)
    {
        if (typeReference is ByReferenceType byReferenceType)
        {
            return TryMapType(byReferenceType.ElementType, out type);
        }

        if (typeReference is Mono.Cecil.ArrayType arrayType)
        {
            if (!TryMapType(arrayType.ElementType, out var elementType))
            {
                type = TypeSymbols.Error;
                return false;
            }

            type = new ArrayTypeSymbol(elementType);
            return true;
        }

        var normalized = typeReference.FullName.Replace("/", ".");
        return normalized switch
        {
            "System.Int32" => SetType(TypeSymbols.Int, out type),
            "System.Int64" => SetType(TypeSymbols.Long, out type),
            "System.Double" => SetType(TypeSymbols.Double, out type),
            "System.Single" => SetType(TypeSymbols.Float, out type),
            "System.Boolean" => SetType(TypeSymbols.Bool, out type),
            "System.String" => SetType(TypeSymbols.String, out type),
            "System.Char" => SetType(TypeSymbols.Char, out type),
            "System.Byte" => SetType(TypeSymbols.Byte, out type),
            "System.SByte" => SetType(TypeSymbols.SByte, out type),
            "System.Int16" => SetType(TypeSymbols.Short, out type),
            "System.UInt16" => SetType(TypeSymbols.UShort, out type),
            "System.UInt32" => SetType(TypeSymbols.UInt, out type),
            "System.UInt64" => SetType(TypeSymbols.ULong, out type),
            "System.IntPtr" => SetType(TypeSymbols.NInt, out type),
            "System.UIntPtr" => SetType(TypeSymbols.NUInt, out type),
            "System.Decimal" => SetType(TypeSymbols.Decimal, out type),
            "System.Void" => SetType(TypeSymbols.Void, out type),
            _ => SetType(TypeSymbols.Error, out type, false),
        };
    }

    private static bool SetType(TypeSymbol value, out TypeSymbol output, bool result = true)
    {
        output = value;
        return result;
    }

    private static Dictionary<string, TypeSymbol> BuildTypeParameterSubstitution(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<TypeSymbol> typeArguments)
    {
        var substitution = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (var i = 0; i < typeParameters.Count && i < typeArguments.Count; i++)
        {
            substitution[typeParameters[i]] = typeArguments[i];
        }

        return substitution;
    }

    private static TypeSymbol SubstituteGenericParameters(TypeSymbol type, IReadOnlyDictionary<string, TypeSymbol> substitution)
    {
        if (type is GenericParameterTypeSymbol genericParameter)
        {
            return substitution.TryGetValue(genericParameter.ParameterName, out var concrete)
                ? concrete
                : type;
        }

        if (type is ArrayTypeSymbol arrayType)
        {
            return new ArrayTypeSymbol(SubstituteGenericParameters(arrayType.ElementType, substitution));
        }

        if (type is FunctionTypeSymbol functionType)
        {
            var parameterTypes = functionType.ParameterTypes.Select(p => SubstituteGenericParameters(p, substitution)).ToList();
            var returnType = SubstituteGenericParameters(functionType.ReturnType, substitution);
            return new FunctionTypeSymbol(parameterTypes, returnType);
        }

        if (type is EnumTypeSymbol enumType)
        {
            var args = enumType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new EnumTypeSymbol(enumType.EnumName, args);
        }

        if (type is ClassTypeSymbol classType)
        {
            var args = classType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new ClassTypeSymbol(classType.ClassName, args);
        }

        if (type is InterfaceTypeSymbol interfaceType)
        {
            var args = interfaceType.TypeArguments.Select(a => SubstituteGenericParameters(a, substitution)).ToList();
            return new InterfaceTypeSymbol(interfaceType.InterfaceName, args);
        }

        return type;
    }

    private static bool TypeEquals(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is ArrayTypeSymbol leftArray && right is ArrayTypeSymbol rightArray)
        {
            return TypeEquals(leftArray.ElementType, rightArray.ElementType);
        }

        if (left is FunctionTypeSymbol leftFunction && right is FunctionTypeSymbol rightFunction)
        {
            if (!TypeEquals(leftFunction.ReturnType, rightFunction.ReturnType) ||
                leftFunction.ParameterTypes.Count != rightFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftFunction.ParameterTypes.Count; i++)
            {
                if (!TypeEquals(leftFunction.ParameterTypes[i], rightFunction.ParameterTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is EnumTypeSymbol leftEnum && right is EnumTypeSymbol rightEnum)
        {
            if (!string.Equals(leftEnum.EnumName, rightEnum.EnumName, StringComparison.Ordinal) ||
                leftEnum.TypeArguments.Count != rightEnum.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftEnum.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftEnum.TypeArguments[i], rightEnum.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is ClassTypeSymbol leftClass && right is ClassTypeSymbol rightClass)
        {
            if (!string.Equals(leftClass.ClassName, rightClass.ClassName, StringComparison.Ordinal) ||
                leftClass.TypeArguments.Count != rightClass.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftClass.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftClass.TypeArguments[i], rightClass.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is InterfaceTypeSymbol leftInterface && right is InterfaceTypeSymbol rightInterface)
        {
            if (!string.Equals(leftInterface.InterfaceName, rightInterface.InterfaceName, StringComparison.Ordinal) ||
                leftInterface.TypeArguments.Count != rightInterface.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftInterface.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftInterface.TypeArguments[i], rightInterface.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return left == right;
    }

    private static (MethodReference? GetterMethod, FieldReference? Field)? ImportStaticValueMember(
        ModuleDefinition module,
        string memberPath,
        DiagnosticBag diagnostics)
    {
        if (!StaticClrMethodResolver.TryResolveValue(memberPath, out var binding, out _, out var errorMessage))
        {
            diagnostics.Report(Span.Empty, $"phase-5 CLR backend {errorMessage}", "IL001");
            return null;
        }

        var getter = binding.PropertyGetter != null ? module.ImportReference(binding.PropertyGetter) : null;
        var field = binding.Field != null ? module.ImportReference(binding.Field) : null;
        return (getter, field);
    }

}
