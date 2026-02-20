using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilParameterAttributes = Mono.Cecil.ParameterAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodImplAttributes = Mono.Cecil.MethodImplAttributes;

namespace Kong;

public class ClrPhase1ExecutionResult
{
    public bool Executed { get; set; }
    public bool IsUnsupported { get; set; }
    public long Value { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}

public class ClrArtifactBuildResult
{
    public bool Built { get; set; }
    public bool IsUnsupported { get; set; }
    public string? AssemblyPath { get; set; }
    public string? RuntimeConfigPath { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}

public class ClrPhase1Executor
{
    private sealed record class DisplayClassInfo(
        TypeDefinition Type,
        MethodDefinition InvokeMethod,
        IReadOnlyList<FieldDefinition> CaptureFields,
        TypeReference DelegateType,
        MethodReference DelegateCtor);

    [RequiresUnreferencedCode("Loads and invokes generated assemblies via reflection.")]
    public ClrPhase1ExecutionResult Execute(CompilationUnit unit, TypeCheckResult typeCheckResult, NameResolution? nameResolution = null)
    {
        var result = new ClrPhase1ExecutionResult();

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

        var value = ExecuteAssembly(assemblyBytes, result.Diagnostics);
        if (value == null)
        {
            result.IsUnsupported = false;
            return result;
        }

        result.Executed = true;
        result.Value = value.Value;
        return result;
    }

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
        System.IO.File.WriteAllBytes(assemblyPath, assemblyBytes);

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
        System.IO.File.WriteAllText(runtimeConfigPath, runtimeConfigJson + Environment.NewLine);

        var kongAssemblyFileName = $"{typeof(ClrPhase1Executor).Assembly.GetName().Name}.dll";
        var kongAssemblyPath = Path.Combine(AppContext.BaseDirectory, kongAssemblyFileName);
        if (System.IO.File.Exists(kongAssemblyPath))
        {
            var kongDestination = Path.Combine(outputDirectory, Path.GetFileName(kongAssemblyPath));
            System.IO.File.Copy(kongAssemblyPath, kongDestination, overwrite: true);
        }

        result.Built = true;
        result.AssemblyPath = assemblyPath;
        result.RuntimeConfigPath = runtimeConfigPath;
        return result;
    }

    [RequiresUnreferencedCode("Loads and invokes generated assemblies via reflection.")]
    public ClrPhase1ExecutionResult Execute(CompilationUnit unit)
    {
        var resolver = new NameResolver();
        var names = resolver.Resolve(unit);

        var checker = new TypeChecker();
        var typeCheck = checker.Check(unit, names);
        var result = new ClrPhase1ExecutionResult();
        result.Diagnostics.AddRange(typeCheck.Diagnostics);
        if (typeCheck.Diagnostics.HasErrors)
        {
            return result;
        }

        return Execute(unit, typeCheck, names);
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

        var methodMap = new Dictionary<string, MethodDefinition>();
        var builtinMap = BuildBuiltinMap(module);
        foreach (var function in allFunctions)
        {
            var returnType = MapType(function.ReturnType, module, diagnostics, delegateTypeMap);
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
                var parameterType = MapType(parameter.Type, module, diagnostics, delegateTypeMap);
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
            "Main",
            CecilMethodAttributes.Public | CecilMethodAttributes.Static,
            module.TypeSystem.Void);
        programType.Methods.Add(mainMethod);
        var writeLineLong = module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])!);
        var mainIl = mainMethod.Body.GetILProcessor();
        mainIl.Emit(OpCodes.Call, methodMap[program.EntryPoint.Name]);
        mainIl.Emit(OpCodes.Call, writeLineLong);
        mainIl.Emit(OpCodes.Ret);
        module.EntryPoint = mainMethod;

        var displayClassMap = new Dictionary<string, DisplayClassInfo>();
        foreach (var function in allFunctions.Where(f => f.CaptureParameterCount > 0))
        {
            var displayClass = BuildDisplayClass(function, methodMap, module, programType, diagnostics, delegateTypeMap);
            if (displayClass == null)
            {
                return null;
            }

            displayClassMap[function.Name] = displayClass;
        }

        foreach (var function in allFunctions)
        {
            if (!EmitFunction(function, methodMap[function.Name], methodMap, functionMap, displayClassMap, builtinMap, module, diagnostics, delegateTypeMap))
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
        IReadOnlyDictionary<string, MethodReference> builtinMap,
        ModuleDefinition module,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap)
    {
        method.Body.InitLocals = true;

        var valueLocals = new Dictionary<IrValueId, VariableDefinition>();
        foreach (var (valueId, type) in function.ValueTypes)
        {
            var variableType = MapType(type, module, diagnostics, delegateTypeMap);
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

            var variableType = MapType(type, module, diagnostics, delegateTypeMap);
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
                        il.Emit(OpCodes.Ldc_I8, constInt.Value);
                        il.Emit(OpCodes.Stloc, valueLocals[constInt.Destination]);
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
                        il.Emit(OpCodes.Ldloc, valueLocals[binary.Left]);
                        il.Emit(OpCodes.Ldloc, valueLocals[binary.Right]);
                        il.Emit(binary.Operator switch
                        {
                            IrBinaryOperator.Add => OpCodes.Add,
                            IrBinaryOperator.Subtract => OpCodes.Sub,
                            IrBinaryOperator.Multiply => OpCodes.Mul,
                            IrBinaryOperator.Divide => OpCodes.Div,
                            _ => throw new InvalidOperationException(),
                        });
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

                        if (!builtinMap.TryGetValue(call.FunctionName, out var builtinMethod))
                        {
                            diagnostics.Report(Span.Empty, $"phase-5 CLR backend could not resolve function '{call.FunctionName}'", "IL001");
                            return false;
                        }

                        il.Emit(OpCodes.Call, builtinMethod);
                        il.Emit(OpCodes.Stloc, valueLocals[call.Destination]);
                        break;

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

                        var delegateType = MapType(closureFunctionType, module, diagnostics, delegateTypeMap);
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

                    case IrNewIntArray newArray:
                        il.Emit(OpCodes.Ldc_I4, newArray.Elements.Count);
                        il.Emit(OpCodes.Newarr, module.TypeSystem.Int64);
                        for (var i = 0; i < newArray.Elements.Count; i++)
                        {
                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Ldc_I4, i);
                            il.Emit(OpCodes.Ldloc, valueLocals[newArray.Elements[i]]);
                            il.Emit(OpCodes.Stelem_I8);
                        }

                        il.Emit(OpCodes.Stloc, valueLocals[newArray.Destination]);
                        break;

                    case IrIntArrayIndex intArrayIndex:
                        il.Emit(OpCodes.Ldloc, valueLocals[intArrayIndex.Array]);
                        il.Emit(OpCodes.Ldloc, valueLocals[intArrayIndex.Index]);
                        il.Emit(OpCodes.Conv_I4);
                        il.Emit(OpCodes.Ldelem_I8);
                        il.Emit(OpCodes.Stloc, valueLocals[intArrayIndex.Destination]);
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
            return module.TypeSystem.Int64;
        }

        if (type == TypeSymbols.Bool)
        {
            return module.TypeSystem.Boolean;
        }

        if (type == TypeSymbols.String)
        {
            return module.TypeSystem.String;
        }

        if (type is ArrayTypeSymbol { ElementType: IntTypeSymbol })
        {
            return new Mono.Cecil.ArrayType(module.TypeSystem.Int64);
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
        IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap)
    {
        if (!methodMap.TryGetValue(function.Name, out var targetMethod))
        {
            diagnostics.Report(Span.Empty, $"phase-6 CLR backend missing target method for '{function.Name}'", "IL001");
            return null;
        }

        var closureType = new FunctionTypeSymbol(
            function.Parameters.Skip(function.CaptureParameterCount).Select(p => p.Type).ToList(),
            function.ReturnType);
        var delegateType = MapType(closureType, module, diagnostics, delegateTypeMap);
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
            var fieldType = MapType(captureParameter.Type, module, diagnostics, delegateTypeMap);
            if (fieldType == null)
            {
                return null;
            }

            var field = new FieldDefinition($"capture_{i}", CecilFieldAttributes.Public, fieldType);
            displayClass.Fields.Add(field);
            captureFields.Add(field);
        }

        var invokeReturnType = MapType(function.ReturnType, module, diagnostics, delegateTypeMap);
        if (invokeReturnType == null)
        {
            return null;
        }

        var invoke = new MethodDefinition("Invoke", CecilMethodAttributes.Public | CecilMethodAttributes.HideBySig, invokeReturnType);
        displayClass.Methods.Add(invoke);
        for (var i = function.CaptureParameterCount; i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            var parameterType = MapType(parameter.Type, module, diagnostics, delegateTypeMap);
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

    private static Dictionary<string, MethodReference> BuildBuiltinMap(ModuleDefinition module)
    {
        var runtimeType = typeof(ClrRuntimeBuiltins);

        return new Dictionary<string, MethodReference>
        {
            ["__builtin_len_string"] = module.ImportReference(runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.LenString))!),
            ["__builtin_first_int_array"] = module.ImportReference(runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.FirstIntArray))!),
            ["__builtin_last_int_array"] = module.ImportReference(runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.LastIntArray))!),
            ["__builtin_rest_int_array"] = module.ImportReference(runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.RestIntArray))!),
            ["__builtin_push_int_array"] = module.ImportReference(runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.PushIntArray))!),
        };
    }

    [RequiresUnreferencedCode("Loads and invokes generated assemblies via reflection.")]
    private static long? ExecuteAssembly(byte[] assemblyBytes, DiagnosticBag diagnostics)
    {
        var context = new AssemblyLoadContext($"kong-clr-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(assemblyBytes);
            var assembly = context.LoadFromStream(stream);
            var programType = assembly.GetType("Kong.Generated.Program");
            var method = programType?.GetMethod("Eval", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                diagnostics.Report(Span.Empty, "generated CLR assembly is missing entry method", "IL002");
                return null;
            }

            var value = method.Invoke(null, null);
            if (value is long int64)
            {
                return int64;
            }

            diagnostics.Report(Span.Empty, "generated CLR entry method returned unexpected value type", "IL003");
            return null;
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            diagnostics.Report(Span.Empty, $"failed to execute generated CLR assembly: {message}", "IL004");
            return null;
        }
        finally
        {
            context.Unload();
        }
    }
}
