using Kong.Diagnostics;

namespace Kong.CodeGeneration;

public sealed record CodegenResult(string OutputAssemblyPath, DiagnosticBag DiagnosticBag)
{
    public bool Succeeded => !DiagnosticBag.HasErrors;
}
