using Kong.CodeGeneration;
using Kong.Diagnostics;
using Kong.Lowering;
using Kong.Parsing;
using Kong.Semantics;

namespace Kong.Compilation;

public sealed record CompilationResult(
    ParseResult ParseResult,
    SemanticResult? SemanticResult,
    LoweringResult? LoweringResult,
    CodegenResult? CodegenResult,
    DiagnosticBag DiagnosticBag)
{
    public bool Succeeded => !DiagnosticBag.HasErrors && CodegenResult is { Succeeded: true };
}
