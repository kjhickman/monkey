using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Cli;

public static class Compilation
{
    private sealed record LoadedUnit(string FilePath, CompilationUnit Unit);

    public static bool TryCompile(
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

        if (!File.Exists(filePath))
        {
            diagnostics.Report(Span.Empty, $"file not found: {filePath}", "CLI001");
            return false;
        }

        var orderedUnits = new List<LoadedUnit>();
        var parsedUnits = new Dictionary<string, CompilationUnit>(StringComparer.OrdinalIgnoreCase);
        var visiting = new Stack<string>();
        var rootFullPath = Path.GetFullPath(filePath);

        if (!TryLoadWithPathImports(rootFullPath, orderedUnits, parsedUnits, visiting, out diagnostics))
        {
            return false;
        }

        unit = MergeUnits(orderedUnits, rootFullPath);

        var resolver = new NameResolver();
        nameResolution = resolver.Resolve(unit);
        if (nameResolution.Diagnostics.HasErrors)
        {
            diagnostics = nameResolution.Diagnostics;
            return false;
        }

        var checker = new TypeChecker();
        typeCheck = checker.Check(unit, nameResolution);
        if (typeCheck.Diagnostics.HasErrors)
        {
            diagnostics = typeCheck.Diagnostics;
            return false;
        }

        return true;
    }

    private static bool TryLoadWithPathImports(
        string filePath,
        List<LoadedUnit> orderedUnits,
        Dictionary<string, CompilationUnit> parsedUnits,
        Stack<string> visiting,
        out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();

        if (parsedUnits.ContainsKey(filePath))
        {
            return true;
        }

        if (visiting.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            var cycle = string.Join(" -> ", visiting.Reverse().Append(filePath).Select(Path.GetFileName));
            diagnostics.Report(Span.Empty, $"cyclic path import detected: {cycle}", "CLI008");
            return false;
        }

        if (!TryParseFile(filePath, out var unit, out diagnostics))
        {
            return false;
        }

        visiting.Push(filePath);

        try
        {
            var containingDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
            foreach (var statement in unit.Statements.OfType<ImportStatement>().Where(i => i.IsPathImport))
            {
                var rawPath = statement.Path!;
                if (!TryResolveImportPath(containingDirectory, rawPath, out var resolvedPath, out var pathError))
                {
                    diagnostics = new DiagnosticBag();
                    diagnostics.Report(statement.Span, pathError, "CLI009");
                    return false;
                }

                if (!TryLoadWithPathImports(resolvedPath, orderedUnits, parsedUnits, visiting, out diagnostics))
                {
                    return false;
                }
            }
        }
        finally
        {
            visiting.Pop();
        }

        parsedUnits[filePath] = unit;
        orderedUnits.Add(new LoadedUnit(filePath, unit));
        return true;
    }

    private static bool TryParseFile(string filePath, out CompilationUnit unit, out DiagnosticBag diagnostics)
    {
        unit = null!;
        diagnostics = new DiagnosticBag();

        if (!File.Exists(filePath))
        {
            diagnostics.Report(Span.Empty, $"file not found: {filePath}", "CLI001");
            return false;
        }

        var source = File.ReadAllText(filePath);
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);
        unit = parser.ParseCompilationUnit();
        if (parser.Diagnostics.HasErrors)
        {
            diagnostics = parser.Diagnostics;
            return false;
        }

        return true;
    }

    private static bool TryResolveImportPath(string containingDirectory, string rawPath, out string resolvedPath, out string error)
    {
        resolvedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "path import must specify a non-empty file path";
            return false;
        }

        if (!rawPath.EndsWith(".kg", StringComparison.OrdinalIgnoreCase))
        {
            error = $"path import '{rawPath}' must reference a .kg file";
            return false;
        }

        resolvedPath = Path.GetFullPath(rawPath, containingDirectory);
        if (!File.Exists(resolvedPath))
        {
            error = $"imported file not found: {resolvedPath}";
            return false;
        }

        return true;
    }

    private static CompilationUnit MergeUnits(IReadOnlyList<LoadedUnit> loadedUnits, string rootFilePath)
    {
        var rootUnit = loadedUnits.Last(unit => string.Equals(unit.FilePath, rootFilePath, StringComparison.OrdinalIgnoreCase)).Unit;

        var merged = new CompilationUnit();
        var importStatements = new List<ImportStatement>();
        var seenImports = new HashSet<string>(StringComparer.Ordinal);
        var declarationStatements = new List<IStatement>();
        NamespaceStatement? rootNamespace = null;

        foreach (var loaded in loadedUnits)
        {
            foreach (var statement in loaded.Unit.Statements)
            {
                switch (statement)
                {
                    case ImportStatement importStatement when importStatement.IsPathImport:
                        break;
                    case ImportStatement importStatement:
                    {
                        var key = importStatement.QualifiedName;
                        if (seenImports.Add(key))
                        {
                            importStatements.Add(importStatement);
                        }

                        break;
                    }
                    case NamespaceStatement namespaceStatement:
                        if (ReferenceEquals(loaded.Unit, rootUnit))
                        {
                            rootNamespace ??= namespaceStatement;
                        }

                        break;
                    default:
                        declarationStatements.Add(statement);
                        break;
                }
            }
        }

        foreach (var importStatement in importStatements)
        {
            merged.Statements.Add(importStatement);
        }

        if (rootNamespace != null)
        {
            merged.Statements.Add(rootNamespace);
        }

        foreach (var declaration in declarationStatements)
        {
            merged.Statements.Add(declaration);
        }

        if (merged.Statements.Count > 0)
        {
            merged.Span = new Span(merged.Statements[0].Span.Start, merged.Statements[^1].Span.End);
        }

        return merged;
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
