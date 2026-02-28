using Kong.CodeGeneration;
using Kong.Diagnostics;
using Kong.Lexing;
using Kong.Lowering;
using Kong.Parsing;
using Kong.Semantics;

namespace Kong.Compilation;

public class Compiler
{
    public CompilationResult Compile(string source, string assemblyName, string outputAssembly)
    {
        var diagnostics = new DiagnosticBag();

        var parseResult = Parse(source);
        diagnostics.AddRange(parseResult.DiagnosticBag);
        if (parseResult.DiagnosticBag.HasErrors)
        {
            return new CompilationResult(parseResult, null, null, null, diagnostics);
        }

        var semanticResult = AnalyzeSemantics(parseResult.Program);
        diagnostics.AddRange(semanticResult.DiagnosticBag);
        if (semanticResult.DiagnosticBag.HasErrors || semanticResult.BoundProgram is null)
        {
            return new CompilationResult(parseResult, semanticResult, null, null, diagnostics);
        }

        var lowerer = new IdentityLowerer();
        var loweringResult = lowerer.Lower(semanticResult.Program, semanticResult.BoundProgram);
        diagnostics.AddRange(CompilationStage.Lowering, loweringResult.DiagnosticBag);
        if (loweringResult.DiagnosticBag.HasErrors)
        {
            return new CompilationResult(parseResult, semanticResult, loweringResult, null, diagnostics);
        }

        var builder = new ClrArtifactBuilder();
        var codegenResult = builder.Build(loweringResult, assemblyName, outputAssembly);
        diagnostics.AddRange(codegenResult.DiagnosticBag);
        return new CompilationResult(parseResult, semanticResult, loweringResult, codegenResult, diagnostics);
    }

    public string? CompileToAssembly(string source, string assemblyName, string outputAssembly)
    {
        var compileResult = Compile(source, assemblyName, outputAssembly);
        if (compileResult.Succeeded)
        {
            return null;
        }

        return compileResult.DiagnosticBag.ToString();
    }

    private static ParseResult Parse(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);
        var program = parser.ParseProgram();

        var diagnostics = new DiagnosticBag();
        diagnostics.AddRange(CompilationStage.Parsing, parser.Errors());
        return new ParseResult(program, diagnostics);
    }

    private static SemanticResult AnalyzeSemantics(Program program)
    {
        var analyzer = new SemanticAnalyzer();
        var diagnostics = new DiagnosticBag();

        var semanticResult = analyzer.Analyze(program);
        diagnostics.AddRange(CompilationStage.SemanticAnalysis, semanticResult.Errors);
        if (diagnostics.HasErrors)
        {
            return new SemanticResult(program, null, semanticResult.Types, diagnostics);
        }

        return new SemanticResult(program, semanticResult.BoundProgram, semanticResult.Types, diagnostics);
    }
}
