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
        var delegateTypeMap = BuildDelegateTypeMap(allFunctions, module, programType, diagnostics);
        if (delegateTypeMap == null)
        {
            return null;
        }

        var typeMapper = new DefaultTypeMapper(delegateTypeMap);

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
            if (!EmitFunction(function, methodMap[function.Name], methodMap, functionMap, displayClassMap, module, diagnostics, delegateTypeMap, typeMapper))
            {
                return null;
            }
        }

        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    private static bool EmitFunction(
        IrFunction function,
        MethodDefinition method,
        IReadOnlyDictionary<string, MethodDefinition> methodMap,
        IReadOnlyDictionary<string, IrFunction> functionMap,
        IReadOnlyDictionary<string, DisplayClassInfo> displayClassMap,
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
                        foreach (var argument in call.Arguments)
                        {
                            il.Emit(OpCodes.Ldloc, valueLocals[argument]);
                        }

                        if (methodMap.TryGetValue(call.FunctionName, out var targetMethod))
                        {
                            il.Emit(OpCodes.Call, targetMethod);
                            il.Emit(OpCodes.Stloc, valueLocals[call.Destination]);
                            break;
                        }

                        diagnostics.Report(Span.Empty, $"phase-5 CLR backend could not resolve function '{call.FunctionName}'", "IL001");
                        return false;

                    case IrStaticCall staticCall:
                    {
                        if (!function.ValueTypes.TryGetValue(staticCall.Destination, out var staticReturnType))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend missing return type for static call '{staticCall.MethodPath}'", "IL001");
                            return false;
                        }

                        if (!StaticClrMethodResolver.TryResolve(
                                staticCall.MethodPath,
                                staticCall.ArgumentTypes,
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

                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, staticCall.Arguments, staticCall.ArgumentTypes, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var staticMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Call, staticMethod);
                        il.Emit(OpCodes.Stloc, valueLocals[staticCall.Destination]);
                        break;
                    }

                    case IrCallVoid callVoid:
                        foreach (var argument in callVoid.Arguments)
                        {
                            il.Emit(OpCodes.Ldloc, valueLocals[argument]);
                        }

                        if (methodMap.TryGetValue(callVoid.FunctionName, out var targetVoidMethod))
                        {
                            il.Emit(OpCodes.Call, targetVoidMethod);
                            break;
                        }

                        diagnostics.Report(Span.Empty, $"phase-6 CLR backend could not resolve function '{callVoid.FunctionName}'", "IL001");
                        return false;

                    case IrStaticCallVoid staticCallVoid:
                    {
                        if (!StaticClrMethodResolver.TryResolve(
                                staticCallVoid.MethodPath,
                                staticCallVoid.ArgumentTypes,
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

                        if (!EmitResolvedStaticCallArguments(module, il, valueLocals, staticCallVoid.Arguments, staticCallVoid.ArgumentTypes, binding.MethodDefinition, diagnostics))
                        {
                            return false;
                        }

                        var staticVoidMethod = module.ImportReference(binding.MethodDefinition);
                        il.Emit(OpCodes.Call, staticVoidMethod);
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
                        var arrayElementClrType = MapType(newArray.ElementType, module, diagnostics, delegateTypeMap);
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
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap)
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
            var mappedElement = MapType(arrayType.ElementType, module, diagnostics, delegateTypeMap);
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
        DiagnosticBag diagnostics)
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

            var returnType = MapType(functionType.ReturnType, module, diagnostics, delegateMap);
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
                var mapped = MapType(parameterType, module, diagnostics, delegateMap);
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

    private static bool EmitResolvedStaticCallArguments(
        ModuleDefinition module,
        ILProcessor il,
        IReadOnlyDictionary<IrValueId, VariableDefinition> valueLocals,
        IReadOnlyList<IrValueId> arguments,
        IReadOnlyList<TypeSymbol> argumentTypes,
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

                EmitArgumentWithConversion(il, valueLocals[arguments[i]], argumentTypes[i], parameterType);
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

            EmitArgumentWithConversion(il, valueLocals[arguments[i]], argumentTypes[i], parameterType);
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

        var hasExplicitArrayArgument = arguments.Count == parameters.Count && argumentTypes[^1] is ArrayTypeSymbol providedArray && TypeEquals(providedArray.ElementType, paramsElementType);
        if (hasExplicitArrayArgument)
        {
            EmitArgumentWithConversion(il, valueLocals[arguments[^1]], argumentTypes[^1], new ArrayTypeSymbol(paramsElementType));
            return true;
        }

        var paramsCount = arguments.Count - fixedCount;
        il.Emit(OpCodes.Ldc_I4, paramsCount);
        il.Emit(OpCodes.Newarr, module.ImportReference(paramsArrayType.ElementType));

        for (var i = 0; i < paramsCount; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            EmitArgumentWithConversion(il, valueLocals[arguments[fixedCount + i]], argumentTypes[fixedCount + i], paramsElementType);
            EmitStoreElement(il, paramsElementType);
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
            "System.Boolean" => SetType(TypeSymbols.Bool, out type),
            "System.String" => SetType(TypeSymbols.String, out type),
            "System.Char" => SetType(TypeSymbols.Char, out type),
            "System.Byte" => SetType(TypeSymbols.Byte, out type),
            "System.Void" => SetType(TypeSymbols.Void, out type),
            _ => SetType(TypeSymbols.Error, out type, false),
        };
    }

    private static bool SetType(TypeSymbol value, out TypeSymbol output, bool result = true)
    {
        output = value;
        return result;
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
