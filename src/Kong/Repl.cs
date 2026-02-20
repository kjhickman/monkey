namespace Kong;

public static class Repl
{
    private const string Prompt = ">> ";

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("REPL CLR execution loads and invokes generated assemblies via reflection.")]
    public static void Start(TextReader input, TextWriter output)
    {
        var lines = new List<string>();

        while (true)
        {
            output.Write(Prompt);
            output.Flush();

            var line = input.ReadLine();
            if (line == null)
                return;

            lines.Add(line);

            var suppressOutput = line.TrimStart().StartsWith("let ", StringComparison.Ordinal);
            var source = string.Join("\n", lines);
            if (suppressOutput)
            {
                source += "\n0;";
            }

            var l = new Lexer(source);
            var p = new Parser(l);

            var unit = p.ParseCompilationUnit();
            if (p.Diagnostics.HasErrors)
            {
                lines.RemoveAt(lines.Count - 1);
                PrintDiagnostics(output, p.Diagnostics);
                continue;
            }

            var resolver = new NameResolver();
            var nameResolution = resolver.Resolve(unit);

            var checker = new TypeChecker();
            var typeCheckResult = checker.Check(unit, nameResolution);
            if (typeCheckResult.Diagnostics.HasErrors)
            {
                lines.RemoveAt(lines.Count - 1);
                PrintDiagnostics(output, typeCheckResult.Diagnostics);
                continue;
            }

            var executor = new ClrPhase1Executor();
            var result = executor.Execute(unit, typeCheckResult, nameResolution);
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
