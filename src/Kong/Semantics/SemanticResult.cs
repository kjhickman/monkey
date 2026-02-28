using Kong.Diagnostics;
using Kong.Parsing;

namespace Kong.Semantics;

public sealed record SemanticResult(Program Program, TypeInferenceResult? Types, DiagnosticBag DiagnosticBag);
