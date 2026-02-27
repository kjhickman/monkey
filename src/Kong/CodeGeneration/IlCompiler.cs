using Kong.Parsing;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class IlCompiler
{
    private sealed class EmitContext
    {
        public EmitContext(TypeInferenceResult types, ModuleDefinition module, MethodDefinition mainMethod)
        {
            Types = types;
            Module = module;
            MainMethod = mainMethod;
            Il = mainMethod.Body.GetILProcessor();
            Locals = [];
            ObjectArrayType = new ArrayType(module.TypeSystem.Object);
        }

        public TypeInferenceResult Types { get; }
        public ModuleDefinition Module { get; }
        public MethodDefinition MainMethod { get; }
        public ILProcessor Il { get; }
        public Dictionary<string, VariableDefinition> Locals { get; }
        public ArrayType ObjectArrayType { get; }
    }

    public string? CompileProgramToMain(Program program, TypeInferenceResult types, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var context = new EmitContext(types, module, mainMethod);
        var lastPushesValue = false;

        for (int i = 0; i < program.Statements.Count; i++)
        {
            (var err, var pushesValue) = EmitStatement(program.Statements[i], context);
            if (err is not null)
            {
                return err;
            }

            lastPushesValue = pushesValue;

            if (i < program.Statements.Count - 1 && pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        EmitProgramResultAndReturn(program, lastPushesValue, context);
        return null;
    }

    private string? EmitExpression(IExpression expression, EmitContext context)
    {
        return expression switch
        {
            Identifier id => EmitIdentifier(id, context),
            IntegerLiteral intLit => EmitIntegerLiteral(intLit, context),
            BooleanLiteral boolLit => EmitBooleanLiteral(boolLit, context),
            StringLiteral strLit => EmitStringLiteral(strLit, context),
            ArrayLiteral arrayLit => EmitArrayLiteral(arrayLit, context),
            InfixExpression plus when plus.Operator == "+" => EmitPlusExpression(plus, context),
            InfixExpression minus when minus.Operator == "-" => EmitBinaryExpression(minus, OpCodes.Sub, context),
            InfixExpression times when times.Operator == "*" => EmitBinaryExpression(times, OpCodes.Mul, context),
            InfixExpression div when div.Operator == "/" => EmitBinaryExpression(div, OpCodes.Div, context),
            InfixExpression eq when eq.Operator == "==" => EmitBinaryExpression(eq, OpCodes.Ceq, context),
            InfixExpression neq when neq.Operator == "!=" => EmitNotEqualExpression(neq, context),
            InfixExpression lt when lt.Operator == "<" => EmitBinaryExpression(lt, OpCodes.Clt, context),
            InfixExpression gt when gt.Operator == ">" => EmitBinaryExpression(gt, OpCodes.Cgt, context),
            PrefixExpression prefix when prefix.Operator == "-" => EmitNegatePrefix(prefix, context),
            PrefixExpression prefix when prefix.Operator == "!" => EmitBangPrefix(prefix, context),
            IfExpression ifExpr => EmitIfExpression(ifExpr, context),
            IndexExpression indexExpr => EmitIndexExpression(indexExpr, context),
            _ => $"Unsupported expression type: {expression.GetType().Name}",
        };
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

    private (string? err, bool pushesValue) EmitStatement(IStatement statement, EmitContext context)
    {
        switch (statement)
        {
            case ExpressionStatement { Expression: IfExpression ifExpr } when ifExpr.Alternative is null:
                var ifStmtErr = EmitIfStatement(ifExpr, context);
                return (ifStmtErr, false);

            case ExpressionStatement es when es.Expression is not null:
                var err = EmitExpression(es.Expression, context);
                if (err is not null)
                {
                    return (err, false);
                }

                var expressionType = context.Types.GetNodeType(es.Expression);
                return (null, expressionType != KongType.Void);

            case LetStatement ls when ls.Value is not null:
                var letErr = EmitLetStatement(ls, context);
                return (letErr, false);

            default:
                return ($"Unsupported statement type: {statement.GetType().Name}", false);
        }
    }

    private string? EmitLetStatement(LetStatement ls, EmitContext context)
    {
        var valueErr = EmitExpression(ls.Value!, context);
        if (valueErr is not null)
        {
            return valueErr;
        }

        if (!context.Locals.TryGetValue(ls.Name.Value, out var local))
        {
            var valueType = context.Types.GetNodeType(ls.Value!);
            var localType = ResolveLocalType(valueType, context.Module);

            local = new VariableDefinition(localType);
            context.MainMethod.Body.Variables.Add(local);
            context.Locals[ls.Name.Value] = local;
        }

        context.Il.Emit(OpCodes.Stloc, local);
        return null;
    }

    private static TypeReference ResolveLocalType(KongType valueType, ModuleDefinition module)
    {
        return valueType switch
        {
            KongType.Int64 => module.TypeSystem.Int64,
            KongType.Boolean => module.TypeSystem.Boolean,
            KongType.Void => module.TypeSystem.Object,
            KongType.String => module.TypeSystem.String,
            KongType.Array => new ArrayType(module.TypeSystem.Object),
            _ => module.TypeSystem.Object,
        };
    }

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

            EmitBoxIfNeeded(context.Types.GetNodeType(arrayLiteral.Elements[i]), context);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        return null;
    }

    private string? EmitPlusExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        var plusType = context.Types.GetNodeType(expression);
        switch (plusType)
        {
            case KongType.Int64:
                context.Il.Emit(OpCodes.Add);
                return null;
            case KongType.String:
                var concatMethod = context.Module.ImportReference(
                    typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!);
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

    private string? EmitNotEqualExpression(InfixExpression expression, EmitContext context)
    {
        var operandsErr = EmitBinaryOperands(expression, context);
        if (operandsErr is not null)
        {
            return operandsErr;
        }

        context.Il.Emit(OpCodes.Ceq);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ceq);
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

    private static string? EmitIdentifier(Identifier identifier, EmitContext context)
    {
        if (!context.Locals.TryGetValue(identifier.Value, out var local))
        {
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

    private string? EmitIndexExpression(IndexExpression indexExpression, EmitContext context)
    {
        var leftType = context.Types.GetNodeType(indexExpression.Left);
        if (leftType is not KongType.Array and not KongType.Unknown)
        {
            return $"Index operator not supported for type: {leftType}";
        }

        var indexType = context.Types.GetNodeType(indexExpression.Index);
        if (indexType is not KongType.Int64 and not KongType.Unknown)
        {
            return $"Array index must be Int64, got: {indexType}";
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
        return null;
    }

    private static void EmitProgramResultAndReturn(Program program, bool lastPushesValue, EmitContext context)
    {
        if (program.Statements.Count == 0 || !lastPushesValue)
        {
            EmitWriteLineString(context, "no value");
            context.Il.Emit(OpCodes.Ret);
            return;
        }

        var resultType = context.Types.GetNodeType(program);
        if (!EmitWriteLineForTypedResult(resultType, context))
        {
            EmitWriteLineForObjectResult(context);
        }

        context.Il.Emit(OpCodes.Ret);
    }

    private static bool EmitWriteLineForTypedResult(KongType resultType, EmitContext context)
    {
        var writeLine = ResolveWriteLineMethod(resultType, context.Module);
        if (writeLine is null)
        {
            return false;
        }

        context.Il.Emit(OpCodes.Call, writeLine);
        return true;
    }

    private static MethodReference? ResolveWriteLineMethod(KongType resultType, ModuleDefinition module)
    {
        return resultType switch
        {
            KongType.Boolean => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(bool)])!),
            KongType.Int64 => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])!),
            KongType.String => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!),
            _ => null,
        };
    }

    private static void EmitWriteLineForObjectResult(EmitContext context)
    {
        var objectLocal = new VariableDefinition(context.Module.TypeSystem.Object);
        var arrayLocal = new VariableDefinition(context.ObjectArrayType);
        context.MainMethod.Body.Variables.Add(objectLocal);
        context.MainMethod.Body.Variables.Add(arrayLocal);

        var writeLineObject = context.Module.ImportReference(
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(object)])!);
        var stringJoinObjectArray = context.Module.ImportReference(
            typeof(string).GetMethod(nameof(string.Join), [typeof(string), typeof(object[])])!);
        var stringConcatThree = context.Module.ImportReference(
            typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!);

        var notArrayLabel = context.Il.Create(OpCodes.Nop);
        var endLabel = context.Il.Create(OpCodes.Nop);

        context.Il.Emit(OpCodes.Stloc, objectLocal);
        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Isinst, context.ObjectArrayType);
        context.Il.Emit(OpCodes.Stloc, arrayLocal);
        context.Il.Emit(OpCodes.Ldloc, arrayLocal);
        context.Il.Emit(OpCodes.Brfalse, notArrayLabel);

        context.Il.Emit(OpCodes.Ldstr, "[");
        context.Il.Emit(OpCodes.Ldstr, ", ");
        context.Il.Emit(OpCodes.Ldloc, arrayLocal);
        context.Il.Emit(OpCodes.Call, stringJoinObjectArray);
        context.Il.Emit(OpCodes.Ldstr, "]");
        context.Il.Emit(OpCodes.Call, stringConcatThree);
        context.Il.Emit(OpCodes.Call, ResolveWriteLineMethod(KongType.String, context.Module)!);
        context.Il.Emit(OpCodes.Br, endLabel);

        context.Il.Append(notArrayLabel);
        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Call, writeLineObject);

        context.Il.Append(endLabel);
    }

    private static void EmitWriteLineString(EmitContext context, string value)
    {
        context.Il.Emit(OpCodes.Ldstr, value);
        context.Il.Emit(OpCodes.Call, ResolveWriteLineMethod(KongType.String, context.Module)!);
    }

    private static void EmitBoxIfNeeded(KongType type, EmitContext context)
    {
        switch (type)
        {
            case KongType.Int64:
                context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Int64);
                break;
            case KongType.Boolean:
                context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Boolean);
                break;
        }
    }

    private static void EmitCastToObjectArray(EmitContext context)
    {
        context.Il.Emit(OpCodes.Castclass, context.ObjectArrayType);
    }

    private static void EmitConvertInt64ToInt32(EmitContext context)
    {
        context.Il.Emit(OpCodes.Conv_I4);
    }
}
