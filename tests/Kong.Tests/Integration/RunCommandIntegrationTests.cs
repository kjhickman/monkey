using Kong.Cli.Commands;

namespace Kong.Tests.Integration;

[Collection("CLI Integration")]
public class RunCommandIntegrationTests
{
    [Fact]
    public void TestRunCommandReportsInferenceDiagnostic()
    {
        var filePath = CreateTempProgram("fn Main() { let x = if (true) { 1 }; }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[T119]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandFailsFastBeforeCompilerDiagnostics()
    {
        var filePath = CreateTempProgram("fn Main() { foobar; }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.DoesNotContain("ERROR:", stdout);
            Assert.Contains("[N001]", stderr);
            Assert.DoesNotContain("[C001]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesValidProgram()
    {
        var filePath = CreateTempProgram("fn Main() { let x = 2; x + 3; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesSimpleAdditionWithClrPhase1Backend()
    {
        var filePath = CreateTempProgram("fn Main() { 1 + 1; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesFunctionCallWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { fn(x: int) -> int { return x + 1; }(5); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesArrayAndStaticClrProgramWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { let xs: int[] = [1, 2, 3]; System.Console.WriteLine(xs[0]); System.Math.Abs(-4); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("1", stdout);
            Assert.DoesNotContain("[IR001]", stderr);
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesClosureProgramWithClrBackend()
    {
        var filePath = CreateTempProgram("fn Main() { let f = fn(outer: int) -> int { let g = fn(x: int) -> int { x + outer }; g(5); }; f(10); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandExecutesNamedFunctionDeclarationProgram()
    {
        var filePath = CreateTempProgram("fn Add(x: int, y: int) -> int { x + y; } fn Main() { Add(20, 22); }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Equal(string.Empty, stderr.Trim());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandPropagatesMainIntExitCodeSilently()
    {
        var filePath = CreateTempProgram("fn Main() -> int { 7; }");
        try
        {
            var (_, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(7, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrOutput()
    {
        var filePath = CreateTempProgram("fn Main() { System.Console.WriteLine(42); }");
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("42", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrWriteLineOutput()
    {
        var filePath = CreateTempProgram("fn Main() { System.Console.WriteLine(42); }");
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("42", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsImportedStaticClrWriteLineOutput()
    {
        var filePath = CreateTempProgram("import System.Console; fn Main() { Console.WriteLine(42); }");
        try
        {
            var (_, _, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsNamespaceImportForStaticClrWriteLineOutput()
    {
        var filePath = CreateTempProgram("import System; fn Main() { Console.WriteLine(42); }");
        try
        {
            var (_, _, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrFileIo()
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"kong-io-test-{Guid.NewGuid():N}.txt");
        var escapedPath = tempFilePath.Replace("\\", "\\\\");
        var source = $"import System; import System.IO; fn Main() {{ File.WriteAllText(\"{escapedPath}\", \"hello\"); Console.WriteLine(File.ReadAllText(\"{escapedPath}\")); File.Delete(\"{escapedPath}\"); }}";
        var filePath = CreateTempProgram(source);
        try
        {
            var (stdout, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Contains("hello", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsPathImportFromAnotherKongFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(20, 22); }");

            var (_, stderr, exitCode) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(0, exitCode);
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
    public void TestRunCommandReportsMissingPathImport()
    {
        var filePath = CreateTempProgram("import \"./missing.kg\"; fn Main() { 1; }");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI009]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandReportsCyclicPathImport()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var aPath = Path.Combine(tempDirectory, "a.kg");
        var bPath = Path.Combine(tempDirectory, "b.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(aPath, "import \"./b.kg\"; namespace A; fn A() -> int { 1; }");
            File.WriteAllText(bPath, "import \"./a.kg\"; namespace B; fn B() -> int { 2; }");
            File.WriteAllText(mainPath, "import \"./a.kg\"; namespace App; fn Main() { A(); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI008]", stderr);
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
    public void TestRunCommandReportsNamespacePathMismatchInImportedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Helpers; fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(1, 2); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI019]", stderr);
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
    public void TestRunCommandReportsDuplicateTopLevelFunctionAcrossModules()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Add(x: int, y: int) -> int { x - y; } fn Main() { Add(1, 2); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI016]", stderr);
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
    public void TestRunCommandReportsMissingNamespaceInImportedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(1, 2); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI010]", stderr);
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
    public void TestRunCommandReportsNestedImportInImportedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; fn Helper() { import System; } fn Add(x: int, y: int) -> int { x + y; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(1, 2); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI014]", stderr);
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
    public void TestRunCommandPrefixesImportedModuleDiagnosticsWithFileName()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; fn Add(x: int, y: int) -> int { missing; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { Add(1, 2); }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[N001]", stderr);
            Assert.Contains("[util.kg]", stderr);
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
    public void TestRunCommandReportsTopLevelExpressionInImportedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; 1;");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { 0; }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI017]", stderr);
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
    public void TestRunCommandReportsMainDeclarationInImportedFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kong-module-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var utilPath = Path.Combine(tempDirectory, "util.kg");
        var mainPath = Path.Combine(tempDirectory, "main.kg");

        try
        {
            File.WriteAllText(utilPath, "namespace Util; fn Main() { 1; }");
            File.WriteAllText(mainPath, "import \"./util.kg\"; namespace App; fn Main() { 0; }");

            var (stdout, stderr, _) = ExecuteRunCommand(mainPath);
            Assert.Equal(string.Empty, stdout.Trim());
            Assert.Contains("[CLI018]", stderr);
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
    public void TestRunCommandSupportsStaticClrPathAndDirectoryCalls()
    {
        var filePath = CreateTempProgram("import System.IO; fn Main() { let p: string = Path.Combine(\"/tmp\", \"kong-path-test.txt\"); if (Directory.Exists(Directory.GetCurrentDirectory())) { System.Console.WriteLine(Path.GetFileName(p)); } else { System.Console.WriteLine(\"missing\"); } }");
        try
        {
            var (_, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrEnvironmentCalls()
    {
        var filePath = CreateTempProgram("import System; fn Main() { Environment.SetEnvironmentVariable(\"KONG_TEST_ENV\", \"ok\"); if (Environment.GetEnvironmentVariable(\"KONG_TEST_ENV\") == \"ok\") { System.Console.WriteLine(1); } else { System.Console.WriteLine(0); } }");
        try
        {
            var (_, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KONG_TEST_ENV", null);
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandSupportsStaticClrPropertyAccess()
    {
        var filePath = CreateTempProgram("import System; fn Main() { if (Environment.NewLine != \"\") { System.Console.WriteLine(1); } else { System.Console.WriteLine(0); } }");
        try
        {
            var (_, stderr, exitCode) = ExecuteRunCommand(filePath);
            Assert.Equal(string.Empty, stderr.Trim());
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandReportsUnsupportedIfWithoutElseBeforeLowering()
    {
        var filePath = CreateTempProgram("fn Main() { if (true) { 1 }; }");
        try
        {
            var (_, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("[CLI007]", stderr);
            Assert.DoesNotContain("[IR001]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TestRunCommandDoesNotFallbackToVmByDefault()
    {
        var filePath = CreateTempProgram("\"hello\";");
        try
        {
            var (stdout, stderr, _) = ExecuteRunCommand(filePath);
            Assert.Contains("[CLI002]", stderr);
            Assert.Contains("[CLI003]", stderr);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateTempProgram(string source)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kong-run-test-{Guid.NewGuid():N}.kg");
        File.WriteAllText(filePath, EnsureFileScopedNamespace(source));
        return filePath;
    }

    private static string EnsureFileScopedNamespace(string source)
    {
        if (source.Contains("namespace "))
        {
            return source;
        }

        var insertIndex = 0;
        while (true)
        {
            var remainder = source[insertIndex..].TrimStart();
            var skipped = source[insertIndex..].Length - remainder.Length;
            insertIndex += skipped;

            if (!source[insertIndex..].StartsWith("import "))
            {
                break;
            }

            var semicolonIndex = source.IndexOf(';', insertIndex);
            if (semicolonIndex < 0)
            {
                break;
            }

            insertIndex = semicolonIndex + 1;
        }

        return source.Insert(insertIndex, " namespace Test; ");
    }

    private static (string Stdout, string Stderr, int ExitCode) ExecuteRunCommand(string filePath)
    {
        var command = new RunFile { File = filePath };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            command.Run(null!);
            return (stdout.ToString(), stderr.ToString(), Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
