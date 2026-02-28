using System.Text;
using Kong.Lowering;
using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private string? EmitIntrinsicCallExpression(IntrinsicCallExpression intrinsicCall, EmitContext context)
    {
        return intrinsicCall.Name switch
        {
            "puts" => EmitPutsBuiltinCall(intrinsicCall.Arguments, context),
            "len" => EmitLenBuiltinCall(intrinsicCall.Arguments, context),
            "push" => EmitPushBuiltinCall(intrinsicCall.Arguments, context),
            _ => $"Unknown intrinsic: {intrinsicCall.Name}",
        };
    }

    private string? EmitPutsBuiltinCall(IReadOnlyList<IExpression> arguments, EmitContext context)
    {
        foreach (var argument in arguments)
        {
            var argumentErr = EmitExpression(argument, context);
            if (argumentErr is not null)
            {
                return argumentErr;
            }

            var argumentType = argument.Type;
            if (!EmitWriteLineForTypedResult(argumentType, context))
            {
                EmitBoxIfNeeded(argumentType, context);
                EmitWriteLineForObjectResult(context);
            }
        }

        return null;
    }

    private string? EmitLenBuiltinCall(IReadOnlyList<IExpression> arguments, EmitContext context)
    {
        if (arguments.Count != 1)
        {
            return $"wrong number of arguments. got={arguments.Count}, want=1";
        }

        var argument = arguments[0];
        var argumentErr = EmitExpression(argument, context);
        if (argumentErr is not null)
        {
            return argumentErr;
        }

        var argumentType = argument.Type;
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

    private string? EmitPushBuiltinCall(IReadOnlyList<IExpression> arguments, EmitContext context)
    {
        if (arguments.Count != 2)
        {
            return $"wrong number of arguments. got={arguments.Count}, want=2";
        }

        var arrayExpr = arguments[0];
        var arrayErr = EmitExpression(arrayExpr, context);
        if (arrayErr is not null)
        {
            return arrayErr;
        }

        var arrayType = arrayExpr.Type;
        if (arrayType == KongType.Array)
        {
            return EmitPushForArrayOnStack(arguments[1], context);
        }

        if (arrayType != KongType.Unknown)
        {
            return $"argument to `push` must be ARRAY, got {arrayType}";
        }

        return EmitPushWithRuntimeTypeCheck(arguments[1], context);
    }

    private string? EmitPushForArrayOnStack(IExpression valueExpr, EmitContext context)
    {
        var oldArrayLocal = new VariableDefinition(context.ObjectArrayType);
        var newArrayLocal = new VariableDefinition(context.ObjectArrayType);
        var lengthLocal = new VariableDefinition(context.Module.TypeSystem.Int32);
        context.Method.Body.Variables.Add(oldArrayLocal);
        context.Method.Body.Variables.Add(newArrayLocal);
        context.Method.Body.Variables.Add(lengthLocal);

        EmitCastToObjectArray(context);
        context.Il.Emit(OpCodes.Stloc, oldArrayLocal);

        context.Il.Emit(OpCodes.Ldloc, oldArrayLocal);
        context.Il.Emit(OpCodes.Ldlen);
        EmitConvertInt64ToInt32(context);
        context.Il.Emit(OpCodes.Stloc, lengthLocal);

        context.Il.Emit(OpCodes.Ldloc, lengthLocal);
        context.Il.Emit(OpCodes.Ldc_I4_1);
        context.Il.Emit(OpCodes.Add);
        context.Il.Emit(OpCodes.Newarr, context.Module.TypeSystem.Object);
        context.Il.Emit(OpCodes.Stloc, newArrayLocal);

        var arrayCopyMethod = context.Module.ImportReference(
            typeof(Array).GetMethod(nameof(Array.Copy), [typeof(Array), typeof(Array), typeof(int)])!);
        context.Il.Emit(OpCodes.Ldloc, oldArrayLocal);
        context.Il.Emit(OpCodes.Ldloc, newArrayLocal);
        context.Il.Emit(OpCodes.Ldloc, lengthLocal);
        context.Il.Emit(OpCodes.Call, arrayCopyMethod);

        context.Il.Emit(OpCodes.Ldloc, newArrayLocal);
        context.Il.Emit(OpCodes.Ldloc, lengthLocal);
        var valueErr = EmitExpression(valueExpr, context);
        if (valueErr is not null)
        {
            return valueErr;
        }

        EmitBoxIfNeeded(valueExpr.Type, context);
        context.Il.Emit(OpCodes.Stelem_Ref);
        context.Il.Emit(OpCodes.Ldloc, newArrayLocal);
        return null;
    }

    private string? EmitPushWithRuntimeTypeCheck(IExpression valueExpr, EmitContext context)
    {
        context.Il.Emit(OpCodes.Dup);
        context.Il.Emit(OpCodes.Isinst, context.ObjectArrayType);

        var throwLabel = context.Il.Create(OpCodes.Nop);
        var continueLabel = context.Il.Create(OpCodes.Nop);
        context.Il.Emit(OpCodes.Brtrue, continueLabel);
        context.Il.Emit(OpCodes.Pop);
        context.Il.Emit(OpCodes.Br, throwLabel);

        context.Il.Append(continueLabel);
        var emitErr = EmitPushForArrayOnStack(valueExpr, context);
        if (emitErr is not null)
        {
            return emitErr;
        }

        var doneLabel = context.Il.Create(OpCodes.Nop);
        context.Il.Emit(OpCodes.Br, doneLabel);

        context.Il.Append(throwLabel);
        var invalidOperationCtor = context.Module.ImportReference(typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        context.Il.Emit(OpCodes.Ldstr, "argument to `push` must be ARRAY");
        context.Il.Emit(OpCodes.Newobj, invalidOperationCtor);
        context.Il.Emit(OpCodes.Throw);

        context.Il.Append(doneLabel);
        return null;
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
}
