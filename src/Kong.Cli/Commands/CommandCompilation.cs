namespace Kong.Cli.Commands;

internal static class CommandCompilation
{
    public static bool TryCompileFile(
        string filePath,
        out CompilationUnit unit,
        out NameResolution nameResolution,
        out TypeCheckResult typeCheck,
        out DiagnosticBag diagnostics)
    {
        unit = null!;
        nameResolution = null!;
        typeCheck = null!;
        diagnostics = new DiagnosticBag();

        if (!System.IO.File.Exists(filePath))
        {
            diagnostics.Report(Span.Empty, $"file not found: {filePath}", "CLI001");
            return false;
        }

        var source = System.IO.File.ReadAllText(filePath);
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);

        unit = parser.ParseCompilationUnit();
        if (parser.Diagnostics.HasErrors)
        {
            diagnostics = parser.Diagnostics;
            return false;
        }

        var resolver = new NameResolver();
        nameResolution = resolver.Resolve(unit);

        var checker = new TypeChecker();
        typeCheck = checker.Check(unit, nameResolution);
        if (typeCheck.Diagnostics.HasErrors)
        {
            diagnostics = typeCheck.Diagnostics;
            return false;
        }

        return true;
    }

    public static bool ValidateProgramEntrypoint(
        CompilationUnit unit,
        TypeCheckResult typeCheck,
        out DiagnosticBag diagnostics)
    {
        diagnostics = ProgramValidator.ValidateEntrypoint(unit, typeCheck);
        return !diagnostics.HasErrors;
    }

    public static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        Console.Error.WriteLine("Whoops! We ran into some monkey business here!");
        foreach (var d in diagnostics.All)
        {
            Console.Error.WriteLine($"\t{d}");
        }
    }
}
