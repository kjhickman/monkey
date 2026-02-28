using Kong.Parsing;
using Kong.Semantics;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class IlCompiler
{
    private sealed class FunctionMetadata
    {
        public required MethodDefinition Method { get; init; }
        public required TypeInferenceResult.FunctionSignature Signature { get; init; }
    }

    private sealed class EmitContext
    {
        public EmitContext(TypeInferenceResult types, ModuleDefinition module, MethodDefinition method, Dictionary<string, FunctionMetadata> functions)
        {
            Types = types;
            Module = module;
            Method = method;
            Il = method.Body.GetILProcessor();
            Locals = [];
            Parameters = [];
            Functions = functions;
            ObjectArrayType = new ArrayType(module.TypeSystem.Object);

            foreach (var parameter in method.Parameters)
            {
                Parameters[parameter.Name] = parameter;
            }
        }

        public TypeInferenceResult Types { get; }
        public ModuleDefinition Module { get; }
        public MethodDefinition Method { get; }
        public ILProcessor Il { get; }
        public Dictionary<string, VariableDefinition> Locals { get; }
        public Dictionary<string, ParameterDefinition> Parameters { get; }
        public Dictionary<string, FunctionMetadata> Functions { get; }
        public ArrayType ObjectArrayType { get; }
    }

    public string? CompileProgramToMain(Program program, TypeInferenceResult types, ModuleDefinition module, MethodDefinition mainMethod)
    {
        var functions = new Dictionary<string, FunctionMetadata>();
        var functionDeclErr = DeclareTopLevelFunctions(program, module, functions, types);
        if (functionDeclErr is not null)
        {
            return functionDeclErr;
        }

        var functionBodyErr = CompileFunctionBodies(program, module, functions, types);
        if (functionBodyErr is not null)
        {
            return functionBodyErr;
        }

        var context = new EmitContext(types, module, mainMethod, functions);
        var lastPushesValue = false;
        var emittedStatementCount = 0;
        var lastResultType = KongType.Unknown;

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

            lastPushesValue = pushesValue;
            emittedStatementCount++;

            if (program.Statements[i] is ExpressionStatement { Expression: not null } expressionStatement)
            {
                lastResultType = context.Types.GetNodeType(expressionStatement.Expression);
            }
            else
            {
                lastResultType = KongType.Unknown;
            }

            if (HasRemainingNonFunctionStatements(program, i) && pushesValue)
            {
                context.Il.Emit(OpCodes.Pop);
            }
        }

        EmitProgramResultAndReturn(emittedStatementCount, lastPushesValue, lastResultType, context);
        return null;
    }

    private static bool IsTopLevelFunctionDeclaration(IStatement statement)
    {
        return statement is LetStatement { Value: FunctionLiteral };
    }

    private static bool HasRemainingNonFunctionStatements(Program program, int currentIndex)
    {
        for (var i = currentIndex + 1; i < program.Statements.Count; i++)
        {
            if (!IsTopLevelFunctionDeclaration(program.Statements[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? DeclareTopLevelFunctions(Program program, ModuleDefinition module, Dictionary<string, FunctionMetadata> functions, TypeInferenceResult types)
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

            if (!types.TryGetFunctionSignature(letStatement.Name.Value, out var signature))
            {
                signature = new TypeInferenceResult.FunctionSignature(letStatement.Name.Value, [], KongType.Unknown);
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

    private string? CompileFunctionBodies(Program program, ModuleDefinition module, Dictionary<string, FunctionMetadata> functions, TypeInferenceResult types)
    {
        foreach (var statement in program.Statements)
        {
            if (statement is not LetStatement { Value: FunctionLiteral functionLiteral } letStatement)
            {
                continue;
            }

            var functionContext = new EmitContext(types, module, functions[letStatement.Name.Value].Method, functions);
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
            EmitConversionIfNeeded(context.Types.GetNodeType(lastExpression), context.Method.ReturnType, context);
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
            StringLiteral strLit => EmitStringLiteral(strLit, context),
            ArrayLiteral arrayLit => EmitArrayLiteral(arrayLit, context),
            HashLiteral hashLiteral => EmitHashLiteral(hashLiteral, context),
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
            CallExpression callExpression => EmitCallExpression(callExpression, context),
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
                if (ls.Value is FunctionLiteral)
                {
                    return ("nested function declarations are not supported in CLR backend", false);
                }

                var letErr = EmitLetStatement(ls, context);
                return (letErr, false);

            case ReturnStatement rs when rs.ReturnValue is not null:
                var returnErr = EmitReturnStatement(rs, context);
                return (returnErr, false);

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
            context.Method.Body.Variables.Add(local);
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
            KongType.HashMap => ResolveHashMapType(module),
            _ => module.TypeSystem.Object,
        };
    }

    private static TypeReference ResolveReturnType(KongType returnType, ModuleDefinition module)
    {
        return returnType switch
        {
            KongType.Int64 => module.TypeSystem.Int64,
            KongType.Boolean => module.TypeSystem.Boolean,
            KongType.String => module.TypeSystem.String,
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

            EmitBoxIfNeeded(context.Types.GetNodeType(pair.Key), context);

            var valueErr = EmitExpression(pair.Value, context);
            if (valueErr is not null)
            {
                return valueErr;
            }

            EmitBoxIfNeeded(context.Types.GetNodeType(pair.Value), context);
            context.Il.Emit(OpCodes.Callvirt, addMethod);
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
            if (context.Parameters.TryGetValue(identifier.Value, out var parameter))
            {
                context.Il.Emit(OpCodes.Ldarg, parameter);
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

    private string? EmitIndexExpression(IndexExpression indexExpression, EmitContext context)
    {
        var leftType = context.Types.GetNodeType(indexExpression.Left);
        if (leftType is not KongType.Array and not KongType.HashMap and not KongType.Unknown)
        {
            return $"Index operator not supported for type: {leftType}";
        }

        var indexType = context.Types.GetNodeType(indexExpression.Index);
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
        context.Il.Emit(OpCodes.Ldnull);

        context.Il.Append(endLabel);
        return null;
    }

    private string? EmitCallExpression(CallExpression callExpression, EmitContext context)
    {
        if (callExpression.Function is not Identifier functionIdentifier)
        {
            return "functions must be called by name in CLR backend";
        }

        if (!context.Functions.TryGetValue(functionIdentifier.Value, out var functionMetadata))
        {
            return $"Undefined function: {functionIdentifier.Value}";
        }

        if (callExpression.Arguments.Count != functionMetadata.Method.Parameters.Count)
        {
            return $"wrong number of arguments for {functionIdentifier.Value}: want={functionMetadata.Method.Parameters.Count}, got={callExpression.Arguments.Count}";
        }

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            var argument = callExpression.Arguments[i];
            var argumentErr = EmitExpression(argument, context);
            if (argumentErr is not null)
            {
                return argumentErr;
            }

            var expectedType = functionMetadata.Method.Parameters[i].ParameterType;
            var argumentType = context.Types.GetNodeType(argument);
            EmitConversionIfNeeded(argumentType, expectedType, context);
        }

        context.Il.Emit(OpCodes.Call, functionMetadata.Method);

        var callType = context.Types.GetNodeType(callExpression);
        EmitTypedReadFromObjectIfNeeded(callType, context);
        return null;
    }

    private string? EmitReturnStatement(ReturnStatement returnStatement, EmitContext context)
    {
        var emitErr = EmitExpression(returnStatement.ReturnValue!, context);
        if (emitErr is not null)
        {
            return emitErr;
        }

        EmitConversionIfNeeded(context.Types.GetNodeType(returnStatement.ReturnValue!), context.Method.ReturnType, context);
        context.Il.Emit(OpCodes.Ret);
        return null;
    }

    private static void EmitConversionIfNeeded(KongType actualType, TypeReference expectedType, EmitContext context)
    {
        if (expectedType.FullName == context.Module.TypeSystem.Object.FullName)
        {
            EmitBoxIfNeeded(actualType, context);
            return;
        }

        if (expectedType.FullName == context.Module.TypeSystem.Int64.FullName)
        {
            return;
        }

        if (expectedType.FullName == context.Module.TypeSystem.Boolean.FullName)
        {
            return;
        }

        context.Il.Emit(OpCodes.Castclass, expectedType);
    }

    private static void EmitTypedReadFromObjectIfNeeded(KongType type, EmitContext context)
    {
        if (context.Module.TypeSystem.Object.FullName != context.Method.ReturnType.FullName)
        {
            return;
        }

        switch (type)
        {
            case KongType.Int64:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Int64);
                break;
            case KongType.Boolean:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Boolean);
                break;
            case KongType.String:
                context.Il.Emit(OpCodes.Castclass, context.Module.TypeSystem.String);
                break;
        }
    }

    private static void EmitProgramResultAndReturn(int statementCount, bool lastPushesValue, KongType resultType, EmitContext context)
    {
        if (statementCount == 0 || !lastPushesValue)
        {
            EmitWriteLineString(context, "no value");
            context.Il.Emit(OpCodes.Ret);
            return;
        }

        if (!EmitWriteLineForTypedResult(resultType, context))
        {
            EmitBoxIfNeeded(resultType, context);
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
            KongType.Boolean => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(bool)])),
            KongType.Int64 => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])),
            KongType.String => module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])),
            _ => null,
        };
    }

    private static void EmitWriteLineForObjectResult(EmitContext context)
    {
        var objectLocal = new VariableDefinition(context.Module.TypeSystem.Object);
        var arrayLocal = new VariableDefinition(context.ObjectArrayType);
        var hashMapType = ResolveHashMapType(context.Module);
        var hashMapLocal = new VariableDefinition(hashMapType);
        var stringBuilderType = context.Module.ImportReference(typeof(StringBuilder));
        var stringBuilderLocal = new VariableDefinition(stringBuilderType);
        var boolLocal = new VariableDefinition(context.Module.TypeSystem.Boolean);
        var hashEnumeratorType = context.Module.ImportReference(typeof(Dictionary<object, object>.Enumerator));
        var hashEnumeratorLocal = new VariableDefinition(hashEnumeratorType);
        var keyValuePairType = context.Module.ImportReference(typeof(KeyValuePair<object, object>));
        var keyValuePairLocal = new VariableDefinition(keyValuePairType);
        context.Method.Body.Variables.Add(objectLocal);
        context.Method.Body.Variables.Add(arrayLocal);
        context.Method.Body.Variables.Add(hashMapLocal);
        context.Method.Body.Variables.Add(stringBuilderLocal);
        context.Method.Body.Variables.Add(boolLocal);
        context.Method.Body.Variables.Add(hashEnumeratorLocal);
        context.Method.Body.Variables.Add(keyValuePairLocal);

        var writeLineObject = context.Module.ImportReference(
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(object)])!);
        var writeLineString = ResolveWriteLineMethod(KongType.String, context.Module)!;
        var stringJoinObjectArray = context.Module.ImportReference(
            typeof(string).GetMethod(nameof(string.Join), [typeof(string), typeof(object[])])!);
        var stringConcatThree = context.Module.ImportReference(
            typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!);
        var stringBuilderCtor = context.Module.ImportReference(typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        var stringBuilderAppendString = context.Module.ImportReference(typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), [typeof(string)])!);
        var stringBuilderAppendObject = context.Module.ImportReference(typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), [typeof(object)])!);
        var stringBuilderToString = context.Module.ImportReference(typeof(StringBuilder).GetMethod(nameof(ToString), Type.EmptyTypes)!);
        var hashGetEnumerator = context.Module.ImportReference(typeof(Dictionary<object, object>).GetMethod(nameof(Dictionary<object, object>.GetEnumerator), Type.EmptyTypes)!);
        var hashMoveNext = context.Module.ImportReference(typeof(Dictionary<object, object>.Enumerator).GetMethod(nameof(Dictionary<object, object>.Enumerator.MoveNext), Type.EmptyTypes)!);
        var hashCurrent = context.Module.ImportReference(typeof(Dictionary<object, object>.Enumerator).GetProperty(nameof(Dictionary<object, object>.Enumerator.Current))!.GetGetMethod()!);
        var keyGetter = context.Module.ImportReference(typeof(KeyValuePair<object, object>).GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetGetMethod()!);
        var valueGetter = context.Module.ImportReference(typeof(KeyValuePair<object, object>).GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetGetMethod()!);

        var notArrayLabel = context.Il.Create(OpCodes.Nop);
        var notHashLabel = context.Il.Create(OpCodes.Nop);
        var hashLoopStartLabel = context.Il.Create(OpCodes.Nop);
        var hashLoopBodyLabel = context.Il.Create(OpCodes.Nop);
        var hashSkipSeparatorLabel = context.Il.Create(OpCodes.Nop);
        var hashLoopEndLabel = context.Il.Create(OpCodes.Nop);
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
        context.Il.Emit(OpCodes.Call, writeLineString);
        context.Il.Emit(OpCodes.Br, endLabel);

        context.Il.Append(notArrayLabel);
        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Isinst, hashMapType);
        context.Il.Emit(OpCodes.Stloc, hashMapLocal);
        context.Il.Emit(OpCodes.Ldloc, hashMapLocal);
        context.Il.Emit(OpCodes.Brfalse, notHashLabel);

        context.Il.Emit(OpCodes.Newobj, stringBuilderCtor);
        context.Il.Emit(OpCodes.Stloc, stringBuilderLocal);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldstr, "{");
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendString);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Emit(OpCodes.Ldc_I4_1);
        context.Il.Emit(OpCodes.Stloc, boolLocal);

        context.Il.Emit(OpCodes.Ldloc, hashMapLocal);
        context.Il.Emit(OpCodes.Callvirt, hashGetEnumerator);
        context.Il.Emit(OpCodes.Stloc, hashEnumeratorLocal);

        context.Il.Append(hashLoopStartLabel);
        context.Il.Emit(OpCodes.Ldloca, hashEnumeratorLocal);
        context.Il.Emit(OpCodes.Call, hashMoveNext);
        context.Il.Emit(OpCodes.Brtrue, hashLoopBodyLabel);
        context.Il.Emit(OpCodes.Br, hashLoopEndLabel);

        context.Il.Append(hashLoopBodyLabel);
        context.Il.Emit(OpCodes.Ldloc, boolLocal);
        context.Il.Emit(OpCodes.Brtrue, hashSkipSeparatorLabel);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldstr, ", ");
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendString);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Append(hashSkipSeparatorLabel);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Stloc, boolLocal);

        context.Il.Emit(OpCodes.Ldloca, hashEnumeratorLocal);
        context.Il.Emit(OpCodes.Call, hashCurrent);
        context.Il.Emit(OpCodes.Stloc, keyValuePairLocal);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldloca, keyValuePairLocal);
        context.Il.Emit(OpCodes.Call, keyGetter);
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendObject);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldstr, ": ");
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendString);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldloca, keyValuePairLocal);
        context.Il.Emit(OpCodes.Call, valueGetter);
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendObject);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Emit(OpCodes.Br, hashLoopStartLabel);

        context.Il.Append(hashLoopEndLabel);
        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Ldstr, "}");
        context.Il.Emit(OpCodes.Callvirt, stringBuilderAppendString);
        context.Il.Emit(OpCodes.Pop);

        context.Il.Emit(OpCodes.Ldloc, stringBuilderLocal);
        context.Il.Emit(OpCodes.Callvirt, stringBuilderToString);
        context.Il.Emit(OpCodes.Call, writeLineString);
        context.Il.Emit(OpCodes.Br, endLabel);

        context.Il.Append(notHashLabel);
        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Call, writeLineObject);

        context.Il.Append(endLabel);
    }

    private static void EmitWriteLineString(EmitContext context, string value)
    {
        context.Il.Emit(OpCodes.Ldstr, value);
        context.Il.Emit(OpCodes.Call, ResolveWriteLineMethod(KongType.String, context.Module));
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

    private static TypeReference ResolveHashMapType(ModuleDefinition module)
    {
        return module.ImportReference(typeof(Dictionary<object, object>));
    }

    private static void EmitConvertInt64ToInt32(EmitContext context)
    {
        context.Il.Emit(OpCodes.Conv_I4);
    }
}
