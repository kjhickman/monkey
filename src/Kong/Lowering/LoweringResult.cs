using Kong.Diagnostics;
using Kong.Semantics.Binding;

namespace Kong.Lowering;

public sealed record LoweringResult(Program Program, BoundProgram BoundProgram, DiagnosticBag DiagnosticBag);
