using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics;

namespace Kong.Lowering;

public sealed class IdentityLowerer
{
    public LoweringResult Lower(Program program, TypeInferenceResult types)
    {
        return new LoweringResult(program, types, new DiagnosticBag());
    }
}
