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

        if (!TryLoadWithPathImports(rootFullPath, rootFullPath, expectedNamespaceTail: null, orderedUnits, parsedUnits, visiting, out diagnostics))
        {
            return false;
        }

        diagnostics = ValidateCrossFileFunctionCollisions(orderedUnits);
        if (diagnostics.HasErrors)
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

    private static DiagnosticBag ValidateCrossFileFunctionCollisions(IReadOnlyList<LoadedUnit> loadedUnits)
    {
        var diagnostics = new DiagnosticBag();
        var declarationsByName = new Dictionary<string, (string FilePath, FunctionDeclaration Declaration)>(StringComparer.Ordinal);

        foreach (var loadedUnit in loadedUnits)
        {
            foreach (var declaration in loadedUnit.Unit.Statements.OfType<FunctionDeclaration>())
            {
                var name = declaration.Name.Value;
                if (!declarationsByName.TryGetValue(name, out var existing))
                {
                    declarationsByName[name] = (loadedUnit.FilePath, declaration);
                    continue;
                }

                if (string.Equals(existing.FilePath, loadedUnit.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                diagnostics.Report(
                    declaration.Span,
                    $"duplicate top-level function '{name}' across modules '{existing.FilePath}' and '{loadedUnit.FilePath}'",
                    "CLI016");
            }
        }

        return diagnostics;
    }

    private static bool TryLoadWithPathImports(
        string filePath,
        string rootFilePath,
        string? expectedNamespaceTail,
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

        if (!TryParseFile(filePath, string.Equals(filePath, rootFilePath, StringComparison.OrdinalIgnoreCase), out var unit, out diagnostics))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedNamespaceTail) &&
            !IsNamespaceCompatibleWithImport(unit, expectedNamespaceTail!, out var namespaceMismatchMessage))
        {
            diagnostics = new DiagnosticBag();
            diagnostics.Report(Span.Empty, $"{namespaceMismatchMessage} in '{filePath}'", "CLI019");
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

                if (visiting.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
                {
                    var cycle = string.Join(" -> ", visiting.Reverse().Append(resolvedPath).Select(Path.GetFileName));
                    diagnostics = new DiagnosticBag();
                    diagnostics.Report(statement.Span, $"cyclic path import detected: {cycle}", "CLI008");
                    return false;
                }

                var expectedTail = Path.GetFileNameWithoutExtension(resolvedPath);
                if (!TryLoadWithPathImports(resolvedPath, rootFilePath, expectedTail, orderedUnits, parsedUnits, visiting, out diagnostics))
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

    private static bool IsNamespaceCompatibleWithImport(
        CompilationUnit unit,
        string expectedTail,
        out string message)
    {
        message = string.Empty;

        var namespaceStatement = unit.Statements.OfType<NamespaceStatement>().FirstOrDefault();
        if (namespaceStatement == null)
        {
            return true;
        }

        var actual = namespaceStatement.QualifiedName;
        var lastDot = actual.LastIndexOf('.');
        var actualTail = lastDot < 0 ? actual : actual[(lastDot + 1)..];
        if (string.Equals(actualTail, expectedTail, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        message = $"namespace '{actual}' is incompatible with imported file name; expected namespace to end with '{expectedTail}'";
        return false;
    }

    private static bool TryParseFile(string filePath, bool isRootFile, out CompilationUnit unit, out DiagnosticBag diagnostics)
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

        ValidateFileStructure(unit, filePath, isRootFile, diagnostics);
        if (diagnostics.HasErrors)
        {
            return false;
        }

        return true;
    }

    private static void ValidateFileStructure(CompilationUnit unit, string filePath, bool isRootFile, DiagnosticBag diagnostics)
    {
        var seenNamespace = false;
        var seenDeclaration = false;

        foreach (var statement in unit.Statements)
        {
            switch (statement)
            {
                case ImportStatement:
                    if (seenNamespace || seenDeclaration)
                    {
                        diagnostics.Report(
                            statement.Span,
                            $"import statements must appear before namespace and other top-level declarations in '{filePath}'",
                            "CLI011");
                    }

                    break;
                case NamespaceStatement:
                    if (seenDeclaration)
                    {
                        diagnostics.Report(
                            statement.Span,
                            $"namespace declaration must appear before top-level declarations in '{filePath}'",
                            "CLI012");
                    }

                    if (seenNamespace)
                    {
                        diagnostics.Report(statement.Span, $"duplicate namespace declaration in '{filePath}'", "CLI013");
                    }

                    seenNamespace = true;
                    break;
                default:
                    if (!isRootFile && statement is not FunctionDeclaration)
                    {
                        diagnostics.Report(
                            statement.Span,
                            $"imported modules may only declare top-level functions in '{filePath}'",
                            "CLI017");
                    }

                    seenDeclaration = true;
                    break;
            }

            if (!isRootFile && statement is FunctionDeclaration { Name.Value: "Main" })
            {
                diagnostics.Report(statement.Span, $"imported modules must not declare 'Main' in '{filePath}'", "CLI018");
            }

            ValidateStatementStructure(statement, isTopLevel: true, filePath, diagnostics);
        }

        if (!seenNamespace)
        {
            diagnostics.Report(Span.Empty, $"missing required file-scoped namespace declaration in '{filePath}'", "CLI010");
        }
    }

    private static void ValidateStatementStructure(
        IStatement statement,
        bool isTopLevel,
        string filePath,
        DiagnosticBag diagnostics)
    {
        if (!isTopLevel && statement is ImportStatement)
        {
            diagnostics.Report(statement.Span, $"import statements are only allowed at the top level in '{filePath}'", "CLI014");
        }

        if (!isTopLevel && statement is NamespaceStatement)
        {
            diagnostics.Report(statement.Span, $"namespace declarations are only allowed at the top level in '{filePath}'", "CLI015");
        }

        switch (statement)
        {
            case FunctionDeclaration functionDeclaration:
                ValidateBlock(functionDeclaration.Body, filePath, diagnostics);
                break;
            case BlockStatement blockStatement:
                ValidateBlock(blockStatement, filePath, diagnostics);
                break;
            case LetStatement { Value: { } value }:
                ValidateExpression(value, filePath, diagnostics);
                break;
            case ReturnStatement { ReturnValue: { } returnValue }:
                ValidateExpression(returnValue, filePath, diagnostics);
                break;
            case ExpressionStatement { Expression: { } expression }:
                ValidateExpression(expression, filePath, diagnostics);
                break;
        }
    }

    private static void ValidateBlock(BlockStatement block, string filePath, DiagnosticBag diagnostics)
    {
        foreach (var statement in block.Statements)
        {
            ValidateStatementStructure(statement, isTopLevel: false, filePath, diagnostics);
        }
    }

    private static void ValidateExpression(IExpression expression, string filePath, DiagnosticBag diagnostics)
    {
        switch (expression)
        {
            case IfExpression ifExpression:
                ValidateExpression(ifExpression.Condition, filePath, diagnostics);
                ValidateBlock(ifExpression.Consequence, filePath, diagnostics);
                if (ifExpression.Alternative != null)
                {
                    ValidateBlock(ifExpression.Alternative, filePath, diagnostics);
                }

                break;
            case PrefixExpression prefixExpression:
                ValidateExpression(prefixExpression.Right, filePath, diagnostics);
                break;
            case InfixExpression infixExpression:
                ValidateExpression(infixExpression.Left, filePath, diagnostics);
                ValidateExpression(infixExpression.Right, filePath, diagnostics);
                break;
            case FunctionLiteral functionLiteral:
                ValidateBlock(functionLiteral.Body, filePath, diagnostics);
                break;
            case CallExpression callExpression:
                ValidateExpression(callExpression.Function, filePath, diagnostics);
                foreach (var argument in callExpression.Arguments)
                {
                    ValidateExpression(argument, filePath, diagnostics);
                }

                break;
            case MemberAccessExpression memberAccessExpression:
                ValidateExpression(memberAccessExpression.Object, filePath, diagnostics);
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ValidateExpression(element, filePath, diagnostics);
                }

                break;
            case IndexExpression indexExpression:
                ValidateExpression(indexExpression.Left, filePath, diagnostics);
                ValidateExpression(indexExpression.Index, filePath, diagnostics);
                break;
        }
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
