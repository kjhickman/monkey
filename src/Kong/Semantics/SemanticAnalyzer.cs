using Kong.Semantics.Binding;
using Kong.Semantics.Checking;

namespace Kong.Semantics;

public sealed class SemanticAnalyzer
{
    public SemanticAnalysisResult Analyze(Parsing.Program program)
    {
        var binder = new Binder();
        var bindResult = binder.Bind(program);

        if (bindResult.Errors.Count > 0)
        {
            return new SemanticAnalysisResult(null, null, bindResult.Errors);
        }

        var checker = new TypeChecker();
        var checkResult = checker.Check(bindResult.BoundProgram);
        if (checkResult.Errors.Count > 0)
        {
            return new SemanticAnalysisResult(bindResult.BoundProgram, checkResult.TypeInfo, checkResult.Errors);
        }

        return new SemanticAnalysisResult(bindResult.BoundProgram, checkResult.TypeInfo, []);
    }
}

public sealed record SemanticAnalysisResult(BoundProgram? BoundProgram, SemanticModel? Types, IReadOnlyList<string> Errors);
