using Kong.Parsing;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class IlCompiler
{
    public string? CompileProgramToMain(Program program, TypeInferenceResult types, ModuleDefinition module, MethodDefinition mainMethod)
    {
        if (program.Statements.Count != 1)
        {
            return "Only a single expression statement is supported for now.";
        }
        var statement = program.Statements[0];
        if (statement is not ExpressionStatement es || es.Expression is null)
        {
            return "Only a single expression statement is supported for now.";
        }
        var il = mainMethod.Body.GetILProcessor();
        var emitErr = EmitExpression(es.Expression, types, il, module);
        if (emitErr is not null)
        {
            return emitErr;
        }
        var writeLineLong = module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])!);
        il.Emit(OpCodes.Call, writeLineLong);
        il.Emit(OpCodes.Ret);
        return null;
    }

    private string? EmitExpression(IExpression expression, TypeInferenceResult types, ILProcessor il, ModuleDefinition module)
    {
        switch (expression)
        {
            case IntegerLiteral intLit:
                il.Emit(OpCodes.Ldc_I8, intLit.Value);
                return null;

            case InfixExpression infix when infix.Operator == "+":
                var leftErr = EmitExpression(infix.Left, types, il, module);
                if (leftErr is not null) return leftErr;
                var rightErr = EmitExpression(infix.Right, types, il, module);
                if (rightErr is not null) return rightErr;
                il.Emit(OpCodes.Add);
                return null;

            default:
                return $"Unsupported expression type: {expression.GetType().Name}";
        }
    }
}
