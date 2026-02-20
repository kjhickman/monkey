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
        diagnostics = new DiagnosticBag();

        foreach (var statement in unit.Statements)
        {
            if (statement is FunctionDeclaration)
            {
                continue;
            }

            diagnostics.Report(statement.Span,
                "top-level statements are not allowed; declare functions only",
                "CLI002");
        }

        var mains = unit.Statements
            .OfType<FunctionDeclaration>()
            .Where(d => d.Name.Value == "Main")
            .ToList();

        if (mains.Count == 0)
        {
            diagnostics.Report(unit.Span, "missing required entrypoint 'fn Main() {}'", "CLI003");
            return !diagnostics.HasErrors;
        }

        if (mains.Count > 1)
        {
            foreach (var declaration in mains)
            {
                diagnostics.Report(declaration.Name.Span,
                    "duplicate 'Main' function declaration",
                    "CLI004");
            }
            return false;
        }

        var main = mains[0];
        if (main.Parameters.Count != 0)
        {
            diagnostics.Report(main.Span, "'Main' must not declare parameters", "CLI005");
        }

        if (typeCheck.DeclaredFunctionTypes.TryGetValue(main, out var mainType))
        {
            if (mainType.ReturnType != TypeSymbols.Void && mainType.ReturnType != TypeSymbols.Int)
            {
                diagnostics.Report(main.Span, "'Main' must have return type 'void' or 'int'", "CLI006");
            }
        }

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
