using Kong.Lowering;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kong.CodeGeneration;

public partial class ClrEmitter
{
    private static class ClosureLayout
    {
        public const int DelegateIndex = 0;
        public const int CapturesIndex = 1;
    }

    private sealed class FunctionMetadata
    {
        public required MethodDefinition Method { get; init; }
        public required FunctionSignature Signature { get; init; }
    }

    private sealed class EmitContext
    {
        public EmitContext(ModuleDefinition module, MethodDefinition method, Dictionary<string, FunctionMetadata> functions)
        {
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
}
