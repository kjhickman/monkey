using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilParameterAttributes = Mono.Cecil.ParameterAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace Kong.Cli;

public class ClrPhase1ExecutionResult
{
    public bool Executed { get; set; }
    public bool IsUnsupported { get; set; }
    public long Value { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();
}

public class ClrPhase1Executor
{
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
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, "Kong.Generated", ModuleKind.Dll);
        var module = assembly.MainModule;

        var programType = new TypeDefinition(
            "Kong.Generated",
            "Program",
            CecilTypeAttributes.Public | CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(programType);

        var allFunctions = new List<IrFunction> { program.EntryPoint };
        allFunctions.AddRange(program.Functions);

        var methodMap = new Dictionary<string, MethodDefinition>();
        var builtinMap = BuildBuiltinMap(module);
        foreach (var function in allFunctions)
        {
            var returnType = MapType(function.ReturnType, module, diagnostics);
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
                var parameterType = MapType(parameter.Type, module, diagnostics);
                if (parameterType == null)
                {
                    return null;
                }

                method.Parameters.Add(new ParameterDefinition(parameter.Name, CecilParameterAttributes.None, parameterType));
            }

            programType.Methods.Add(method);
            methodMap[function.Name] = method;
        }

        foreach (var function in allFunctions)
        {
            if (!EmitFunction(function, methodMap[function.Name], methodMap, builtinMap, module, diagnostics))
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
        IReadOnlyDictionary<string, MethodReference> builtinMap,
        ModuleDefinition module,
        DiagnosticBag diagnostics)
    {
        method.Body.InitLocals = true;

        var valueLocals = new Dictionary<IrValueId, VariableDefinition>();
        foreach (var (valueId, type) in function.ValueTypes)
        {
            var variableType = MapType(type, module, diagnostics);
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

            var variableType = MapType(type, module, diagnostics);
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

    private static TypeReference? MapType(TypeSymbol type, ModuleDefinition module, DiagnosticBag diagnostics)
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

        diagnostics.Report(Span.Empty, $"phase-4 CLR backend does not support type '{type}'", "IL001");
        return null;
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
            diagnostics.Report(Span.Empty, $"failed to execute generated CLR assembly: {ex.Message}", "IL004");
            return null;
        }
        finally
        {
            context.Unload();
        }
    }
}
