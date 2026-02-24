using System.Reflection;
using System.Runtime.Loader;
using Kong.CodeGeneration;
using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Benchmarks;

public sealed class CompiledKongProgram : IDisposable
{
    private readonly string _outputDirectory;
    private readonly AssemblyLoadContext _assemblyLoadContext;

    private CompiledKongProgram(string outputDirectory, AssemblyLoadContext assemblyLoadContext, Func<int> entryPoint)
    {
        _outputDirectory = outputDirectory;
        _assemblyLoadContext = assemblyLoadContext;
        EntryPoint = entryPoint;
    }

    public Func<int> EntryPoint { get; }

    public static CompiledKongProgram CompileIntProgram(string source, string assemblyName)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "kong-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var unit = Parse(source);

            var resolver = new NameResolver();
            var names = resolver.Resolve(unit);
            EnsureNoErrors("name resolution", names.Diagnostics);

            var checker = new TypeChecker();
            var typeCheck = checker.Check(unit, names);
            EnsureNoErrors("type checking", typeCheck.Diagnostics);

            var builder = new ClrArtifactBuilder();
            var build = builder.BuildArtifact(unit, typeCheck, outputDirectory, assemblyName, names);
            if (!build.Built || build.AssemblyPath is null)
            {
                throw new InvalidOperationException(FormatDiagnostics("CLR artifact generation", build.Diagnostics));
            }

            var loadContext = new AssemblyLoadContext($"kong-benchmark-load-context-{Guid.NewGuid():N}", isCollectible: true);
            var assembly = loadContext.LoadFromAssemblyPath(build.AssemblyPath);
            var evalMethod = assembly
                .GetTypes()
                .Select(t => t.GetMethod("Eval", BindingFlags.Public | BindingFlags.Static))
                .FirstOrDefault(m => m is not null && m.ReturnType == typeof(int) && m.GetParameters().Length == 0)
                ?? throw new InvalidOperationException("Generated assembly did not contain an Eval() method.");

            var entryPoint = evalMethod.CreateDelegate<Func<int>>();
            return new CompiledKongProgram(outputDirectory, loadContext, entryPoint);
        }
        catch
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            throw;
        }
    }

    public void Dispose()
    {
        _assemblyLoadContext.Unload();
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private static CompilationUnit Parse(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();
        EnsureNoErrors("parsing", parser.Diagnostics);
        return unit;
    }

    private static void EnsureNoErrors(string phase, DiagnosticBag diagnostics)
    {
        if (diagnostics.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(phase, diagnostics));
        }
    }

    private static string FormatDiagnostics(string phase, DiagnosticBag diagnostics)
    {
        var lines = diagnostics.All.Select(d => d.ToString());
        return $"Kong benchmark setup failed during {phase}.{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}
