using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Cli;

public static class Compilation
{
    private sealed record LoadedUnit(string FilePath, CompilationUnit Unit);
    private sealed record ModuleSemanticResult(string FilePath, NameResolution Names, TypeCheckResult TypeCheck);

    public static bool TryCompileModules(
        string filePath,
        out IReadOnlyList<ModuleAnalysis> modules,
        out CompilationUnit rootUnit,
        out NameResolution rootNameResolution,
        out TypeCheckResult rootTypeCheck,
        out DiagnosticBag diagnostics)
    {
        modules = [];
        rootUnit = null!;
        rootNameResolution = null!;
        rootTypeCheck = null!;
        diagnostics = new DiagnosticBag();

        if (!File.Exists(filePath))
        {
            diagnostics.Report(Span.Empty, $"file not found: {filePath}", "CLI001");
            return false;
        }

        var orderedUnits = new List<LoadedUnit>();
        var parsedUnits = new Dictionary<string, CompilationUnit>(StringComparer.OrdinalIgnoreCase);
        var moduleImports = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var visiting = new Stack<string>();
        var rootFullPath = Path.GetFullPath(filePath);

        if (!TryLoadWithPathImports(rootFullPath, rootFullPath, expectedNamespaceTail: null, orderedUnits, parsedUnits, moduleImports, visiting, out diagnostics))
        {
            return false;
        }

        diagnostics = ValidateCrossFileFunctionCollisions(orderedUnits);
        if (diagnostics.HasErrors)
        {
            return false;
        }

        if (!TryAnalyzeModules(orderedUnits, moduleImports, out var moduleSemantics, out diagnostics))
        {
            return false;
        }

        var moduleByPath = orderedUnits.ToDictionary(u => u.FilePath, StringComparer.OrdinalIgnoreCase);
        var semanticByPath = moduleSemantics.ToDictionary(s => s.FilePath, StringComparer.OrdinalIgnoreCase);
        var builtModules = new List<ModuleAnalysis>(orderedUnits.Count);
        foreach (var loadedUnit in orderedUnits)
        {
            if (!semanticByPath.TryGetValue(loadedUnit.FilePath, out var semantic))
            {
                diagnostics.Report(Span.Empty, $"internal error: missing semantic result for module '{loadedUnit.FilePath}'", "CLI020");
                return false;
            }

            builtModules.Add(new ModuleAnalysis(
                loadedUnit.FilePath,
                loadedUnit.Unit,
                semantic.Names,
                semantic.TypeCheck,
                string.Equals(loadedUnit.FilePath, rootFullPath, StringComparison.OrdinalIgnoreCase)));
        }

        var rootModule = builtModules.Single(m => m.IsRootModule);
        modules = builtModules;
        rootUnit = rootModule.Unit;
        rootNameResolution = rootModule.NameResolution;
        rootTypeCheck = rootModule.TypeCheck;
        return true;
    }

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

        if (!TryCompileModules(filePath, out var modules, out _, out _, out _, out diagnostics))
        {
            return false;
        }

        var rootFilePath = Path.GetFullPath(filePath);
        unit = MergeUnits(modules.Select(m => new LoadedUnit(m.FilePath, m.Unit)).ToList(), rootFilePath);

        var moduleSemantics = modules
            .Select(m => new ModuleSemanticResult(m.FilePath, m.NameResolution, m.TypeCheck))
            .ToList();

        nameResolution = CombineNameResolutions(moduleSemantics);
        typeCheck = CombineTypeCheckResults(moduleSemantics);

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
        Dictionary<string, List<string>> moduleImports,
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
                if (!TryLoadWithPathImports(resolvedPath, rootFilePath, expectedTail, orderedUnits, parsedUnits, moduleImports, visiting, out diagnostics))
                {
                    return false;
                }

                if (!moduleImports.TryGetValue(filePath, out var imports))
                {
                    imports = [];
                    moduleImports[filePath] = imports;
                }

                if (!imports.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
                {
                    imports.Add(resolvedPath);
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

    private static bool TryAnalyzeModules(
        IReadOnlyList<LoadedUnit> loadedUnits,
        IReadOnlyDictionary<string, List<string>> moduleImports,
        out List<ModuleSemanticResult> moduleSemantics,
        out DiagnosticBag diagnostics)
    {
        moduleSemantics = [];
        diagnostics = new DiagnosticBag();

        var modulesByPath = loadedUnits.ToDictionary(u => u.FilePath, StringComparer.OrdinalIgnoreCase);

        var functionDeclsByFile = new Dictionary<string, List<FunctionDeclaration>>(StringComparer.OrdinalIgnoreCase);
        var functionTypesByFile = new Dictionary<string, Dictionary<string, FunctionTypeSymbol>>(StringComparer.OrdinalIgnoreCase);
        foreach (var loadedUnit in loadedUnits)
        {
            var declarations = loadedUnit.Unit.Statements.OfType<FunctionDeclaration>().ToList();
            functionDeclsByFile[loadedUnit.FilePath] = declarations;

            var typeMap = new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal);
            foreach (var declaration in declarations)
            {
                if (TryBindFunctionType(declaration, out var functionType))
                {
                    typeMap[declaration.Name.Value] = functionType;
                }
            }

            functionTypesByFile[loadedUnit.FilePath] = typeMap;
        }

        foreach (var loadedUnit in loadedUnits)
        {
            var reachable = ComputeReachableModules(loadedUnit.FilePath, moduleImports);
            reachable.Remove(loadedUnit.FilePath);

            var externalDeclarations = new List<FunctionDeclaration>();
            var externalFunctionTypes = new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal);

            foreach (var path in reachable)
            {
                if (functionDeclsByFile.TryGetValue(path, out var declarations))
                {
                    externalDeclarations.AddRange(declarations);
                }

                if (functionTypesByFile.TryGetValue(path, out var typeMap))
                {
                    foreach (var pair in typeMap)
                    {
                        externalFunctionTypes[pair.Key] = pair.Value;
                    }
                }
            }

            var resolver = new NameResolver();
            var names = resolver.Resolve(loadedUnit.Unit, externalDeclarations);
            if (names.Diagnostics.HasErrors)
            {
                diagnostics = PrefixDiagnosticsWithFile(names.Diagnostics, loadedUnit.FilePath);
                return false;
            }

            var checker = new TypeChecker();
            var typeCheck = checker.Check(loadedUnit.Unit, names, externalFunctionTypes);
            if (typeCheck.Diagnostics.HasErrors)
            {
                diagnostics = PrefixDiagnosticsWithFile(typeCheck.Diagnostics, loadedUnit.FilePath);
                return false;
            }

            moduleSemantics.Add(new ModuleSemanticResult(loadedUnit.FilePath, names, typeCheck));
        }

        return true;
    }

    private static HashSet<string> ComputeReachableModules(
        string rootFile,
        IReadOnlyDictionary<string, List<string>> moduleImports)
    {
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(rootFile);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!reachable.Add(current))
            {
                continue;
            }

            if (!moduleImports.TryGetValue(current, out var imports))
            {
                continue;
            }

            foreach (var imported in imports)
            {
                stack.Push(imported);
            }
        }

        return reachable;
    }

    private static bool TryBindFunctionType(FunctionDeclaration declaration, out FunctionTypeSymbol functionType)
    {
        var diagnostics = new DiagnosticBag();
        var parameterTypes = new List<TypeSymbol>(declaration.Parameters.Count);
        foreach (var parameter in declaration.Parameters)
        {
            if (parameter.TypeAnnotation == null)
            {
                functionType = null!;
                return false;
            }

            var boundParameterType = TypeAnnotationBinder.Bind(parameter.TypeAnnotation, diagnostics);
            if (boundParameterType == null)
            {
                functionType = null!;
                return false;
            }

            parameterTypes.Add(boundParameterType);
        }

        var returnType = declaration.ReturnTypeAnnotation != null
            ? TypeAnnotationBinder.Bind(declaration.ReturnTypeAnnotation, diagnostics)
            : TypeSymbols.Void;

        if (returnType == null)
        {
            functionType = null!;
            return false;
        }

        functionType = new FunctionTypeSymbol(parameterTypes, returnType);
        return true;
    }

    private static NameResolution CombineNameResolutions(IReadOnlyList<ModuleSemanticResult> moduleSemantics)
    {
        var combined = new NameResolution();
        foreach (var module in moduleSemantics)
        {
            foreach (var pair in module.Names.IdentifierSymbols)
            {
                combined.IdentifierSymbols[pair.Key] = pair.Value;
            }

            foreach (var pair in module.Names.ParameterSymbols)
            {
                combined.ParameterSymbols[pair.Key] = pair.Value;
            }

            foreach (var pair in module.Names.FunctionCaptures)
            {
                combined.FunctionCaptures[pair.Key] = pair.Value;
            }

            foreach (var functionName in module.Names.GlobalFunctionNames)
            {
                combined.GlobalFunctionNames.Add(functionName);
            }

            foreach (var pair in module.Names.ImportedTypeAliases)
            {
                combined.ImportedTypeAliases[pair.Key] = pair.Value;
            }

            foreach (var importedNamespace in module.Names.ImportedNamespaces)
            {
                combined.ImportedNamespaces.Add(importedNamespace);
            }

            if (combined.FileNamespace == null)
            {
                combined.FileNamespace = module.Names.FileNamespace;
            }
        }

        return combined;
    }

    private static TypeCheckResult CombineTypeCheckResults(IReadOnlyList<ModuleSemanticResult> moduleSemantics)
    {
        var combined = new TypeCheckResult();
        foreach (var module in moduleSemantics)
        {
            foreach (var pair in module.TypeCheck.ExpressionTypes)
            {
                combined.ExpressionTypes[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.ResolvedStaticMethodPaths)
            {
                combined.ResolvedStaticMethodPaths[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.ResolvedStaticValuePaths)
            {
                combined.ResolvedStaticValuePaths[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.VariableTypes)
            {
                combined.VariableTypes[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.FunctionTypes)
            {
                combined.FunctionTypes[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.DeclaredFunctionTypes)
            {
                combined.DeclaredFunctionTypes[pair.Key] = pair.Value;
            }
        }

        return combined;
    }

    private static DiagnosticBag PrefixDiagnosticsWithFile(DiagnosticBag source, string filePath)
    {
        var prefixed = new DiagnosticBag();
        var fileName = Path.GetFileName(filePath);
        foreach (var diagnostic in source.All)
        {
            prefixed.Report(
                diagnostic.Span,
                $"[{fileName}] {diagnostic.Message}",
                diagnostic.Code,
                diagnostic.Severity);
        }

        return prefixed;
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
