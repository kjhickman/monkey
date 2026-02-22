using System.Reflection;
using System.Runtime.Loader;
using Kong.CodeGeneration;
using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Tests;

namespace Kong.Tests.CodeGeneration;

public class ClrArtifactBuilderTests
{
    [Fact]
    public void TestExecutesSimpleAdditionExpression()
    {
        var result = Execute("1 + 1;");

        Assert.True(result.Executed);
        Assert.Equal(2, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestExecutesNestedArithmeticExpression()
    {
        var result = Execute("(1 + 2) * 3;");

        Assert.True(result.Executed);
        Assert.Equal(9, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestExecutesProgramWithLetBinding()
    {
        var result = Execute("let x = 2; x + 3;");

        Assert.True(result.Executed);
        Assert.Equal(5, result.Value);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReturnsSemanticDiagnosticsWhenTypeCheckFails()
    {
        var result = Execute("x;");

        Assert.False(result.Executed);
        Assert.False(result.IsUnsupported);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N001");
    }

    [Fact]
    public void TestReturnsUnsupportedForNonIntTopLevelResult()
    {
        var result = Execute("true;");

        Assert.False(result.Executed);
        Assert.True(result.IsUnsupported);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "IR001");
    }

    [Fact]
    public void TestExecutesIfElseExpression()
    {
        var result = Execute("if (true) { 10 } else { 20 };");

        Assert.True(result.Executed);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void TestExecutesFunctionLiteralCallWithReturn()
    {
        var result = Execute("fn(x: int) -> int { return x + 1; }(41);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesArrayIndexExpression()
    {
        var result = Execute("let xs: int[] = [4, 5, 6]; xs[1];");

        Assert.True(result.Executed);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrConsoleWriteLine()
    {
        var result = Execute("System.Console.WriteLine(42); 1;");

        Assert.True(result.Executed);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrMathAbs()
    {
        var result = Execute("System.Math.Abs(-42);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesImportedStaticClrMathAbs()
    {
        var result = Execute("import System.Math; Math.Abs(-42);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrMathMax()
    {
        var result = Execute("System.Math.Max(10, 3);");

        Assert.True(result.Executed);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrFileExistsViaNamespaceImport()
    {
        var result = Execute("import System.IO; if (File.Exists(\"/tmp/kong-test-never-exists\")) { 1; } else { 0; }");

        Assert.True(result.Executed);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrPathHelpersViaNamespaceImport()
    {
        var result = Execute("import System.IO; if (File.Exists(Path.Combine(\"/tmp\", \"kong-path-never-exists\"))) { 1; } else { 0; }");

        Assert.True(result.Executed);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrEnvironmentCallsViaNamespaceImport()
    {
        var result = Execute("import System; Environment.SetEnvironmentVariable(\"KONG_TEST_ENV\", \"ok\"); Environment.GetEnvironmentVariable(\"KONG_TEST_ENV\"); 1;");

        Assert.True(result.Executed);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrPropertyAccessAndStringInequality()
    {
        var result = Execute("import System; if (Environment.NewLine != \"\") { 1; } else { 0; }");

        Assert.True(result.Executed);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void TestExecutesStaticClrMethodCall()
    {
        var result = Execute("System.Math.Abs(-42);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesClosureWithSingleCapture()
    {
        var result = Execute("let f = fn(outer: int) -> int { let g = fn(x: int) -> int { x + outer }; g(5); }; f(10);");

        Assert.True(result.Executed);
        Assert.Equal(15, result.Value);
    }

    [Fact]
    public void TestExecutesNestedClosureCall()
    {
        var result = Execute("let f = fn(x: int) -> int { let g = fn(y: int) -> int { x + y }; g(7); }; f(5);");

        Assert.True(result.Executed);
        Assert.Equal(12, result.Value);
    }

    [Fact]
    public void TestExecutesEscapingClosureValue()
    {
        var result = Execute("let makeAdder: fn(int) -> fn(int) -> int = fn(x: int) -> fn(int) -> int { fn(y: int) -> int { x + y } }; let addTwo: fn(int) -> int = makeAdder(2); addTwo(40);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesNamedFunctionDeclarationCall()
    {
        var result = Execute("fn Add(x: int, y: int) -> int { x + y; } Add(20, 22);");

        Assert.True(result.Executed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TestExecutesReturnInsideIfBranches()
    {
        var result = Execute("fn Pick(flag: bool) -> int { if (flag) { return 1; } else { return 2; } } fn Main() { System.Console.WriteLine(Pick(false)); }");

        Assert.True(result.Executed);
    }

    [Fact]
    public void TestExecutesProgramWithImplicitVoidNamedFunctionDeclaration()
    {
        var result = Execute("fn Main() { } 1;");

        Assert.True(result.Executed);
        Assert.Equal(1, result.Value);
    }

    private static ClrExecutionResult Execute(string input)
    {
        var unit = Parse(input);
        var result = new ClrExecutionResult();

        var resolver = new NameResolver();
        var names = resolver.Resolve(unit);
        result.Diagnostics.AddRange(names.Diagnostics);
        if (names.Diagnostics.HasErrors)
        {
            return result;
        }

        var checker = new TypeChecker();
        var typeCheck = checker.Check(unit, names);
        result.Diagnostics.AddRange(typeCheck.Diagnostics);
        if (typeCheck.Diagnostics.HasErrors)
        {
            return result;
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "kong-clr-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var builder = new ClrArtifactBuilder();
            var build = builder.BuildArtifact(unit, typeCheck, outputDirectory, "Kong.Generated", names);
            result.Diagnostics.AddRange(build.Diagnostics);
            result.IsUnsupported = build.IsUnsupported;

            if (!build.Built || build.AssemblyPath == null)
            {
                return result;
            }

            var value = ExecuteAssembly(build.AssemblyPath, result.Diagnostics);
            if (value == null)
            {
                return result;
            }

            result.Executed = true;
            result.Value = value.Value;
            return result;
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static long? ExecuteAssembly(string assemblyPath, DiagnosticBag diagnostics)
    {
        var context = new AssemblyLoadContext($"kong-clr-tests-{Guid.NewGuid():N}", isCollectible: true);

        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var programType = assembly.GetType("Kong.Generated.Program");
            var method = programType?.GetMethod("Eval", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                diagnostics.Report(Span.Empty, "generated CLR assembly is missing entry method", "IL002");
                return null;
            }

            var value = method.Invoke(null, null);
            if (value is int int32)
            {
                return int32;
            }

            if (value is long int64)
            {
                return int64;
            }

            diagnostics.Report(Span.Empty, "generated CLR entry method returned unexpected value type", "IL003");
            return null;
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            diagnostics.Report(Span.Empty, $"failed to execute generated CLR assembly: {message}", "IL004");
            return null;
        }
        finally
        {
            context.Unload();
        }
    }

    private static CompilationUnit Parse(string input)
    {
        input = TestSourceUtilities.EnsureFileScopedNamespace(input);
        var lexer = new Lexer(input);
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();

        if (!parser.Diagnostics.HasErrors)
        {
            return unit;
        }

        var message = $"parser has {parser.Diagnostics.Count} errors\n";
        foreach (var diagnostic in parser.Diagnostics.All)
        {
            message += $"parser error: \"{diagnostic.Message}\"\n";
        }

        Assert.Fail(message);
        return null!;
    }

    private sealed class ClrExecutionResult
    {
        public bool Executed { get; set; }
        public bool IsUnsupported { get; set; }
        public long Value { get; set; }
        public DiagnosticBag Diagnostics { get; } = new();
    }
}
