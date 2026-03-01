namespace Kong.Diagnostics;

public sealed record Diagnostic(CompilationStage Stage, string Message, int Line = 0, int Column = 0)
{
    public string FormatMessage()
    {
        if (Line > 0)
        {
            return $"line {Line}, col {Column}: {Message}";
        }

        return Message;
    }
}
