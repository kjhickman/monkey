namespace Kong;

/// <summary>
/// The severity level of a diagnostic.
/// </summary>
public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A structured diagnostic produced by any phase of the compiler pipeline.
/// Carries source location, severity, a human-readable message, and a
/// machine-readable code suitable for LSP <c>diagnosticCode</c>.
/// </summary>
public readonly record struct Diagnostic(Span Span, Severity Severity, string Message, string Code)
{
    public override string ToString()
    {
        var location = Span == Span.Empty ? "" : $" (at {Span.Start})";
        return $"{Severity}: {Message}{location} [{Code}]";
    }
}

/// <summary>
/// A mutable bag that collects <see cref="Diagnostic"/> values.
/// Each pipeline phase (parser, compiler, VM) owns one instance.
/// </summary>
public class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>All diagnostics collected so far.</summary>
    public IReadOnlyList<Diagnostic> All => _diagnostics;

    /// <summary>True when at least one <see cref="Severity.Error"/> has been reported.</summary>
    public bool HasErrors => _diagnostics.Exists(d => d.Severity == Severity.Error);

    /// <summary>Number of diagnostics in the bag.</summary>
    public int Count => _diagnostics.Count;

    /// <summary>
    /// Report a diagnostic with the given span, message, and code.
    /// Defaults to <see cref="Severity.Error"/>.
    /// </summary>
    public void Report(Span span, string message, string code, Severity severity = Severity.Error)
    {
        _diagnostics.Add(new Diagnostic(span, severity, message, code));
    }

    /// <summary>
    /// Merge all diagnostics from another bag into this one.
    /// </summary>
    public void AddRange(DiagnosticBag other)
    {
        _diagnostics.AddRange(other._diagnostics);
    }
}
