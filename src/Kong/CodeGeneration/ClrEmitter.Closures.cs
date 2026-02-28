using Kong.Lowering;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private string? EmitCallExpression(CallExpression callExpression, EmitContext context)
    {
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
                var argumentType = argument.Type;
                EmitConversionIfNeeded(argumentType, expectedType, context);
            }

            context.Il.Emit(OpCodes.Call, functionMetadata.Method);

            var callType = callExpression.Type;
            if (callType == Kong.Semantics.KongType.Void)
            {
                context.Il.Emit(OpCodes.Pop);
                return null;
            }

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

            EmitBoxIfNeeded(callExpression.Arguments[i].Type, context);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        var invokeMethod = context.Module.ImportReference(
            typeof(Func<object[], object[], object>).GetMethod(nameof(Func<object[], object[], object>.Invoke))!);
        context.Il.Emit(OpCodes.Ldloc, closureDelegateLocal);
        context.Il.Emit(OpCodes.Ldloc, capturesLocal);
        context.Il.Emit(OpCodes.Ldloc, argsLocal);
        context.Il.Emit(OpCodes.Callvirt, invokeMethod);

        var closureCallType = callExpression.Type;
        if (closureCallType == Kong.Semantics.KongType.Void)
        {
            context.Il.Emit(OpCodes.Pop);
            return null;
        }

        EmitReadFromObject(closureCallType, context);
        return null;
    }

    private string? EmitFunctionLiteralExpression(FunctionLiteral functionLiteral, EmitContext outerContext)
    {
        var captures = functionLiteral.Captures.ToList();
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

        var closureContext = new EmitContext(outerContext.Module, method, outerContext.Functions);
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

    private static bool IsVariableBound(string name, EmitContext context)
    {
        return context.Locals.ContainsKey(name)
            || context.Parameters.ContainsKey(name)
            || context.ClosureCaptureIndices.ContainsKey(name)
            || context.ClosureParameterIndices.ContainsKey(name);
    }
}
