using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
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
    public ClrPhase1ExecutionResult Execute(CompilationUnit unit, TypeCheckResult typeCheckResult)
    {
        var result = new ClrPhase1ExecutionResult();

        var lowerer = new IrLowerer();
        var loweringResult = lowerer.Lower(unit, typeCheckResult);
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

    private static byte[]? EmitAssembly(IrProgram program, DiagnosticBag diagnostics)
    {
        var function = program.EntryPoint;

        var assemblyName = new AssemblyNameDefinition("Kong.Generated", new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, "Kong.Generated", ModuleKind.Dll);
        var module = assembly.MainModule;

        var programType = new TypeDefinition(
            "Kong.Generated",
            "Program",
            CecilTypeAttributes.Public | CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(programType);

        var evalMethod = new MethodDefinition(
            "Eval",
            CecilMethodAttributes.Public | CecilMethodAttributes.Static,
            module.TypeSystem.Int64);
        programType.Methods.Add(evalMethod);

        if (!EmitFunction(function, evalMethod, module, diagnostics))
        {
            return null;
        }

        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    private static bool EmitFunction(IrFunction function, MethodDefinition method, ModuleDefinition module, DiagnosticBag diagnostics)
    {
        if (function.ReturnType != TypeSymbols.Int)
        {
            diagnostics.Report(Span.Empty, $"phase-1 CLR backend only supports int return type, got '{function.ReturnType}'", "IL001");
            return false;
        }

        if (function.Blocks.Count != 1)
        {
            diagnostics.Report(Span.Empty, "phase-1 CLR backend currently supports exactly one IR block", "IL001");
            return false;
        }

        var block = function.Blocks[0];
        method.Body.InitLocals = true;

        var valueLocals = new Dictionary<IrValueId, VariableDefinition>();
        foreach (var (valueId, type) in function.ValueTypes)
        {
            if (type != TypeSymbols.Int)
            {
                diagnostics.Report(Span.Empty, $"phase-1 CLR backend only supports int IR values, got '{type}'", "IL001");
                return false;
            }

            valueLocals[valueId] = new VariableDefinition(module.TypeSystem.Int64);
            method.Body.Variables.Add(valueLocals[valueId]);
        }

        var namedLocals = new Dictionary<IrLocalId, VariableDefinition>();
        foreach (var (localId, type) in function.LocalTypes)
        {
            if (type != TypeSymbols.Int)
            {
                diagnostics.Report(Span.Empty, $"phase-1 CLR backend only supports int IR locals, got '{type}'", "IL001");
                return false;
            }

            namedLocals[localId] = new VariableDefinition(module.TypeSystem.Int64);
            method.Body.Variables.Add(namedLocals[localId]);
        }

        var il = method.Body.GetILProcessor();
        foreach (var instruction in block.Instructions)
        {
            switch (instruction)
            {
                case IrConstInt constInt:
                    il.Emit(OpCodes.Ldc_I8, constInt.Value);
                    il.Emit(OpCodes.Stloc, valueLocals[constInt.Destination]);
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
                    il.Emit(OpCodes.Stloc, namedLocals[storeLocal.Local]);
                    break;

                case IrLoadLocal loadLocal:
                    il.Emit(OpCodes.Ldloc, namedLocals[loadLocal.Local]);
                    il.Emit(OpCodes.Stloc, valueLocals[loadLocal.Destination]);
                    break;

                default:
                    diagnostics.Report(Span.Empty, "phase-1 CLR backend encountered unsupported IR instruction", "IL001");
                    return false;
            }
        }

        if (block.Terminator is not IrReturn ret)
        {
            diagnostics.Report(Span.Empty, "phase-1 CLR backend requires an IR return terminator", "IL001");
            return false;
        }

        if (!valueLocals.TryGetValue(ret.Value, out var returnLocal))
        {
            diagnostics.Report(Span.Empty, "phase-1 CLR backend could not resolve IR return value", "IL001");
            return false;
        }

        il.Emit(OpCodes.Ldloc, returnLocal);
        il.Emit(OpCodes.Ret);
        return true;
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

        return Execute(unit, typeCheck);
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
