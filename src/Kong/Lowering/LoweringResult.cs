using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics;

namespace Kong.Lowering;

public sealed record LoweringResult(Program Program, TypeInferenceResult Types, DiagnosticBag DiagnosticBag);
