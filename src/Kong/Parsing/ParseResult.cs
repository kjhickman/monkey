using Kong.Diagnostics;

namespace Kong.Parsing;

public sealed record ParseResult(Program Program, DiagnosticBag DiagnosticBag);
