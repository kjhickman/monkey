using Kong.Lowering;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private int _nextClosureId;

    public string? CompileProgramToMain(Program program, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var functions = new Dictionary<string, FunctionMetadata>();
        var functionDeclErr = DeclareTopLevelFunctions(program, module, functions);
        if (functionDeclErr is not null)
        {
            return functionDeclErr;
        }

        var functionBodyErr = CompileFunctionBodies(program, module, functions);
        if (functionBodyErr is not null)
        {
            return functionBodyErr;
        }

        var context = new EmitContext(module, mainMethod, functions);

        for (int i = 0; i < program.Statements.Count; i++)
        {
            if (IsTopLevelFunctionDeclaration(program.Statements[i]))
            {
                continue;
            }

            (var err, var pushesValue) = EmitStatement(program.Statements[i], context);
            if (err is not null)
            {
                return err;
            }

            if (pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        context.Il.Emit(OpCodes.Ret);
        return null;
    }

    private static bool IsTopLevelFunctionDeclaration(IStatement statement)
    {
        return statement is LetStatement { Value: FunctionLiteral };
    }

    private static string? DeclareTopLevelFunctions(Program program, ModuleDefinition module, Dictionary<string, FunctionMetadata> functions)
    {
        var programType = module.Types.FirstOrDefault(t => t.Name == "Program");
        if (programType is null)
        {
            return "unable to find Program type while declaring functions";
        }

        foreach (var statement in program.Statements)
        {
            if (statement is not LetStatement { Value: FunctionLiteral functionLiteral } letStatement)
            {
                continue;
            }

            if (!program.FunctionSignatures.TryGetValue(letStatement.Name.Value, out var signature))
            {
                signature = new FunctionSignature(letStatement.Name.Value, [], KongType.Unknown);
            }

            var method = new MethodDefinition(
                letStatement.Name.Value,
                MethodAttributes.Public | MethodAttributes.Static,
                ResolveReturnType(signature.ReturnType, module));

            for (var i = 0; i < functionLiteral.Parameters.Count; i++)
            {
                var parameterName = functionLiteral.Parameters[i].Name.Value;
                var parameterType = i < signature.ParameterTypes.Count
                    ? ResolveLocalType(signature.ParameterTypes[i], module)
                    : module.TypeSystem.Object;

                method.Parameters.Add(new ParameterDefinition(parameterName, ParameterAttributes.None, parameterType));
            }

            programType.Methods.Add(method);
            functions[letStatement.Name.Value] = new FunctionMetadata
            {
                Method = method,
                Signature = signature,
            };
        }

        return null;
    }

    private string? CompileFunctionBodies(Program program, ModuleDefinition module, Dictionary<string, FunctionMetadata> functions)
    {
        foreach (var statement in program.Statements)
        {
            if (statement is not LetStatement { Value: FunctionLiteral functionLiteral } letStatement)
            {
                continue;
            }

            var functionContext = new EmitContext(module, functions[letStatement.Name.Value].Method, functions);
            for (var i = 0; i < functionLiteral.Parameters.Count; i++)
            {
                var parameter = functionLiteral.Parameters[i];
                functionContext.Parameters[parameter.Name.Value] = functionContext.Method.Parameters[i];
            }

            var functionBodyErr = EmitFunctionBody(functionLiteral.Body, functionContext);
            if (functionBodyErr is not null)
            {
                return functionBodyErr;
            }
        }

        return null;
    }

    private string? EmitFunctionBody(BlockStatement body, EmitContext context)
    {
        var lastPushesValue = false;
        IExpression? lastExpression = null;

        for (var i = 0; i < body.Statements.Count; i++)
        {
            var statement = body.Statements[i];
            (var err, var pushesValue) = EmitStatement(statement, context);
            if (err is not null)
            {
                return err;
            }

            lastPushesValue = pushesValue;
            lastExpression = statement is ExpressionStatement es ? es.Expression : null;

            if (i < body.Statements.Count - 1 && pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        if (context.Method.Body.Instructions.Count > 0 && context.Method.Body.Instructions[^1].OpCode == OpCodes.Ret)
        {
            return null;
        }

        if (lastPushesValue && lastExpression is not null)
        {
            EmitConversionIfNeeded(lastExpression.Type, context.Method.ReturnType, context);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        if (context.Method.ReturnType.FullName == context.Module.TypeSystem.Object.FullName)
        {
            context.Il.Emit(OpCodes.Ldnull);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        if (context.Method.ReturnType.FullName == context.Module.TypeSystem.Int64.FullName)
        {
            context.Il.Emit(OpCodes.Ldc_I8, 0L);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        if (context.Method.ReturnType.FullName == context.Module.TypeSystem.Boolean.FullName)
        {
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        if (context.Method.ReturnType.FullName == context.Module.TypeSystem.Char.FullName)
        {
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        if (context.Method.ReturnType.FullName == context.Module.TypeSystem.Double.FullName)
        {
            context.Il.Emit(OpCodes.Ldc_R8, 0.0);
            context.Il.Emit(OpCodes.Ret);
            return null;
        }

        context.Il.Emit(OpCodes.Ldnull);
        context.Il.Emit(OpCodes.Ret);
        return null;
    }

    private string? EmitExpression(IExpression expression, EmitContext context)
    {
        return expression switch
        {
            Identifier id => EmitIdentifier(id, context),
            IntegerLiteral intLit => EmitIntegerLiteral(intLit, context),
            BooleanLiteral boolLit => EmitBooleanLiteral(boolLit, context),
            CharLiteral charLit => EmitCharLiteral(charLit, context),
            DoubleLiteral dblLit => EmitDoubleLiteral(dblLit, context),
            StringLiteral strLit => EmitStringLiteral(strLit, context),
            ArrayLiteral arrayLit => EmitArrayLiteral(arrayLit, context),
            HashLiteral hashLiteral => EmitHashLiteral(hashLiteral, context),
            InfixExpression plus when plus.Operator == "+" => EmitPlusExpression(plus, context),
            InfixExpression minus when minus.Operator == "-" => EmitBinaryExpression(minus, OpCodes.Sub, context),
            InfixExpression times when times.Operator == "*" => EmitBinaryExpression(times, OpCodes.Mul, context),
            InfixExpression div when div.Operator == "/" => EmitBinaryExpression(div, OpCodes.Div, context),
            InfixExpression mod when mod.Operator == "%" => EmitBinaryExpression(mod, OpCodes.Rem, context),
            InfixExpression eq when eq.Operator == "==" => EmitEqualExpression(eq, context),
            InfixExpression neq when neq.Operator == "!=" => EmitNotEqualExpression(neq, context),
            InfixExpression lt when lt.Operator == "<" => EmitLessThanExpression(lt, context),
            InfixExpression gt when gt.Operator == ">" => EmitGreaterThanExpression(gt, context),
            InfixExpression logicalAnd when logicalAnd.Operator == "&&" => EmitLogicalAndExpression(logicalAnd, context),
            InfixExpression logicalOr when logicalOr.Operator == "||" => EmitLogicalOrExpression(logicalOr, context),
            PrefixExpression prefix when prefix.Operator == "-" => EmitNegatePrefix(prefix, context),
            PrefixExpression prefix when prefix.Operator == "!" => EmitBangPrefix(prefix, context),
            IfExpression ifExpr => EmitIfExpression(ifExpr, context),
            IndexExpression indexExpr => EmitIndexExpression(indexExpr, context),
            CallExpression callExpression => EmitCallExpression(callExpression, context),
            IntrinsicCallExpression intrinsicCall => EmitIntrinsicCallExpression(intrinsicCall, context),
            FunctionLiteral functionLiteral => EmitFunctionLiteralExpression(functionLiteral, context),
            _ => $"Unsupported expression type: {expression.GetType().Name}",
        };
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

                var expressionType = es.Expression.Type;
                return (null, expressionType != KongType.Void);

            case LetStatement ls when ls.Value is not null:
                var letErr = EmitLetStatement(ls, context);
                return (letErr, false);

            case AssignStatement assignStatement:
                var assignErr = EmitAssignStatement(assignStatement, context);
                return (assignErr, false);

            case IfStatement ifStatement:
                var loweredIfErr = EmitIfStatement(ifStatement, context);
                return (loweredIfErr, false);

            case ReturnStatement rs when rs.ReturnValue is not null:
                var returnErr = EmitReturnStatement(rs, context);
                return (returnErr, false);

            default:
                return ($"Unsupported statement type: {statement.GetType().Name}", false);
        }
    }
}
