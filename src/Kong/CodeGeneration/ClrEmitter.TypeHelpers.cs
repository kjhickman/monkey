using Kong.Semantics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private static TypeReference ResolveLocalType(KongType valueType, ModuleDefinition module)
    {
        return valueType switch
        {
            KongType.Int64 => module.TypeSystem.Int64,
            KongType.Boolean => module.TypeSystem.Boolean,
            KongType.Char => module.TypeSystem.Char,
            KongType.Double => module.TypeSystem.Double,
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
            KongType.Char => module.TypeSystem.Char,
            KongType.Double => module.TypeSystem.Double,
            KongType.String => module.TypeSystem.String,
            _ => module.TypeSystem.Object,
        };
    }

    private static TypeReference ResolveHashMapType(ModuleDefinition module)
    {
        return module.ImportReference(typeof(Dictionary<object, object>));
    }

    private static TypeReference ResolveClosureDelegateType(ModuleDefinition module)
    {
        return module.ImportReference(typeof(Func<object[], object[], object>));
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
            case KongType.Char:
                context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Char);
                break;
            case KongType.Double:
                context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Double);
                break;
        }
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

        if (type.FullName == context.Module.TypeSystem.Char.FullName)
        {
            context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Char);
        }

        if (type.FullName == context.Module.TypeSystem.Double.FullName)
        {
            context.Il.Emit(OpCodes.Box, context.Module.TypeSystem.Double);
        }
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
            case KongType.Char:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Char);
                break;
            case KongType.Double:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Double);
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
            case KongType.Char:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Char);
                break;
            case KongType.Double:
                context.Il.Emit(OpCodes.Unbox_Any, context.Module.TypeSystem.Double);
                break;
            case KongType.String:
                context.Il.Emit(OpCodes.Castclass, context.Module.TypeSystem.String);
                break;
        }
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

        if (expectedType.FullName == context.Module.TypeSystem.Char.FullName)
        {
            return;
        }

        if (expectedType.FullName == context.Module.TypeSystem.Double.FullName)
        {
            return;
        }

        context.Il.Emit(OpCodes.Castclass, expectedType);
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
