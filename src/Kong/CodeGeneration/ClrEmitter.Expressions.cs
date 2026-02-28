using Kong.Lowering;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private static string? EmitIntegerLiteral(IntegerLiteral literal, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I8, literal.Value);
        return null;
    }

    private static string? EmitBooleanLiteral(BooleanLiteral literal, EmitContext context)
    {
        context.Il.Emit(literal.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        return null;
    }

    private static string? EmitStringLiteral(StringLiteral literal, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldstr, literal.Value);
        return null;
    }

    private string? EmitArrayLiteral(ArrayLiteral arrayLiteral, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, arrayLiteral.Elements.Count);
        context.Il.Emit(OpCodes.Newarr, context.Module.TypeSystem.Object);

        for (var i = 0; i < arrayLiteral.Elements.Count; i++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, i);

            var elementErr = EmitExpression(arrayLiteral.Elements[i], context);
            if (elementErr is not null)
            {
                return elementErr;
            }

            EmitBoxIfNeeded(arrayLiteral.Elements[i].Type, context);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        return null;
    }

    private string? EmitHashLiteral(HashLiteral hashLiteral, EmitContext context)
    {
        var hashMapType = ResolveHashMapType(context.Module);
        var hashMapCtor = context.Module.ImportReference(
            typeof(Dictionary<object, object>).GetConstructor(Type.EmptyTypes));
        var addMethod = context.Module.ImportReference(
            typeof(Dictionary<object, object>).GetMethod(nameof(Dictionary<object, object>.Add), [typeof(object), typeof(object)]));

        context.Il.Emit(OpCodes.Newobj, hashMapCtor);
        context.Il.Emit(OpCodes.Castclass, hashMapType);

        foreach (var pair in hashLiteral.Pairs)
        {
            context.Il.Emit(OpCodes.Dup);

            var keyErr = EmitExpression(pair.Key, context);
            if (keyErr is not null)
            {
                return keyErr;
            }

            EmitBoxIfNeeded(pair.Key.Type, context);

            var valueErr = EmitExpression(pair.Value, context);
            if (valueErr is not null)
            {
                return valueErr;
            }

            EmitBoxIfNeeded(pair.Value.Type, context);
            context.Il.Emit(OpCodes.Callvirt, addMethod);
        }

        return null;
    }

    private static string? EmitIdentifier(Identifier identifier, EmitContext context)
    {
        if (!context.Locals.TryGetValue(identifier.Value, out var local))
        {
            if (context.Parameters.TryGetValue(identifier.Value, out var parameter))
            {
                context.Il.Emit(OpCodes.Ldarg, parameter);
                return null;
            }

            if (context.ClosureParameterIndices.TryGetValue(identifier.Value, out var parameterIndex))
            {
                context.Il.Emit(OpCodes.Ldarg_1);
                context.Il.Emit(OpCodes.Ldc_I4, parameterIndex);
                context.Il.Emit(OpCodes.Ldelem_Ref);
                EmitReadFromObject(identifier.Type, context);
                return null;
            }

            if (context.ClosureCaptureIndices.TryGetValue(identifier.Value, out var captureIndex))
            {
                context.Il.Emit(OpCodes.Ldarg_0);
                context.Il.Emit(OpCodes.Ldc_I4, captureIndex);
                context.Il.Emit(OpCodes.Ldelem_Ref);
                EmitReadFromObject(identifier.Type, context);
                return null;
            }

            return $"Undefined variable: {identifier.Value}";
        }

        context.Il.Emit(OpCodes.Ldloc, local);
        return null;
    }

    private string? EmitNegatePrefix(PrefixExpression expression, EmitContext context)
    {
        var operandErr = EmitExpression(expression.Right, context);
        if (operandErr is not null)
        {
            return operandErr;
        }

        context.Il.Emit(OpCodes.Neg);
        return null;
    }

    private string? EmitBangPrefix(PrefixExpression expression, EmitContext context)
    {
        var operandErr = EmitExpression(expression.Right, context);
        if (operandErr is not null)
        {
            return operandErr;
        }

        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ceq);
        return null;
    }

    private string? EmitPlusExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        var plusType = expression.Type;
        switch (plusType)
        {
            case KongType.Int64:
                context.Il.Emit(OpCodes.Add);
                return null;
            case KongType.String:
                var concatMethod = context.Module.ImportReference(
                    typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)]));
                context.Il.Emit(OpCodes.Call, concatMethod);
                return null;
            default:
                return $"Unsupported type for + operator: {plusType}";
        }
    }

    private string? EmitBinaryExpression(InfixExpression expression, OpCode opcode, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        context.Il.Emit(opcode);
        return null;
    }

    private string? EmitEqualExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        if (expression.Left.Type == KongType.String)
        {
            var equalsMethod = context.Module.ImportReference(
                typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)]));
            context.Il.Emit(OpCodes.Call, equalsMethod);
            return null;
        }

        context.Il.Emit(OpCodes.Ceq);
        return null;
    }

    private string? EmitNotEqualExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        if (expression.Left.Type == KongType.String)
        {
            var equalsMethod = context.Module.ImportReference(
                typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)]));
            context.Il.Emit(OpCodes.Call, equalsMethod);
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Ceq);
            return null;
        }

        context.Il.Emit(OpCodes.Ceq);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ceq);
        return null;
    }

    private string? EmitLessThanExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        if (expression.Left.Type == KongType.String)
        {
            var compareMethod = context.Module.ImportReference(
                typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)]));
            context.Il.Emit(OpCodes.Call, compareMethod);
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Clt);
            return null;
        }

        context.Il.Emit(OpCodes.Clt);
        return null;
    }

    private string? EmitGreaterThanExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        if (expression.Left.Type == KongType.String)
        {
            var compareMethod = context.Module.ImportReference(
                typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)]));
            context.Il.Emit(OpCodes.Call, compareMethod);
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Cgt);
            return null;
        }

        context.Il.Emit(OpCodes.Cgt);
        return null;
    }

    private string? EmitBinaryOperands(InfixExpression expression, EmitContext context)
    {
        var leftErr = EmitExpression(expression.Left, context);
        if (leftErr is not null)
        {
            return leftErr;
        }

        return EmitExpression(expression.Right, context);
    }

    private string? EmitIfExpression(IfExpression ifExpr, EmitContext context)
    {
        if (ifExpr.Alternative is null)
        {
            return "If expression without else cannot be used as a value";
        }

        var conditionErr = EmitExpression(ifExpr.Condition, context);
        if (conditionErr is not null)
        {
            return conditionErr;
        }

        var elseLabel = context.Il.Create(OpCodes.Nop);
        var endLabel = context.Il.Create(OpCodes.Nop);

        context.Il.Emit(OpCodes.Brfalse, elseLabel);

        var thenErr = EmitBlockExpression(ifExpr.Consequence, context);
        if (thenErr is not null)
        {
            return thenErr;
        }

        context.Il.Emit(OpCodes.Br, endLabel);
        context.Il.Append(elseLabel);

        var elseErr = EmitBlockExpression(ifExpr.Alternative, context);
        if (elseErr is not null)
        {
            return elseErr;
        }

        context.Il.Append(endLabel);
        return null;
    }

    private string? EmitBlockExpression(BlockStatement block, EmitContext context)
    {
        var lastPushesValue = false;

        for (int i = 0; i < block.Statements.Count; i++)
        {
            (var err, var pushesValue) = EmitStatement(block.Statements[i], context);
            if (err is not null)
            {
                return err;
            }

            lastPushesValue = pushesValue;

            if (i < block.Statements.Count - 1 && pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        if (block.Statements.Count == 0 || !lastPushesValue)
        {
            return "If branch must end with an expression";
        }

        return null;
    }

    private string? EmitIndexExpression(IndexExpression indexExpression, EmitContext context)
    {
        var leftType = indexExpression.Left.Type;
        if (leftType is not KongType.Array and not KongType.HashMap and not KongType.Unknown)
        {
            return $"Index operator not supported for type: {leftType}";
        }

        var indexType = indexExpression.Index.Type;
        if (leftType == KongType.Array && indexType is not KongType.Int64 and not KongType.Unknown)
        {
            return $"Array index must be Int64, got: {indexType}";
        }

        if (leftType == KongType.HashMap && indexType is not KongType.Int64 and not KongType.Boolean and not KongType.String and not KongType.Unknown)
        {
            return $"Hash map index must be Int64, Boolean, or String, got: {indexType}";
        }

        if (leftType == KongType.HashMap)
        {
            return EmitHashMapIndexExpression(indexExpression, context, indexType);
        }

        var indexedErr = EmitExpression(indexExpression.Left, context);
        if (indexedErr is not null)
        {
            return indexedErr;
        }

        EmitCastToObjectArray(context);

        var indexErr = EmitExpression(indexExpression.Index, context);
        if (indexErr is not null)
        {
            return indexErr;
        }

        EmitConvertInt64ToInt32(context);
        context.Il.Emit(OpCodes.Ldelem_Ref);
        EmitReadFromObject(indexExpression.Type, context);
        return null;
    }

    private string? EmitHashMapIndexExpression(IndexExpression indexExpression, EmitContext context, KongType indexType)
    {
        var hashMapType = ResolveHashMapType(context.Module);
        var keyType = context.Module.TypeSystem.Object;
        var hashMapLocal = new VariableDefinition(hashMapType);
        var keyLocal = new VariableDefinition(keyType);
        context.Method.Body.Variables.Add(hashMapLocal);
        context.Method.Body.Variables.Add(keyLocal);

        var containsKeyMethod = context.Module.ImportReference(
            typeof(Dictionary<object, object>).GetMethod(nameof(Dictionary<object, object>.ContainsKey), [typeof(object)])!);
        var itemGetterMethod = context.Module.ImportReference(
            typeof(Dictionary<object, object>).GetProperty("Item")!.GetGetMethod()!);
        var keyNotFoundCtor = context.Module.ImportReference(
            typeof(KeyNotFoundException).GetConstructor([typeof(string)])!);

        var keyMissingLabel = context.Il.Create(OpCodes.Nop);
        var endLabel = context.Il.Create(OpCodes.Nop);

        var hashMapErr = EmitExpression(indexExpression.Left, context);
        if (hashMapErr is not null)
        {
            return hashMapErr;
        }

        context.Il.Emit(OpCodes.Castclass, hashMapType);
        context.Il.Emit(OpCodes.Stloc, hashMapLocal);

        var keyErr = EmitExpression(indexExpression.Index, context);
        if (keyErr is not null)
        {
            return keyErr;
        }

        EmitBoxIfNeeded(indexType, context);
        context.Il.Emit(OpCodes.Stloc, keyLocal);

        context.Il.Emit(OpCodes.Ldloc, hashMapLocal);
        context.Il.Emit(OpCodes.Ldloc, keyLocal);
        context.Il.Emit(OpCodes.Callvirt, containsKeyMethod);
        context.Il.Emit(OpCodes.Brfalse, keyMissingLabel);

        context.Il.Emit(OpCodes.Ldloc, hashMapLocal);
        context.Il.Emit(OpCodes.Ldloc, keyLocal);
        context.Il.Emit(OpCodes.Callvirt, itemGetterMethod);
        context.Il.Emit(OpCodes.Br, endLabel);

        context.Il.Append(keyMissingLabel);
        context.Il.Emit(OpCodes.Ldstr, "key not found in hash map");
        context.Il.Emit(OpCodes.Newobj, keyNotFoundCtor);
        context.Il.Emit(OpCodes.Throw);

        context.Il.Append(endLabel);
        return null;
    }
}
