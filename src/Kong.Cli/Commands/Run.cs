using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Name = "run", Description = "Run a file", Parent = typeof(Root))]
public class RunFile
{
    [CliArgument(Description = "File to run", Required = true)]
    public string File { get; set; } = null!;

    public void Run(CliContext context)
    {
        if (!System.IO.File.Exists(File))
        {
            Console.Error.WriteLine($"File not found: {File}");
            return;
        }

        var source = System.IO.File.ReadAllText(File);
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);

        var program = parser.ParseCompilationUnit();
        if (parser.Diagnostics.HasErrors)
        {
            PrintDiagnostics(parser.Diagnostics);
            return;
        }

        var resolver = new NameResolver();
        var nameResolution = resolver.Resolve(program);

        var checker = new TypeChecker();
        var typeCheckResult = checker.Check(program, nameResolution);
        if (typeCheckResult.Diagnostics.HasErrors)
        {
            PrintDiagnostics(typeCheckResult.Diagnostics);
            return;
        }

        var clrExecutor = new ClrPhase1Executor();
        var clrResult = clrExecutor.Execute(program, typeCheckResult, nameResolution);
        if (clrResult.Executed)
        {
            Console.WriteLine(clrResult.Value);
            return;
        }

        if (!clrResult.IsUnsupported)
        {
            PrintDiagnostics(clrResult.Diagnostics);
            return;
        }

        PrintDiagnostics(clrResult.Diagnostics);
    }

    private static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        Console.Error.WriteLine("Whoops! We ran into some monkey business here!");
        foreach (var d in diagnostics.All)
        {
            Console.Error.WriteLine($"\t{d}");
        }
    }
}
