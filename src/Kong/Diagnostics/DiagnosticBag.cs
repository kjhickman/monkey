namespace Kong.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;

    public bool HasErrors => _items.Count > 0;

    public void Add(CompilationStage stage, string message, int line = 0, int column = 0)
    {
        _items.Add(new Diagnostic(stage, message, line, column));
    }

    public void AddRange(CompilationStage stage, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _items.Add(new Diagnostic(stage, diagnostic.Message, diagnostic.Line, diagnostic.Column));
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
            Add(stage, diagnostic.Message, diagnostic.Line, diagnostic.Column);
        }
    }

    public override string ToString()
    {
        return string.Join(Environment.NewLine, _items.Select(d => d.FormatMessage()));
    }
}
