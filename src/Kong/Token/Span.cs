namespace Kong.Token;

/// <summary>
/// A single point in source code (1-based line and column).
/// </summary>
public readonly record struct Position(int Line, int Column)
{
    public override string ToString() => $"line {Line}, column {Column}";
}

/// <summary>
/// A range in source code from <see cref="Start"/> (inclusive) to <see cref="End"/> (exclusive).
/// </summary>
public readonly record struct Span(Position Start, Position End)
{
    public static readonly Span Empty = default;

    /// <summary>
    /// Returns the smallest span that contains both <paramref name="a"/> and <paramref name="b"/>.
    /// </summary>
    public static Span Merge(Span a, Span b) => new(a.Start, b.End);

    public override string ToString() => $"{Start} - {End}";
}
