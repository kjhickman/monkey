using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics.Symbols;

namespace Kong.Semantics;

public sealed class SemanticModel
{
    public sealed record FunctionSignature(string Name, IReadOnlyList<TypeSymbol> ParameterTypes, TypeSymbol ReturnType);

    private Dictionary<INode, TypeSymbol> Types { get; set; } = [];
    private List<Diagnostic> Errors { get; set; } = [];
    private Dictionary<string, FunctionSignature> FunctionSignatures { get; set; } = [];

    public void AddNodeType(INode node, TypeSymbol type)
    {
        Types[node] = type;
    }

    public KongType GetNodeType(INode node)
    {
        return GetNodeTypeSymbol(node).ToKongType();
    }

    public TypeSymbol GetNodeTypeSymbol(INode node)
    {
        return Types.TryGetValue(node, out var type) ? type : TypeSymbol.Unknown;
    }

    public void AddError(string error, int line = 0, int column = 0)
    {
        Errors.Add(new Diagnostic(CompilationStage.SemanticAnalysis, error, line, column));
    }

    public IEnumerable<Diagnostic> GetErrors()
    {
        return Errors;
    }

    public void AddFunctionSignature(string name, IReadOnlyList<TypeSymbol> parameterTypes, TypeSymbol returnType)
    {
        FunctionSignatures[name] = new FunctionSignature(name, parameterTypes, returnType);
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
