namespace Kong.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;

    public bool HasErrors => _items.Count > 0;

    public void Add(CompilationStage stage, string message)
    {
        _items.Add(new Diagnostic(stage, message));
    }

    public void AddRange(CompilationStage stage, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Add(stage, message);
        }
    }

    public void AddRange(DiagnosticBag diagnostics)
    {
        if (ReferenceEquals(this, diagnostics))
        {
            return;
        }

        _items.AddRange(diagnostics._items);
    }

    public void AddRange(CompilationStage stage, DiagnosticBag diagnostics)
    {
        if (ReferenceEquals(this, diagnostics))
        {
            return;
        }

        foreach (var diagnostic in diagnostics._items)
        {
            Add(stage, diagnostic.Message);
        }
    }

    public override string ToString()
    {
        return string.Join(Environment.NewLine, _items.Select(d => d.Message));
    }
}
