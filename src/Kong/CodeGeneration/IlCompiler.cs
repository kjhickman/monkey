using Kong.Parsing;
using Kong.Semantics;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public class IlCompiler
{
    private int _nextClosureId;

    private static class ClosureLayout
    {
        public const int DelegateIndex = 0;
        public const int CapturesIndex = 1;
    }

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
            ClosureParameterIndices = [];
            ClosureCaptureIndices = [];
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
        public Dictionary<string, int> ClosureParameterIndices { get; }
        public Dictionary<string, int> ClosureCaptureIndices { get; }
        public ArrayType ObjectArrayType { get; }

        public bool IsClosureContext => Method.Parameters.Count == 2
            && Method.Parameters[0].ParameterType.FullName == ObjectArrayType.FullName
            && Method.Parameters[1].ParameterType.FullName == ObjectArrayType.FullName;
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
            FunctionLiteral functionLiteral => EmitFunctionLiteralExpression(functionLiteral, context),
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

            if (context.ClosureParameterIndices.TryGetValue(identifier.Value, out var parameterIndex))
            {
                context.Il.Emit(OpCodes.Ldarg_1);
                context.Il.Emit(OpCodes.Ldc_I4, parameterIndex);
                context.Il.Emit(OpCodes.Ldelem_Ref);
                EmitReadFromObject(context.Types.GetNodeType(identifier), context);
                return null;
            }

            if (context.ClosureCaptureIndices.TryGetValue(identifier.Value, out var captureIndex))
            {
                context.Il.Emit(OpCodes.Ldarg_0);
                context.Il.Emit(OpCodes.Ldc_I4, captureIndex);
                context.Il.Emit(OpCodes.Ldelem_Ref);
                EmitReadFromObject(context.Types.GetNodeType(identifier), context);
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
        if (callExpression.Function is Identifier { Value: "puts" })
        {
            return EmitPutsBuiltinCall(callExpression, context);
        }

        if (callExpression.Function is Identifier { Value: "len" })
        {
            return EmitLenBuiltinCall(callExpression, context);
        }

        if (callExpression.Function is Identifier functionIdentifier
            && context.Functions.TryGetValue(functionIdentifier.Value, out var functionMetadata)
            && !IsVariableBound(functionIdentifier.Value, context))
        {
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

        var closureArrayLocal = new VariableDefinition(context.ObjectArrayType);
        var delegateType = ResolveClosureDelegateType(context.Module);
        var closureDelegateLocal = new VariableDefinition(delegateType);
        var capturesLocal = new VariableDefinition(context.ObjectArrayType);
        var argsLocal = new VariableDefinition(context.ObjectArrayType);
        context.Method.Body.Variables.Add(closureArrayLocal);
        context.Method.Body.Variables.Add(closureDelegateLocal);
        context.Method.Body.Variables.Add(capturesLocal);
        context.Method.Body.Variables.Add(argsLocal);

        var functionErr = EmitExpression(callExpression.Function, context);
        if (functionErr is not null)
        {
            return functionErr;
        }

        context.Il.Emit(OpCodes.Castclass, context.ObjectArrayType);
        context.Il.Emit(OpCodes.Stloc, closureArrayLocal);

        context.Il.Emit(OpCodes.Ldloc, closureArrayLocal);
        context.Il.Emit(OpCodes.Ldc_I4, ClosureLayout.DelegateIndex);
        context.Il.Emit(OpCodes.Ldelem_Ref);
        context.Il.Emit(OpCodes.Castclass, delegateType);
        context.Il.Emit(OpCodes.Stloc, closureDelegateLocal);

        context.Il.Emit(OpCodes.Ldloc, closureArrayLocal);
        context.Il.Emit(OpCodes.Ldc_I4, ClosureLayout.CapturesIndex);
        context.Il.Emit(OpCodes.Ldelem_Ref);
        context.Il.Emit(OpCodes.Castclass, context.ObjectArrayType);
        context.Il.Emit(OpCodes.Stloc, capturesLocal);

        context.Il.Emit(OpCodes.Ldc_I4, callExpression.Arguments.Count);
        context.Il.Emit(OpCodes.Newarr, context.Module.TypeSystem.Object);
        context.Il.Emit(OpCodes.Stloc, argsLocal);

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            context.Il.Emit(OpCodes.Ldloc, argsLocal);
            context.Il.Emit(OpCodes.Ldc_I4, i);

            var argumentErr = EmitExpression(callExpression.Arguments[i], context);
            if (argumentErr is not null)
            {
                return argumentErr;
            }

            EmitBoxIfNeeded(context.Types.GetNodeType(callExpression.Arguments[i]), context);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        var invokeMethod = context.Module.ImportReference(
            typeof(Func<object[], object[], object>).GetMethod(nameof(Func<object[], object[], object>.Invoke))!);
        context.Il.Emit(OpCodes.Ldloc, closureDelegateLocal);
        context.Il.Emit(OpCodes.Ldloc, capturesLocal);
        context.Il.Emit(OpCodes.Ldloc, argsLocal);
        context.Il.Emit(OpCodes.Callvirt, invokeMethod);

        var closureCallType = context.Types.GetNodeType(callExpression);
        EmitReadFromObject(closureCallType, context);
        return null;
    }

    private string? EmitPutsBuiltinCall(CallExpression callExpression, EmitContext context)
    {
        foreach (var argument in callExpression.Arguments)
        {
            var argumentErr = EmitExpression(argument, context);
            if (argumentErr is not null)
            {
                return argumentErr;
            }

            var argumentType = context.Types.GetNodeType(argument);
            if (!EmitWriteLineForTypedResult(argumentType, context))
            {
                EmitBoxIfNeeded(argumentType, context);
                EmitWriteLineForObjectResult(context);
            }
        }

        return null;
    }

    private string? EmitLenBuiltinCall(CallExpression callExpression, EmitContext context)
    {
        if (callExpression.Arguments.Count != 1)
        {
            return $"wrong number of arguments. got={callExpression.Arguments.Count}, want=1";
        }

        var argument = callExpression.Arguments[0];
        var argumentErr = EmitExpression(argument, context);
        if (argumentErr is not null)
        {
            return argumentErr;
        }

        var argumentType = context.Types.GetNodeType(argument);
        if (argumentType == KongType.String)
        {
            EmitStringLength(context);
            return null;
        }

        if (argumentType == KongType.Array)
        {
            EmitArrayLength(context);
            return null;
        }

        EmitLenWithRuntimeTypeCheck(argumentType, context);
        return null;
    }

    private static void EmitStringLength(EmitContext context)
    {
        var stringLengthGetter = context.Module.ImportReference(typeof(string).GetProperty(nameof(string.Length))!.GetGetMethod()!);
        context.Il.Emit(OpCodes.Callvirt, stringLengthGetter);
        context.Il.Emit(OpCodes.Conv_I8);
    }

    private static void EmitArrayLength(EmitContext context)
    {
        EmitCastToObjectArray(context);
        context.Il.Emit(OpCodes.Ldlen);
        context.Il.Emit(OpCodes.Conv_I8);
    }

    private static void EmitLenWithRuntimeTypeCheck(KongType argumentType, EmitContext context)
    {
        EmitBoxIfNeeded(argumentType, context);

        var objectLocal = new VariableDefinition(context.Module.TypeSystem.Object);
        var stringLocal = new VariableDefinition(context.Module.TypeSystem.String);
        var arrayLocal = new VariableDefinition(context.ObjectArrayType);
        context.Method.Body.Variables.Add(objectLocal);
        context.Method.Body.Variables.Add(stringLocal);
        context.Method.Body.Variables.Add(arrayLocal);

        var invalidArgLabel = context.Il.Create(OpCodes.Nop);
        var notStringLabel = context.Il.Create(OpCodes.Nop);
        var notArrayLabel = context.Il.Create(OpCodes.Nop);

        var stringLengthGetter = context.Module.ImportReference(typeof(string).GetProperty(nameof(string.Length))!.GetGetMethod()!);
        var invalidOperationCtor = context.Module.ImportReference(typeof(InvalidOperationException).GetConstructor([typeof(string)])!);

        context.Il.Emit(OpCodes.Stloc, objectLocal);

        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Isinst, context.Module.TypeSystem.String);
        context.Il.Emit(OpCodes.Stloc, stringLocal);
        context.Il.Emit(OpCodes.Ldloc, stringLocal);
        context.Il.Emit(OpCodes.Brfalse, notStringLabel);
        context.Il.Emit(OpCodes.Ldloc, stringLocal);
        context.Il.Emit(OpCodes.Callvirt, stringLengthGetter);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Br, invalidArgLabel);

        context.Il.Append(notStringLabel);
        context.Il.Emit(OpCodes.Ldloc, objectLocal);
        context.Il.Emit(OpCodes.Isinst, context.ObjectArrayType);
        context.Il.Emit(OpCodes.Stloc, arrayLocal);
        context.Il.Emit(OpCodes.Ldloc, arrayLocal);
        context.Il.Emit(OpCodes.Brfalse, notArrayLabel);
        context.Il.Emit(OpCodes.Ldloc, arrayLocal);
        context.Il.Emit(OpCodes.Ldlen);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Br, invalidArgLabel);

        context.Il.Append(notArrayLabel);
        context.Il.Emit(OpCodes.Ldstr, "argument to `len` not supported");
        context.Il.Emit(OpCodes.Newobj, invalidOperationCtor);
        context.Il.Emit(OpCodes.Throw);

        context.Il.Append(invalidArgLabel);
    }

    private static bool IsVariableBound(string name, EmitContext context)
    {
        return context.Locals.ContainsKey(name)
            || context.Parameters.ContainsKey(name)
            || context.ClosureCaptureIndices.ContainsKey(name)
            || context.ClosureParameterIndices.ContainsKey(name);
    }

    private string? EmitFunctionLiteralExpression(FunctionLiteral functionLiteral, EmitContext outerContext)
    {
        var captures = ResolveFreeVariables(functionLiteral, outerContext);
        var (closureMethod, createErr) = CreateClosureMethod(functionLiteral, captures, outerContext);
        if (createErr is not null)
        {
            return createErr;
        }

        var capturesArrayLocal = new VariableDefinition(outerContext.ObjectArrayType);
        outerContext.Method.Body.Variables.Add(capturesArrayLocal);

        outerContext.Il.Emit(OpCodes.Ldc_I4, captures.Count);
        outerContext.Il.Emit(OpCodes.Newarr, outerContext.Module.TypeSystem.Object);

        for (var i = 0; i < captures.Count; i++)
        {
            outerContext.Il.Emit(OpCodes.Dup);
            outerContext.Il.Emit(OpCodes.Ldc_I4, i);

            var loadErr = EmitLoadIdentifierAsObject(captures[i], outerContext);
            if (loadErr is not null)
            {
                return loadErr;
            }

            outerContext.Il.Emit(OpCodes.Stelem_Ref);
        }

        outerContext.Il.Emit(OpCodes.Stloc, capturesArrayLocal);

        var delegateType = ResolveClosureDelegateType(outerContext.Module);
        var delegateCtor = outerContext.Module.ImportReference(
            typeof(Func<object[], object[], object>).GetConstructor([typeof(object), typeof(IntPtr)])!);

        outerContext.Il.Emit(OpCodes.Ldc_I4_2);
        outerContext.Il.Emit(OpCodes.Newarr, outerContext.Module.TypeSystem.Object);

        outerContext.Il.Emit(OpCodes.Dup);
        outerContext.Il.Emit(OpCodes.Ldc_I4, ClosureLayout.DelegateIndex);
        outerContext.Il.Emit(OpCodes.Ldnull);
        outerContext.Il.Emit(OpCodes.Ldftn, closureMethod);
        outerContext.Il.Emit(OpCodes.Newobj, delegateCtor);
        outerContext.Il.Emit(OpCodes.Stelem_Ref);

        outerContext.Il.Emit(OpCodes.Dup);
        outerContext.Il.Emit(OpCodes.Ldc_I4, ClosureLayout.CapturesIndex);
        outerContext.Il.Emit(OpCodes.Ldloc, capturesArrayLocal);
        outerContext.Il.Emit(OpCodes.Stelem_Ref);

        return null;
    }

    private (MethodDefinition Method, string? Err) CreateClosureMethod(FunctionLiteral functionLiteral, List<string> captures, EmitContext outerContext)
    {
        var closureId = _nextClosureId++;
        var method = new MethodDefinition(
            $"__closure_{closureId}",
            MethodAttributes.Private | MethodAttributes.Static,
            outerContext.Module.TypeSystem.Object);
        method.Parameters.Add(new ParameterDefinition("captures", ParameterAttributes.None, outerContext.ObjectArrayType));
        method.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, outerContext.ObjectArrayType));

        var programType = outerContext.Module.Types.First(t => t.Name == "Program");
        programType.Methods.Add(method);

        var closureContext = new EmitContext(outerContext.Types, outerContext.Module, method, outerContext.Functions);
        for (var i = 0; i < captures.Count; i++)
        {
            closureContext.ClosureCaptureIndices[captures[i]] = i;
        }

        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            closureContext.ClosureParameterIndices[functionLiteral.Parameters[i].Name.Value] = i;
        }

        var err = EmitFunctionBody(functionLiteral.Body, closureContext);
        return (method, err);
    }

    private static List<string> ResolveFreeVariables(FunctionLiteral functionLiteral, EmitContext context)
    {
        var referenced = new HashSet<string>();
        CollectReferencedIdentifiers(functionLiteral.Body, referenced);

        var declared = new HashSet<string>(functionLiteral.Parameters.Select(p => p.Name.Value));
        CollectDeclaredNames(functionLiteral.Body, declared);

        var captures = new List<string>();
        foreach (var name in referenced)
        {
            if (declared.Contains(name))
            {
                continue;
            }

            if (context.Functions.ContainsKey(name) && !IsVariableBound(name, context))
            {
                continue;
            }

            if (IsVariableBound(name, context))
            {
                captures.Add(name);
            }
        }

        return captures;
    }

    private static void CollectDeclaredNames(IStatement statement, HashSet<string> declared)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                declared.Add(letStatement.Name.Value);
                if (letStatement.Value is not null)
                {
                    CollectDeclaredNames(letStatement.Value, declared);
                }
                break;
            case ExpressionStatement expressionStatement when expressionStatement.Expression is not null:
                CollectDeclaredNames(expressionStatement.Expression, declared);
                break;
            case ReturnStatement returnStatement when returnStatement.ReturnValue is not null:
                CollectDeclaredNames(returnStatement.ReturnValue, declared);
                break;
            case BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    CollectDeclaredNames(inner, declared);
                }
                break;
        }
    }

    private static void CollectDeclaredNames(IExpression expression, HashSet<string> declared)
    {
        switch (expression)
        {
            case FunctionLiteral functionLiteral:
                foreach (var parameter in functionLiteral.Parameters)
                {
                    declared.Add(parameter.Name.Value);
                }
                CollectDeclaredNames(functionLiteral.Body, declared);
                break;
            case PrefixExpression prefixExpression:
                CollectDeclaredNames(prefixExpression.Right, declared);
                break;
            case InfixExpression infixExpression:
                CollectDeclaredNames(infixExpression.Left, declared);
                CollectDeclaredNames(infixExpression.Right, declared);
                break;
            case IfExpression ifExpression:
                CollectDeclaredNames(ifExpression.Condition, declared);
                CollectDeclaredNames(ifExpression.Consequence, declared);
                if (ifExpression.Alternative is not null)
                {
                    CollectDeclaredNames(ifExpression.Alternative, declared);
                }
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    CollectDeclaredNames(element, declared);
                }
                break;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    CollectDeclaredNames(pair.Key, declared);
                    CollectDeclaredNames(pair.Value, declared);
                }
                break;
            case IndexExpression indexExpression:
                CollectDeclaredNames(indexExpression.Left, declared);
                CollectDeclaredNames(indexExpression.Index, declared);
                break;
            case CallExpression callExpression:
                CollectDeclaredNames(callExpression.Function, declared);
                foreach (var argument in callExpression.Arguments)
                {
                    CollectDeclaredNames(argument, declared);
                }
                break;
        }
    }

    private static void CollectReferencedIdentifiers(IStatement statement, HashSet<string> identifiers)
    {
        switch (statement)
        {
            case LetStatement letStatement when letStatement.Value is not null:
                CollectReferencedIdentifiers(letStatement.Value, identifiers);
                break;
            case ExpressionStatement expressionStatement when expressionStatement.Expression is not null:
                CollectReferencedIdentifiers(expressionStatement.Expression, identifiers);
                break;
            case ReturnStatement returnStatement when returnStatement.ReturnValue is not null:
                CollectReferencedIdentifiers(returnStatement.ReturnValue, identifiers);
                break;
            case BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    CollectReferencedIdentifiers(inner, identifiers);
                }
                break;
        }
    }

    private static void CollectReferencedIdentifiers(IExpression expression, HashSet<string> identifiers)
    {
        switch (expression)
        {
            case Identifier identifier:
                identifiers.Add(identifier.Value);
                break;
            case PrefixExpression prefixExpression:
                CollectReferencedIdentifiers(prefixExpression.Right, identifiers);
                break;
            case InfixExpression infixExpression:
                CollectReferencedIdentifiers(infixExpression.Left, identifiers);
                CollectReferencedIdentifiers(infixExpression.Right, identifiers);
                break;
            case IfExpression ifExpression:
                CollectReferencedIdentifiers(ifExpression.Condition, identifiers);
                CollectReferencedIdentifiers(ifExpression.Consequence, identifiers);
                if (ifExpression.Alternative is not null)
                {
                    CollectReferencedIdentifiers(ifExpression.Alternative, identifiers);
                }
                break;
            case FunctionLiteral functionLiteral:
                CollectReferencedIdentifiers(functionLiteral.Body, identifiers);
                break;
            case CallExpression callExpression:
                CollectReferencedIdentifiers(callExpression.Function, identifiers);
                foreach (var argument in callExpression.Arguments)
                {
                    CollectReferencedIdentifiers(argument, identifiers);
                }
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    CollectReferencedIdentifiers(element, identifiers);
                }
                break;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    CollectReferencedIdentifiers(pair.Key, identifiers);
                    CollectReferencedIdentifiers(pair.Value, identifiers);
                }
                break;
            case IndexExpression indexExpression:
                CollectReferencedIdentifiers(indexExpression.Left, identifiers);
                CollectReferencedIdentifiers(indexExpression.Index, identifiers);
                break;
        }
    }

    private static string? EmitLoadIdentifierAsObject(string name, EmitContext context)
    {
        if (context.Locals.TryGetValue(name, out var local))
        {
            context.Il.Emit(OpCodes.Ldloc, local);
            EmitBoxForTypeReferenceIfNeeded(local.VariableType, context);
            return null;
        }

        if (context.Parameters.TryGetValue(name, out var parameter))
        {
            context.Il.Emit(OpCodes.Ldarg, parameter);
            EmitBoxForTypeReferenceIfNeeded(parameter.ParameterType, context);
            return null;
        }

        if (context.ClosureParameterIndices.TryGetValue(name, out var paramIndex))
        {
            context.Il.Emit(OpCodes.Ldarg_1);
            context.Il.Emit(OpCodes.Ldc_I4, paramIndex);
            context.Il.Emit(OpCodes.Ldelem_Ref);
            return null;
        }

        if (context.ClosureCaptureIndices.TryGetValue(name, out var captureIndex))
        {
            context.Il.Emit(OpCodes.Ldarg_0);
            context.Il.Emit(OpCodes.Ldc_I4, captureIndex);
            context.Il.Emit(OpCodes.Ldelem_Ref);
            return null;
        }

        return $"Undefined variable: {name}";
    }

    private static void EmitBoxForTypeReferenceIfNeeded(TypeReference type, EmitContext context)
    {
        if (type.FullName == context.Module.TypeSystem.Int64.FullName)
        {
            context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Int64);
            return;
        }

        if (type.FullName == context.Module.TypeSystem.Boolean.FullName)
        {
            context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Boolean);
        }
    }

    private static TypeReference ResolveClosureDelegateType(ModuleDefinition module)
    {
        return module.ImportReference(typeof(Func<object[], object[], object>));
    }

    private static void EmitReadFromObject(KongType type, EmitContext context)
    {
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
            case KongType.Array:
                context.Il.Emit(OpCodes.Castclass, context.ObjectArrayType);
                break;
            case KongType.HashMap:
                context.Il.Emit(OpCodes.Castclass, ResolveHashMapType(context.Module));
                break;
        }
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
