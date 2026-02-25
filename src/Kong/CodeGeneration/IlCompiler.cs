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

            case InfixExpression plus when plus.Operator == "+":
                var leftErr = EmitExpression(plus.Left, types, il, module);
                if (leftErr is not null) return leftErr;
                var rightErr = EmitExpression(plus.Right, types, il, module);
                if (rightErr is not null) return rightErr;
                il.Emit(OpCodes.Add);
                return null;
            
            case InfixExpression minus when minus.Operator == "-":
                var leftErr2 = EmitExpression(minus.Left, types, il, module);
                if (leftErr2 is not null) return leftErr2;
                var rightErr2 = EmitExpression(minus.Right, types, il, module);
                if (rightErr2 is not null) return rightErr2;
                il.Emit(OpCodes.Sub);
                return null;

            case InfixExpression times when times.Operator == "*":
                var leftErr3 = EmitExpression(times.Left, types, il, module);
                if (leftErr3 is not null) return leftErr3;
                var rightErr3 = EmitExpression(times.Right, types, il, module);
                if (rightErr3 is not null) return rightErr3;
                il.Emit(OpCodes.Mul);
                return null;

            case InfixExpression div when div.Operator == "/":
                var leftErr4 = EmitExpression(div.Left, types, il, module);
                if (leftErr4 is not null) return leftErr4;
                var rightErr4 = EmitExpression(div.Right, types, il, module);
                if (rightErr4 is not null) return rightErr4;
                il.Emit(OpCodes.Div);
                return null;

            default:
                return $"Unsupported expression type: {expression.GetType().Name}";
        }
    }
}
