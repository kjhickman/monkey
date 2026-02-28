using System.Diagnostics;
using Kong.CodeGeneration;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Evaluating;

namespace Kong.Tests.CodeGeneration;

public class VmClrOutputComparisonTests
{
    public static TheoryData<string> ArithmeticPrograms =>
    [
        "1",
        "1 + 2",
        "1 - 2",
        "1 * 2",
        "4 / 2",
        "1 + 1; 5 - 1;",
        "let a = 5; a;",
        "let a = 5; a + 1;",
        "let a = 5; let b = 10; a + b;",
        "let a = 5; let b = a + 2; b + a;",
        "-1",
        "let a = 5; -a + 2;",
        "true",
        "false",
        "1 < 2",
        "1 > 2",
        "1 == 1",
        "1 != 1",
        "1 == 2",
        "1 != 2",
        "true == true",
        "true != false",
        "false == false",
        "(1 < 2) == true",
        "(1 > 2) == false",
        "1 + 2 == 3",
        "1 + 2 * 3 == 7",
        "(1 + 2) * 3 == 9",
        "let a = 5; let b = 10; a < b;",
        "let a = 5; let b = 10; a == b;",
        "let t = true; t;",
        "let t = true; let f = false; t != f;",
        "!true",
        "!false",
        "!!true",
        "!!false",
        "let t = true; !t;",
        "let f = false; !f;",
        "-1 < 0",
        "-1 == -1",
        "1 < 2; 3 < 4;",
        "1 == 2; 3 == 3;",
        "if (true) { 10 } else { 20 }",
        "if (false) { 10 } else { 20 }",
        "if (1 < 2) { 10 } else { 20 }",
        "if (1 > 2) { 10 } else { 20 }",
        "let x = 5; if (x > 3) { x } else { 0 }",
        "if (true) { true } else { false }",
        "if (false) { true } else { false }",
        "let y = 4; let x = if (y > 3) { 5 } else { 10 }; x;",
        "[]",
        "[1, 2, 3]",
        "[1 + 2, 3 * 4, 5 + 6]",
        "let a = [1, 2, 3]; a;",
        "let a = []; a;",
        "let a = [1 + 2, 3 * 4, 5 + 6]; a;",
        "[\"a\", \"b\", \"c\"]",
        "let a = []; let b = [1, 2]; b;",
        "[1, 2, 3][1]",
        "[1, 2, 3][0 + 2]",
        "[[1, 1, 1]][0][0]",
        "let a = [1, 2, 3]; a[2];",
        "{1: 1, 2: 2}[1]",
        "{1: 1, 2: 2}[2]",
        "let h = {1: 1, 2: 2}; h[1];",
        "let add = fn(a: int, b: int) { a + b }; add(5, 3)",
        "let identity = fn(x: int) { x }; identity(42)",
        "let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; choose(8)",
        "let choose = fn(x: int) { if (x > 5) { 10 } else { 20 } }; choose(3)",
    ];

    [Theory]
    [MemberData(nameof(ArithmeticPrograms))]
    public async Task ClrBackend_MatchesVm_ForStarterArithmeticMatrix(string source)
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
        var vmError = vm.Run();
        Assert.Null(vmError);

        return vm.LastPoppedStackElem().Inspect();
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
