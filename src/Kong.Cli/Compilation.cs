using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Cli;

public static class Compilation
{
    private sealed record LoadedUnit(string FilePath, CompilationUnit Unit, string FileNamespace);
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

        var rootFullPath = Path.GetFullPath(filePath);
        var rootDirectory = Path.GetDirectoryName(rootFullPath) ?? Directory.GetCurrentDirectory();

        if (!TryLoadProjectUnits(rootFullPath, rootDirectory, out var orderedUnits, out diagnostics))
        {
            return false;
        }

        diagnostics = ValidateCrossFileFunctionCollisions(orderedUnits);
        if (diagnostics.HasErrors)
        {
            return false;
        }

        if (!TryAnalyzeModules(orderedUnits, out var moduleSemantics, out diagnostics))
        {
            return false;
        }

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
        unit = MergeUnits(modules.Select(m => new LoadedUnit(m.FilePath, m.Unit, m.NameResolution.FileNamespace ?? string.Empty)).ToList(), rootFilePath);

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
        var declarationsByKey = new Dictionary<string, (string FilePath, FunctionDeclaration Declaration)>(StringComparer.Ordinal);

        foreach (var loadedUnit in loadedUnits)
        {
            foreach (var declaration in loadedUnit.Unit.Statements.OfType<FunctionDeclaration>())
            {
                var key = loadedUnit.FileNamespace + "::" + declaration.Name.Value;
                if (!declarationsByKey.TryGetValue(key, out var existing))
                {
                    declarationsByKey[key] = (loadedUnit.FilePath, declaration);
                    continue;
                }

                if (string.Equals(existing.FilePath, loadedUnit.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                diagnostics.Report(
                    declaration.Span,
                    $"duplicate top-level function '{declaration.Name.Value}' in namespace '{loadedUnit.FileNamespace}' across modules '{existing.FilePath}' and '{loadedUnit.FilePath}'",
                    "CLI016");
            }
        }

        return diagnostics;
    }

    private static bool TryLoadProjectUnits(
        string rootFilePath,
        string rootDirectory,
        out List<LoadedUnit> loadedUnits,
        out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        loadedUnits = [];

        var discoveredPaths = Directory
            .GetFiles(rootDirectory, "*.kg", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToList();

        if (!discoveredPaths.Contains(rootFilePath, StringComparer.OrdinalIgnoreCase))
        {
            discoveredPaths.Add(rootFilePath);
        }

        discoveredPaths.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var discoveredPath in discoveredPaths)
        {
            var isRootFile = string.Equals(discoveredPath, rootFilePath, StringComparison.OrdinalIgnoreCase);
            if (!TryParseFile(discoveredPath, isRootFile, out var unit, out diagnostics))
            {
                return false;
            }

            var fileNamespace = GetFileNamespace(unit);
            if (fileNamespace == null)
            {
                diagnostics = new DiagnosticBag();
                diagnostics.Report(Span.Empty, $"missing required file-scoped module declaration in '{discoveredPath}'", "CLI010");
                return false;
            }

            loadedUnits.Add(new LoadedUnit(discoveredPath, unit, fileNamespace));
        }

        return true;
    }

    private static string? GetFileNamespace(CompilationUnit unit)
    {
        return unit.Statements.OfType<NamespaceStatement>().FirstOrDefault()?.QualifiedName;
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
                            $"use statements must appear before module and other top-level declarations in '{filePath}'",
                            "CLI011");
                    }

                    break;
                case NamespaceStatement:
                    if (seenDeclaration)
                    {
                        diagnostics.Report(
                            statement.Span,
                            $"module declaration must appear before top-level declarations in '{filePath}'",
                            "CLI012");
                    }

                    if (seenNamespace)
                    {
                        diagnostics.Report(statement.Span, $"duplicate module declaration in '{filePath}'", "CLI013");
                    }

                    seenNamespace = true;
                    break;
                default:
                    if (!isRootFile &&
                        statement is not FunctionDeclaration &&
                        statement is not EnumDeclaration &&
                        statement is not ClassDeclaration &&
                        statement is not InterfaceDeclaration &&
                        statement is not ImplBlock &&
                        statement is not InterfaceImplBlock)
                    {
                        diagnostics.Report(
                            statement.Span,
                            $"non-root modules may only declare top-level functions, enums, classes, interfaces, and impl blocks in '{filePath}'",
                            "CLI017");
                    }

                    seenDeclaration = true;
                    break;
            }

            if (!isRootFile && statement is FunctionDeclaration { Name.Value: "Main" })
            {
                diagnostics.Report(statement.Span, $"non-root modules must not declare 'Main' in '{filePath}'", "CLI018");
            }

            ValidateStatementStructure(statement, isTopLevel: true, filePath, diagnostics);
        }

        if (!seenNamespace)
        {
            diagnostics.Report(Span.Empty, $"missing required file-scoped module declaration in '{filePath}'", "CLI010");
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
            diagnostics.Report(statement.Span, $"use statements are only allowed at the top level in '{filePath}'", "CLI014");
        }

        if (!isTopLevel && statement is NamespaceStatement)
        {
            diagnostics.Report(statement.Span, $"module declarations are only allowed at the top level in '{filePath}'", "CLI015");
        }

        switch (statement)
        {
            case FunctionDeclaration functionDeclaration:
                ValidateBlock(functionDeclaration.Body, filePath, diagnostics);
                break;
            case EnumDeclaration:
            case ClassDeclaration:
            case InterfaceDeclaration:
                break;
            case ImplBlock implBlock:
                if (implBlock.Constructor != null)
                {
                    ValidateBlock(implBlock.Constructor.Body, filePath, diagnostics);
                }

                foreach (var method in implBlock.Methods)
                {
                    ValidateBlock(method.Body, filePath, diagnostics);
                }

                break;
            case InterfaceImplBlock interfaceImplBlock:
                foreach (var method in interfaceImplBlock.Methods)
                {
                    ValidateBlock(method.Body, filePath, diagnostics);
                }

                break;
            case BlockStatement blockStatement:
                ValidateBlock(blockStatement, filePath, diagnostics);
                break;
            case LetStatement { Value: { } value }:
                ValidateExpression(value, filePath, diagnostics);
                break;
            case AssignmentStatement assignmentStatement:
                ValidateExpression(assignmentStatement.Value, filePath, diagnostics);
                break;
            case IndexAssignmentStatement indexAssignmentStatement:
                ValidateExpression(indexAssignmentStatement.Target.Left, filePath, diagnostics);
                ValidateExpression(indexAssignmentStatement.Target.Index, filePath, diagnostics);
                ValidateExpression(indexAssignmentStatement.Value, filePath, diagnostics);
                break;
            case MemberAssignmentStatement memberAssignmentStatement:
                ValidateExpression(memberAssignmentStatement.Target.Object, filePath, diagnostics);
                ValidateExpression(memberAssignmentStatement.Value, filePath, diagnostics);
                break;
            case ReturnStatement { ReturnValue: { } returnValue }:
                ValidateExpression(returnValue, filePath, diagnostics);
                break;
            case ForInStatement forInStatement:
                ValidateExpression(forInStatement.Iterable, filePath, diagnostics);
                ValidateBlock(forInStatement.Body, filePath, diagnostics);
                break;
            case BreakStatement:
            case ContinueStatement:
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
            case MatchExpression matchExpression:
                ValidateExpression(matchExpression.Target, filePath, diagnostics);
                foreach (var arm in matchExpression.Arms)
                {
                    ValidateBlock(arm.Body, filePath, diagnostics);
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
                    ValidateExpression(argument.Expression, filePath, diagnostics);
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
            case NewExpression newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    ValidateExpression(argument, filePath, diagnostics);
                }
                break;
        }
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
        out List<ModuleSemanticResult> moduleSemantics,
        out DiagnosticBag diagnostics)
    {
        moduleSemantics = [];
        diagnostics = new DiagnosticBag();

        var modulesByNamespace = loadedUnits
            .GroupBy(u => u.FileNamespace, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(u => u.FilePath).ToList(), StringComparer.Ordinal);

        var functionDeclsByFile = new Dictionary<string, List<FunctionDeclaration>>(StringComparer.OrdinalIgnoreCase);
        var functionTypesByFile = new Dictionary<string, Dictionary<string, FunctionTypeSymbol>>(StringComparer.OrdinalIgnoreCase);
        var enumDeclsByFile = new Dictionary<string, List<EnumDeclaration>>(StringComparer.OrdinalIgnoreCase);
        var namespaceByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loadedUnit in loadedUnits)
        {
            var declarations = loadedUnit.Unit.Statements.OfType<FunctionDeclaration>().ToList();
            functionDeclsByFile[loadedUnit.FilePath] = declarations;
            enumDeclsByFile[loadedUnit.FilePath] = loadedUnit.Unit.Statements.OfType<EnumDeclaration>().ToList();
            namespaceByFile[loadedUnit.FilePath] = loadedUnit.FileNamespace;

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
            var importedNamespaces = loadedUnit.Unit.Statements
                .OfType<ImportStatement>()
                .Select(i => i.QualifiedName)
                .ToHashSet(StringComparer.Ordinal);
            importedNamespaces.Add(loadedUnit.FileNamespace);
            var calledFunctionNames = CollectDirectCallIdentifierNames(loadedUnit.Unit);

            var visibleModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var importedNamespace in importedNamespaces)
            {
                if (!modulesByNamespace.TryGetValue(importedNamespace, out var modulePaths))
                {
                    continue;
                }

                foreach (var modulePath in modulePaths)
                {
                    visibleModules.Add(modulePath);
                }
            }

            visibleModules.Remove(loadedUnit.FilePath);

            var externalDeclarations = new List<FunctionDeclaration>();
            var externalEnumDeclarations = new List<EnumDeclaration>();
            var externalFunctionTypes = new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal);
            var privateImportedFunctionNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in visibleModules)
            {
                var isSameNamespace = namespaceByFile.TryGetValue(path, out var namespaceName) &&
                    string.Equals(namespaceName, loadedUnit.FileNamespace, StringComparison.Ordinal);

                if (functionDeclsByFile.TryGetValue(path, out var declarations))
                {
                    if (isSameNamespace)
                    {
                        externalDeclarations.AddRange(declarations);
                    }
                    else
                    {
                        foreach (var declaration in declarations.Where(d => !d.IsPublic))
                        {
                            privateImportedFunctionNames.Add(declaration.Name.Value);
                        }

                        externalDeclarations.AddRange(declarations.Where(d => d.IsPublic));
                    }
                }

                if (functionTypesByFile.TryGetValue(path, out var typeMap))
                {
                    if (isSameNamespace)
                    {
                        foreach (var pair in typeMap)
                        {
                            externalFunctionTypes[pair.Key] = pair.Value;
                        }
                    }
                    else
                    {
                        if (!functionDeclsByFile.TryGetValue(path, out var declarationsForVisibility))
                        {
                            continue;
                        }

                        var publicDeclarationNames = declarationsForVisibility
                            .Where(d => d.IsPublic)
                            .Select(d => d.Name.Value)
                            .ToHashSet(StringComparer.Ordinal);

                        foreach (var pair in typeMap)
                        {
                            if (publicDeclarationNames.Contains(pair.Key))
                            {
                                externalFunctionTypes[pair.Key] = pair.Value;
                            }
                        }
                    }
                }

                if (enumDeclsByFile.TryGetValue(path, out var enumDeclarations))
                {
                    externalEnumDeclarations.AddRange(enumDeclarations);
                }
            }

            var resolver = new NameResolver();
            var names = resolver.Resolve(loadedUnit.Unit, externalDeclarations, externalEnumDeclarations);
            if (names.Diagnostics.HasErrors)
            {
                var visibilityDiagnostics = BuildVisibilityDiagnostics(
                    names.Diagnostics,
                    privateImportedFunctionNames,
                    calledFunctionNames);

                if (visibilityDiagnostics.Count == 0)
                {
                    diagnostics = PrefixDiagnosticsWithFile(names.Diagnostics, loadedUnit.FilePath);
                    return false;
                }

                var combinedDiagnostics = new DiagnosticBag();
                combinedDiagnostics.AddRange(names.Diagnostics);
                combinedDiagnostics.AddRange(visibilityDiagnostics);
                diagnostics = PrefixDiagnosticsWithFile(combinedDiagnostics, loadedUnit.FilePath);
                return false;
            }

            var checker = new TypeChecker();
            var typeCheck = checker.Check(loadedUnit.Unit, names, externalFunctionTypes, externalEnumDeclarations);
            if (typeCheck.Diagnostics.HasErrors)
            {
                diagnostics = PrefixDiagnosticsWithFile(typeCheck.Diagnostics, loadedUnit.FilePath);
                return false;
            }

            moduleSemantics.Add(new ModuleSemanticResult(loadedUnit.FilePath, names, typeCheck));
        }

        return true;
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

            foreach (var pair in module.TypeCheck.ClassDefinitions)
            {
                combined.ClassDefinitions[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.InterfaceDefinitions)
            {
                combined.InterfaceDefinitions[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.EnumDefinitions)
            {
                combined.EnumDefinitions[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.ResolvedEnumVariantConstructions)
            {
                combined.ResolvedEnumVariantConstructions[pair.Key] = pair.Value;
            }

            foreach (var pair in module.TypeCheck.ResolvedMatches)
            {
                combined.ResolvedMatches[pair.Key] = pair.Value;
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

    private static DiagnosticBag BuildVisibilityDiagnostics(
        DiagnosticBag source,
        IReadOnlySet<string> privateImportedFunctionNames,
        IReadOnlySet<string> calledFunctionNames)
    {
        var diagnostics = new DiagnosticBag();
        foreach (var diagnostic in source.All)
        {
            if (diagnostic.Code != "N001")
            {
                continue;
            }

            if (!TryExtractUndefinedIdentifierName(diagnostic.Message, out var identifierName))
            {
                continue;
            }

            if (!privateImportedFunctionNames.Contains(identifierName) || !calledFunctionNames.Contains(identifierName))
            {
                continue;
            }

            diagnostics.Report(
                diagnostic.Span,
                $"function '{identifierName}' is private to its namespace; declare it as 'public fn' to access it from another namespace",
                "CLI019");
        }

        return diagnostics;
    }

    private static bool TryExtractUndefinedIdentifierName(string message, out string name)
    {
        name = string.Empty;
        const string prefix = "undefined variable '";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var endQuote = message.LastIndexOf("'", StringComparison.Ordinal);
        if (endQuote <= prefix.Length)
        {
            return false;
        }

        name = message[prefix.Length..endQuote];
        return name.Length > 0;
    }

    private static HashSet<string> CollectDirectCallIdentifierNames(CompilationUnit unit)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in unit.Statements)
        {
            CollectDirectCallIdentifierNames(statement, names);
        }

        return names;
    }

    private static void CollectDirectCallIdentifierNames(IStatement statement, HashSet<string> names)
    {
        switch (statement)
        {
            case LetStatement { Value: { } value }:
                CollectDirectCallIdentifierNames(value, names);
                break;
            case AssignmentStatement assignmentStatement:
                CollectDirectCallIdentifierNames(assignmentStatement.Value, names);
                break;
            case IndexAssignmentStatement indexAssignmentStatement:
                CollectDirectCallIdentifierNames(indexAssignmentStatement.Target.Left, names);
                CollectDirectCallIdentifierNames(indexAssignmentStatement.Target.Index, names);
                CollectDirectCallIdentifierNames(indexAssignmentStatement.Value, names);
                break;
            case ForInStatement forInStatement:
                CollectDirectCallIdentifierNames(forInStatement.Iterable, names);
                foreach (var nested in forInStatement.Body.Statements)
                {
                    CollectDirectCallIdentifierNames(nested, names);
                }
                break;
            case FunctionDeclaration declaration:
                foreach (var nested in declaration.Body.Statements)
                {
                    CollectDirectCallIdentifierNames(nested, names);
                }
                break;
            case ReturnStatement { ReturnValue: { } returnValue }:
                CollectDirectCallIdentifierNames(returnValue, names);
                break;
            case ExpressionStatement { Expression: { } expression }:
                CollectDirectCallIdentifierNames(expression, names);
                break;
            case BlockStatement blockStatement:
                foreach (var nested in blockStatement.Statements)
                {
                    CollectDirectCallIdentifierNames(nested, names);
                }
                break;
        }
    }

    private static void CollectDirectCallIdentifierNames(IExpression expression, HashSet<string> names)
    {
        switch (expression)
        {
            case IfExpression ifExpression:
                CollectDirectCallIdentifierNames(ifExpression.Condition, names);
                foreach (var statement in ifExpression.Consequence.Statements)
                {
                    CollectDirectCallIdentifierNames(statement, names);
                }

                if (ifExpression.Alternative != null)
                {
                    foreach (var statement in ifExpression.Alternative.Statements)
                    {
                        CollectDirectCallIdentifierNames(statement, names);
                    }
                }
                break;
            case PrefixExpression prefixExpression:
                CollectDirectCallIdentifierNames(prefixExpression.Right, names);
                break;
            case InfixExpression infixExpression:
                CollectDirectCallIdentifierNames(infixExpression.Left, names);
                CollectDirectCallIdentifierNames(infixExpression.Right, names);
                break;
            case FunctionLiteral functionLiteral:
                foreach (var statement in functionLiteral.Body.Statements)
                {
                    CollectDirectCallIdentifierNames(statement, names);
                }
                break;
            case CallExpression callExpression:
                if (callExpression.Function is Identifier identifier)
                {
                    names.Add(identifier.Value);
                }

                CollectDirectCallIdentifierNames(callExpression.Function, names);
                foreach (var argument in callExpression.Arguments)
                {
                    CollectDirectCallIdentifierNames(argument.Expression, names);
                }
                break;
            case MemberAccessExpression memberAccessExpression:
                CollectDirectCallIdentifierNames(memberAccessExpression.Object, names);
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    CollectDirectCallIdentifierNames(element, names);
                }
                break;
            case IndexExpression indexExpression:
                CollectDirectCallIdentifierNames(indexExpression.Left, names);
                CollectDirectCallIdentifierNames(indexExpression.Index, names);
                break;
            case NewExpression newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    CollectDirectCallIdentifierNames(argument, names);
                }
                break;
        }
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
