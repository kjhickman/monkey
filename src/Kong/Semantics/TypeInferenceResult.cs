using Kong.Parsing;

namespace Kong.Semantics;

public sealed class TypeInferenceResult
{
    // Maps each node to its inferred type. Note: uses reference equality for keys
    private Dictionary<INode, KongType> Types { get; set; } = [];
    private List<string> Errors { get; set; } = [];

    public void AddNodeType(INode node, KongType type)
    {
        Types[node] = type;
    }

    public KongType GetNodeType(INode node)
    {
        return Types.TryGetValue(node, out var type) ? type : KongType.Unknown;
    }

    public void AddError(string error)
    {
        Errors.Add(error);
    }

    public IEnumerable<string> GetErrors()
    {
        return Errors;
    }
}
