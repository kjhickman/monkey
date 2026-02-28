using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics.Binding;

namespace Kong.Semantics;

public sealed record SemanticResult(Program Program, BoundProgram? BoundProgram, SemanticModel? Types, DiagnosticBag DiagnosticBag);
