using Kong.Lowering;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private string? EmitLetStatement(LetStatement ls, EmitContext context)
    {
        var valueErr = EmitExpression(ls.Value!, context);
        if (valueErr is not null)
        {
            return valueErr;
        }

        if (!context.Locals.TryGetValue(ls.Name.Value, out var local))
        {
            var valueType = ls.Value!.Type;
            var localType = ResolveLocalType(valueType, context.Module);

            local = new VariableDefinition(localType);
            context.Method.Body.Variables.Add(local);
            context.Locals[ls.Name.Value] = local;
        }

        context.Il.Emit(OpCodes.Stloc, local);
        return null;
    }

    private string? EmitAssignStatement(AssignStatement assignStatement, EmitContext context)
    {
        var valueErr = EmitExpression(assignStatement.Value, context);
        if (valueErr is not null)
        {
            return valueErr;
        }

        if (!context.Locals.TryGetValue(assignStatement.Name.Value, out var local))
        {
            return $"Undefined variable: {assignStatement.Name.Value}";
        }

        context.Il.Emit(OpCodes.Stloc, local);
        return null;
    }

    private string? EmitReturnStatement(ReturnStatement returnStatement, EmitContext context)
    {
        var emitErr = EmitExpression(returnStatement.ReturnValue!, context);
        if (emitErr is not null)
        {
            return emitErr;
        }

        EmitConversionIfNeeded(returnStatement.ReturnValue!.Type, context.Method.ReturnType, context);
        context.Il.Emit(OpCodes.Ret);
        return null;
    }

    private string? EmitIfStatement(IfExpression ifExpr, EmitContext context)
    {
        var conditionErr = EmitExpression(ifExpr.Condition, context);
        if (conditionErr is not null)
        {
            return conditionErr;
        }

        var endLabel = context.Il.Create(OpCodes.Nop);
        context.Il.Emit(OpCodes.Brfalse, endLabel);

        var thenErr = EmitBlockStatement(ifExpr.Consequence, context);
        if (thenErr is not null)
        {
            return thenErr;
        }

        context.Il.Append(endLabel);
        return null;
    }

    private string? EmitIfStatement(IfStatement ifStatement, EmitContext context)
    {
        var conditionErr = EmitExpression(ifStatement.Condition, context);
        if (conditionErr is not null)
        {
            return conditionErr;
        }

        var elseLabel = context.Il.Create(OpCodes.Nop);
        var endLabel = context.Il.Create(OpCodes.Nop);
        context.Il.Emit(OpCodes.Brfalse, elseLabel);

        var thenErr = EmitBlockStatement(ifStatement.Consequence, context);
        if (thenErr is not null)
        {
            return thenErr;
        }

        context.Il.Emit(OpCodes.Br, endLabel);
        context.Il.Append(elseLabel);

        var elseErr = EmitBlockStatement(ifStatement.Alternative, context);
        if (elseErr is not null)
        {
            return elseErr;
        }

        context.Il.Append(endLabel);
        return null;
    }

    private string? EmitBlockStatement(BlockStatement block, EmitContext context)
    {
        for (int i = 0; i < block.Statements.Count; i++)
        {
            (var err, var pushesValue) = EmitStatement(block.Statements[i], context);
            if (err is not null)
            {
                return err;
            }

            if (pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        return null;
    }
}
