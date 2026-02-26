using Kong.Parsing;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class IlCompiler
{
    public string? CompileProgramToMain(Program program, TypeInferenceResult types, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var il = mainMethod.Body.GetILProcessor();
        var locals = new Dictionary<string, VariableDefinition>();
        var lastPushesValue = false;

        for (int i = 0; i < program.Statements.Count; i++)
        {
            (var err, var pushesValue) = EmitStatement(program.Statements[i], types, il, module, mainMethod, locals);
            if (err is not null) return err;

            lastPushesValue = pushesValue;

            if (i < program.Statements.Count - 1 && pushesValue)
            {
                il.Emit(OpCodes.Pop);
            }
        }

        if (program.Statements.Count == 0 || !lastPushesValue)
        {
            return "Program must end with an expression that evaluates to Int64";
        }
        
        var resultType = types.GetNodeType(program);
        MethodReference writeLine;
        switch (resultType)
        {
            case KongType.Boolean:
                writeLine = module.ImportReference(
                    typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(bool)])!);
                break;
            case KongType.Int64:
                writeLine = module.ImportReference(
                    typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])!);
                break;
            default:
                return $"Unsupported program result type: {resultType}";
        }
        il.Emit(OpCodes.Call, writeLine);
        il.Emit(OpCodes.Ret);
        return null;
    }

    private string? EmitExpression(IExpression expression, TypeInferenceResult types, ILProcessor il, ModuleDefinition module, Dictionary<string, VariableDefinition> locals)
    {
        switch (expression)
        {
            case IntegerLiteral intLit:
                il.Emit(OpCodes.Ldc_I8, intLit.Value);
                return null;

            case BooleanLiteral boolLit:
                il.Emit(boolLit.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                return null;

            case InfixExpression plus when plus.Operator == "+":
                var leftErr = EmitExpression(plus.Left, types, il, module, locals);
                if (leftErr is not null) return leftErr;
                var rightErr = EmitExpression(plus.Right, types, il, module, locals);
                if (rightErr is not null) return rightErr;
                il.Emit(OpCodes.Add);
                return null;
            
            case InfixExpression minus when minus.Operator == "-":
                var leftErr2 = EmitExpression(minus.Left, types, il, module, locals);
                if (leftErr2 is not null) return leftErr2;
                var rightErr2 = EmitExpression(minus.Right, types, il, module, locals);
                if (rightErr2 is not null) return rightErr2;
                il.Emit(OpCodes.Sub);
                return null;

            case InfixExpression times when times.Operator == "*":
                var leftErr3 = EmitExpression(times.Left, types, il, module, locals);
                if (leftErr3 is not null) return leftErr3;
                var rightErr3 = EmitExpression(times.Right, types, il, module, locals);
                if (rightErr3 is not null) return rightErr3;
                il.Emit(OpCodes.Mul);
                return null;

            case InfixExpression div when div.Operator == "/":
                var leftErr4 = EmitExpression(div.Left, types, il, module, locals);
                if (leftErr4 is not null) return leftErr4;
                var rightErr4 = EmitExpression(div.Right, types, il, module, locals);
                if (rightErr4 is not null) return rightErr4;
                il.Emit(OpCodes.Div);
                return null;

            case InfixExpression eq when eq.Operator == "==":
                var leftErr5 = EmitExpression(eq.Left, types, il, module, locals);
                if (leftErr5 is not null) return leftErr5;
                var rightErr5 = EmitExpression(eq.Right, types, il, module, locals);
                if (rightErr5 is not null) return rightErr5;
                il.Emit(OpCodes.Ceq);
                return null;

            case InfixExpression neq when neq.Operator == "!=":
                var leftErr6 = EmitExpression(neq.Left, types, il, module, locals);
                if (leftErr6 is not null) return leftErr6;
                var rightErr6 = EmitExpression(neq.Right, types, il, module, locals);
                if (rightErr6 is not null) return rightErr6;
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                return null;

            case InfixExpression lt when lt.Operator == "<":
                var leftErr7 = EmitExpression(lt.Left, types, il, module, locals);
                if (leftErr7 is not null) return leftErr7;
                var rightErr7 = EmitExpression(lt.Right, types, il, module, locals);
                if (rightErr7 is not null) return rightErr7;
                il.Emit(OpCodes.Clt);
                return null;

            case InfixExpression gt when gt.Operator == ">":
                var leftErr8 = EmitExpression(gt.Left, types, il, module, locals);
                if (leftErr8 is not null) return leftErr8;
                var rightErr8 = EmitExpression(gt.Right, types, il, module, locals);
                if (rightErr8 is not null) return rightErr8;
                il.Emit(OpCodes.Cgt);
                return null;
            
            case Identifier id:
                if (!locals.TryGetValue(id.Value, out var local))
                {
                    return $"Undefined variable: {id.Value}";
                }
                il.Emit(OpCodes.Ldloc, local);
                return null;

            case PrefixExpression prefix when prefix.Operator == "-":
                var operandErr = EmitExpression(prefix.Right, types, il, module, locals);
                if (operandErr is not null) return operandErr;
                il.Emit(OpCodes.Neg);
                return null;

            case PrefixExpression prefix when prefix.Operator == "!":
                var operandErr2 = EmitExpression(prefix.Right, types, il, module, locals);
                if (operandErr2 is not null) return operandErr2;
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                return null;

            default:
                return $"Unsupported expression type: {expression.GetType().Name}";
        }
    }

    private (string? err, bool pushesValue) EmitStatement(IStatement statement, TypeInferenceResult types, ILProcessor il, ModuleDefinition module, MethodDefinition mainMethod, Dictionary<string, VariableDefinition> locals)
    {
        switch (statement)
        {
            case ExpressionStatement es when es.Expression is not null:
                var err = EmitExpression(es.Expression, types, il, module, locals);
                return (err, err is null);
            
            case LetStatement ls when ls.Value is not null:
                var letErr = EmitLetStatement(ls, types, il, module, mainMethod, locals);
                return (letErr, false);

            default:
                return ($"Unsupported statement type: {statement.GetType().Name}", false);
        }
    }

    private string? EmitLetStatement(LetStatement ls, TypeInferenceResult types, ILProcessor il, ModuleDefinition module, MethodDefinition mainMethod, Dictionary<string, VariableDefinition> locals)
    {
        var valueErr = EmitExpression(ls.Value!, types, il, module, locals);
        if (valueErr is not null)
        {
            return valueErr;
        }

        if (!locals.TryGetValue(ls.Name.Value, out var local))
        {
            local = new VariableDefinition(module.TypeSystem.Int64);
            mainMethod.Body.Variables.Add(local);
            locals[ls.Name.Value] = local;
        }

        il.Emit(OpCodes.Stloc, local);
        return null;
    }
}
