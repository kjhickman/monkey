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
    public ClrPhase1ExecutionResult Execute(CompilationUnit unit)
    {
        var result = new ClrPhase1ExecutionResult();

        var assemblyBytes = EmitAssembly(unit, result.Diagnostics);
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

    private static byte[]? EmitAssembly(CompilationUnit unit, DiagnosticBag diagnostics)
    {
        if (unit.Statements.Count != 1)
        {
            diagnostics.Report(unit.Span, "phase-1 CLR backend currently supports exactly one top-level expression statement", "IL001");
            return null;
        }

        if (unit.Statements[0] is not ExpressionStatement { Expression: { } expression })
        {
            diagnostics.Report(unit.Statements[0].Span, "phase-1 CLR backend currently supports expression statements only", "IL001");
            return null;
        }

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

        var il = evalMethod.Body.GetILProcessor();
        if (!EmitExpression(expression, il, diagnostics))
        {
            return null;
        }

        il.Emit(OpCodes.Ret);

        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    private static bool EmitExpression(IExpression expression, ILProcessor il, DiagnosticBag diagnostics)
    {
        switch (expression)
        {
            case IntegerLiteral integerLiteral:
                il.Emit(OpCodes.Ldc_I8, integerLiteral.Value);
                return true;

            case InfixExpression infixExpression when infixExpression.Operator is "+" or "-" or "*" or "/":
                if (!EmitExpression(infixExpression.Left, il, diagnostics))
                {
                    return false;
                }

                if (!EmitExpression(infixExpression.Right, il, diagnostics))
                {
                    return false;
                }

                il.Emit(infixExpression.Operator switch
                {
                    "+" => OpCodes.Add,
                    "-" => OpCodes.Sub,
                    "*" => OpCodes.Mul,
                    "/" => OpCodes.Div,
                    _ => throw new InvalidOperationException(),
                });
                return true;

            default:
                diagnostics.Report(expression.Span, $"phase-1 CLR backend does not support expression '{expression.TokenLiteral()}'", "IL001");
                return false;
        }
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
