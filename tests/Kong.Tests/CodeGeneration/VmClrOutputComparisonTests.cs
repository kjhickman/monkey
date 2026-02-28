using System.Diagnostics;
using System.IO;
using Kong.CodeGeneration;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Evaluating;

namespace Kong.Tests.CodeGeneration;

public class VmClrOutputComparisonTests
{
    public static TheoryData<string> OutputPrograms =>
    [
        "puts(1)",
        "puts(1 + 2)",
        "puts(true != false)",
        "puts(if (1 < 2) { 10 } else { 20 })",
        "puts(\"mon\" + \"key\")",
        "puts([1 + 2, 3 * 4, 5 + 6])",
        "puts({1 + 1: 2 * 2, 3 + 3: 4 * 4})",
        "puts([1, 2, 3][0 + 2])",
        "let add = fn(a: int, b: int) { a + b }; puts(add(5, 3));",
        "let factorial = fn(x: int) { if (x == 0) { 1 } else { x * factorial(x - 1) } }; puts(factorial(5));",
        "let newClosure = fn(a: int) { fn() { a; }; }; let closure = newClosure(99); puts(closure());",
        "puts(\"hello\", \"world!\")",
    ];

    [Theory]
    [MemberData(nameof(OutputPrograms))]
    public async Task ClrBackend_MatchesVm_ForExplicitPutsPrograms(string source)
    {
        var vmOutput = RunOnVm(source);
        var clrOutput = await CompileAndRunOnClr(source);

        Assert.Equal(NormalizeOutput(vmOutput), NormalizeOutput(clrOutput));
    }

    private static string RunOnVm(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);
        var program = parser.ParseProgram();

        var parserErrors = parser.Errors();
        Assert.Empty(parserErrors);

        var compiler = new Compiler();
        var compileError = compiler.Compile(program);
        Assert.Null(compileError);

        var vm = new Vm(compiler.GetBytecode());

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var vmError = vm.Run();
            Assert.Null(vmError);
            return writer.ToString().TrimEnd();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static async Task<string> CompileAndRunOnClr(string source)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-il-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var assemblyPath = Path.Combine(tempDirectory, "program.dll");
            var compiler = new KongCompiler();
            var compileError = compiler.CompileToAssembly(source, "program", assemblyPath);
            Assert.Null(compileError);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{assemblyPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"dotnet exited with code {process.ExitCode}: {stdErr}");

            return stdOut.TrimEnd();
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static string NormalizeOutput(string s)
    {
        var trimmed = s.Trim();
        return trimmed switch
        {
            "True" => "true",
            "False" => "false",
            _ => trimmed,
        };
    }
}
