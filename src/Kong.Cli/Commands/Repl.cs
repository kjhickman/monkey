using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Name = "repl", Description = "Start the Kong REPL", Parent = typeof(Root))]
public class Repl
{
    public bool UseVmBackend { get; set; }

    public void Run(CliContext context)
    {
        var username = Environment.UserName;
        Console.WriteLine($"Hello {username}! This is the Kong programming language!");
        Console.WriteLine("Feel free to type in commands");

        if (ShouldUseVmBackend())
        {
            Kong.Repl.Start(Console.In, Console.Out);
            return;
        }

        StartClrRepl(Console.In, Console.Out);
    }

    private static bool IsVmEnvEnabled()
    {
        var value = Environment.GetEnvironmentVariable("KONG_USE_VM");
        return value is "1" or "true" or "TRUE" or "True";
    }

    private bool ShouldUseVmBackend()
    {
        return UseVmBackend || IsVmEnvEnabled();
    }

    private static void StartClrRepl(TextReader input, TextWriter output)
    {
        var lines = new List<string>();
        while (true)
        {
            output.Write(">> ");
            output.Flush();

            var line = input.ReadLine();
            if (line == null)
            {
                return;
            }

            lines.Add(line);

            var suppressOutput = line.TrimStart().StartsWith("let ", StringComparison.Ordinal);
            var source = string.Join("\n", lines);
            if (suppressOutput)
            {
                source += "\n0;";
            }

            var lexer = new Lexer(source);
            var parser = new Parser(lexer);
            var unit = parser.ParseCompilationUnit();
            if (parser.Diagnostics.HasErrors)
            {
                lines.RemoveAt(lines.Count - 1);
                PrintDiagnostics(output, parser.Diagnostics);
                continue;
            }

            var resolver = new NameResolver();
            var names = resolver.Resolve(unit);

            var checker = new TypeChecker();
            var typeCheck = checker.Check(unit, names);
            if (typeCheck.Diagnostics.HasErrors)
            {
                lines.RemoveAt(lines.Count - 1);
                PrintDiagnostics(output, typeCheck.Diagnostics);
                continue;
            }

            var executor = new ClrPhase1Executor();
            var result = executor.Execute(unit, typeCheck, names);
            if (!result.Executed)
            {
                lines.RemoveAt(lines.Count - 1);
                PrintDiagnostics(output, result.Diagnostics);
                continue;
            }

            if (!suppressOutput)
            {
                output.WriteLine(result.Value);
            }
        }
    }

    private static void PrintDiagnostics(TextWriter output, DiagnosticBag diagnostics)
    {
        output.WriteLine("Whoops! We ran into some monkey business here!");
        foreach (var d in diagnostics.All)
        {
            output.WriteLine($"\t{d}");
        }
    }
}
