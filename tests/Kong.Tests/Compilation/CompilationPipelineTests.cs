using Kong.Compilation;
using Kong.Diagnostics;

namespace Kong.Tests.Compilation;

public class CompilationPipelineTests
{
    [Fact]
    public void Compile_ReportsParsingDiagnostics()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-pipeline-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("let = 5;", "program", outputAssemblyPath);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.ParseResult.DiagnosticBag.Items);
        Assert.Contains(result.DiagnosticBag.Items, d => d.Stage == CompilationStage.Parsing);
        Assert.Null(result.SemanticResult);
        Assert.Null(result.LoweringResult);
        Assert.Null(result.CodegenResult);
    }

    [Fact]
    public void Compile_ReportsSemanticDiagnostics()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-pipeline-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("puts(len(1));", "program", outputAssemblyPath);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.SemanticResult);
        Assert.Null(result.SemanticResult!.BoundProgram);
        Assert.Contains(result.DiagnosticBag.Items, d => d.Stage == CompilationStage.SemanticAnalysis);
        Assert.Contains(result.DiagnosticBag.Items, d => d.Message.Contains("argument to `len` not supported"));
        Assert.Null(result.LoweringResult);
        Assert.Null(result.CodegenResult);
    }

    [Fact]
    public void Compile_BuildsSuccessfulPipelineResult()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var outputAssemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new Compiler();

            var result = compiler.Compile(
                "let add = fn(a: int, b: int) { a + b }; puts(add(1, 2));",
                "program",
                outputAssemblyPath);

            Assert.True(result.Succeeded);
            Assert.Empty(result.DiagnosticBag.Items);
            Assert.NotNull(result.SemanticResult);
            Assert.NotNull(result.SemanticResult!.BoundProgram);
            Assert.NotNull(result.SemanticResult!.Types);
            Assert.NotNull(result.LoweringResult);
            Assert.NotNull(result.CodegenResult);
            Assert.True(File.Exists(outputAssemblyPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Compile_ReportsDuplicateTopLevelFunctionDefinitions()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-pipeline-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("let a = fn() { 1 }; let a = fn() { 2 }; puts(a());", "program", outputAssemblyPath);

        Assert.False(result.Succeeded);
        Assert.Contains(result.DiagnosticBag.Items, d => d.Stage == CompilationStage.SemanticAnalysis);
        Assert.Contains(result.DiagnosticBag.Items, d => d.Message.Contains("duplicate top-level function definition: a"));
    }
}
