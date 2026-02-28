using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics;
using Kong.Semantics.Binding;

namespace Kong.Lowering;

public sealed class IdentityLowerer
{
    public LoweringResult Lower(Program program, BoundProgram boundProgram)
    {
        return new LoweringResult(program, boundProgram, new DiagnosticBag());
    }
}
