using Kong.Parsing;
using Kong.Semantics.Symbols;

namespace Kong.Semantics;

public sealed class TypeInferenceResult
{
    public sealed record FunctionSignature(string Name, IReadOnlyList<TypeSymbol> ParameterTypes, TypeSymbol ReturnType);

    // Maps each node to its inferred type. Note: uses reference equality for keys
    private Dictionary<INode, TypeSymbol> Types { get; set; } = [];
    private List<string> Errors { get; set; } = [];
    private Dictionary<string, FunctionSignature> FunctionSignatures { get; set; } = [];

    public void AddNodeType(INode node, TypeSymbol type)
    {
        Types[node] = type;
    }

    public void AddNodeType(INode node, KongType type)
    {
        AddNodeType(node, TypeSymbol.FromKongType(type));
    }

    public KongType GetNodeType(INode node)
    {
        return GetNodeTypeSymbol(node).ToKongType();
    }

    public TypeSymbol GetNodeTypeSymbol(INode node)
    {
        return Types.TryGetValue(node, out var type) ? type : TypeSymbol.Unknown;
    }

    public void AddError(string error)
    {
        Errors.Add(error);
    }

    public IEnumerable<string> GetErrors()
    {
        return Errors;
    }

    public void AddFunctionSignature(string name, IReadOnlyList<TypeSymbol> parameterTypes, TypeSymbol returnType)
    {
        FunctionSignatures[name] = new FunctionSignature(name, parameterTypes, returnType);
    }

    public void AddFunctionSignature(string name, IReadOnlyList<KongType> parameterTypes, KongType returnType)
    {
        AddFunctionSignature(
            name,
            parameterTypes.Select(TypeSymbol.FromKongType).ToList(),
            TypeSymbol.FromKongType(returnType));
    }

    public bool TryGetFunctionSignature(string name, out FunctionSignature signature)
    {
        return FunctionSignatures.TryGetValue(name, out signature!);
    }

    public IEnumerable<FunctionSignature> GetFunctionSignatures()
    {
        return FunctionSignatures.Values;
    }
}
