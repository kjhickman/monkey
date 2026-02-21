using Kong.Parsing;

namespace Kong.Semantic;

public sealed record ModuleAnalysis(
    string FilePath,
    CompilationUnit Unit,
    NameResolution NameResolution,
    TypeCheckResult TypeCheck,
    bool IsRootModule);
