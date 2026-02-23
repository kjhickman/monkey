using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public enum NameSymbolKind
{
    Global,
    Local,
    Parameter,
    EnumVariant,
}

public readonly record struct NameSymbol(string Name, NameSymbolKind Kind, Span DeclarationSpan, int FunctionDepth);

public class NameResolution
{
    public Dictionary<Identifier, NameSymbol> IdentifierSymbols { get; } = [];
    public Dictionary<FunctionParameter, NameSymbol> ParameterSymbols { get; } = [];
    public Dictionary<FunctionLiteral, List<NameSymbol>> FunctionCaptures { get; } = [];
    public HashSet<string> GlobalFunctionNames { get; } = [];
    public Dictionary<string, string> ImportedTypeAliases { get; } = [];
    public HashSet<string> ImportedNamespaces { get; } = [];
    public HashSet<NameSymbol> MutableSymbols { get; } = [];
    public string? FileNamespace { get; set; }
    public DiagnosticBag Diagnostics { get; } = new();

    public IReadOnlyList<NameSymbol> GetCapturedSymbols(FunctionLiteral function)
    {
        return FunctionCaptures.GetValueOrDefault(function, []);
    }
}

public class NameResolver
{
    private sealed class Scope(Scope? parent, bool isGlobalRoot, int functionDepth)
    {
        public Scope? Parent { get; } = parent;
        public bool IsGlobalRoot { get; } = isGlobalRoot;
        public int FunctionDepth { get; } = functionDepth;
        public Dictionary<string, NameSymbol> Symbols { get; } = [];

        public bool TryLookupInCurrent(string name, out NameSymbol symbol)
        {
            return Symbols.TryGetValue(name, out symbol);
        }

        public bool TryLookup(string name, out NameSymbol symbol)
        {
            if (Symbols.TryGetValue(name, out symbol))
            {
                return true;
            }

            if (Parent != null)
            {
                return Parent.TryLookup(name, out symbol);
            }

            symbol = default;
            return false;
        }
    }

    private readonly NameResolution _result = new();
    private readonly Stack<FunctionLiteral> _functionStack = [];
    private Scope _scope = null!;

    public NameResolution Resolve(CompilationUnit unit)
    {
        return Resolve(unit, [], []);
    }

    public NameResolution Resolve(
        CompilationUnit unit,
        IReadOnlyList<FunctionDeclaration> externalFunctionDeclarations,
        IReadOnlyList<EnumDeclaration> externalEnumDeclarations)
    {
        _scope = new Scope(parent: null, isGlobalRoot: true, functionDepth: 0);
        PredeclareTopLevelDeclarations(unit, externalFunctionDeclarations, externalEnumDeclarations);

        var seenNamespace = false;
        var seenDeclaration = false;

        foreach (var statement in unit.Statements)
        {
            switch (statement)
            {
                case ImportStatement:
                    if (seenNamespace || seenDeclaration)
                    {
                        _result.Diagnostics.Report(statement.Span,
                            "import statements must appear before namespace and other top-level declarations",
                            "N005");
                    }
                    break;
                case NamespaceStatement namespaceStatement:
                    if (seenDeclaration)
                    {
                        _result.Diagnostics.Report(namespaceStatement.Span,
                            "namespace declaration must appear before top-level declarations",
                            "N007");
                    }

                    if (seenNamespace)
                    {
                        _result.Diagnostics.Report(namespaceStatement.Span,
                            "duplicate namespace declaration",
                            "N008");
                    }

                    seenNamespace = true;
                    break;
                default:
                    seenDeclaration = true;
                    break;
            }

            ResolveStatement(statement);
        }

        if (!seenNamespace)
        {
            _result.Diagnostics.Report(unit.Span,
                "missing required file-scoped namespace declaration",
                "N006");
        }

        return _result;
    }

    private void PredeclareTopLevelDeclarations(
        CompilationUnit unit,
        IReadOnlyList<FunctionDeclaration> externalFunctionDeclarations,
        IReadOnlyList<EnumDeclaration> externalEnumDeclarations)
    {
        foreach (var external in externalFunctionDeclarations)
        {
            DeclareTopLevelFunction(external.Name.Value, external.Name.Span);
        }

        foreach (var externalEnum in externalEnumDeclarations)
        {
            foreach (var variant in externalEnum.Variants)
            {
                DeclareEnumVariant(variant.Name.Value, variant.Name.Span);
            }
        }

        foreach (var statement in unit.Statements)
        {
            if (statement is FunctionDeclaration functionDeclaration)
            {
                DeclareTopLevelFunction(functionDeclaration.Name.Value, functionDeclaration.Name.Span);
                if (_scope.TryLookupInCurrent(functionDeclaration.Name.Value, out var symbol))
                {
                    _result.IdentifierSymbols[functionDeclaration.Name] = symbol;
                }

                continue;
            }

            if (statement is not EnumDeclaration enumDeclaration)
            {
                continue;
            }

            foreach (var variant in enumDeclaration.Variants)
            {
                DeclareEnumVariant(variant.Name.Value, variant.Name.Span);
                if (_scope.TryLookupInCurrent(variant.Name.Value, out var symbol))
                {
                    _result.IdentifierSymbols[variant.Name] = symbol;
                }
            }
        }
    }

    private void DeclareTopLevelFunction(string name, Span declarationSpan)
    {
        if (_scope.TryLookupInCurrent(name, out _))
        {
            _result.Diagnostics.Report(declarationSpan, $"duplicate declaration of '{name}'", "N002");
            return;
        }

        var symbol = new NameSymbol(name, NameSymbolKind.Global, declarationSpan, _scope.FunctionDepth);
        _scope.Symbols[name] = symbol;
        _result.GlobalFunctionNames.Add(name);
    }

    private void DeclareEnumVariant(string name, Span declarationSpan)
    {
        if (_scope.TryLookupInCurrent(name, out _))
        {
            _result.Diagnostics.Report(declarationSpan, $"duplicate declaration of '{name}'", "N002");
            return;
        }

        var symbol = new NameSymbol(name, NameSymbolKind.EnumVariant, declarationSpan, _scope.FunctionDepth);
        _scope.Symbols[name] = symbol;
    }

    private void ResolveStatement(IStatement statement)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                ResolveLetStatement(letStatement);
                break;
            case AssignmentStatement assignmentStatement:
                ResolveAssignmentStatement(assignmentStatement);
                break;
            case IndexAssignmentStatement indexAssignmentStatement:
                ResolveIndexAssignmentStatement(indexAssignmentStatement);
                break;
            case ForInStatement forInStatement:
                ResolveForInStatement(forInStatement);
                break;
            case BreakStatement:
            case ContinueStatement:
                break;
            case FunctionDeclaration functionDeclaration:
                ResolveFunctionDeclaration(functionDeclaration);
                break;
            case EnumDeclaration:
                break;
            case ReturnStatement returnStatement:
                if (returnStatement.ReturnValue != null)
                {
                    ResolveExpression(returnStatement.ReturnValue);
                }
                break;
            case ImportStatement importStatement:
                ResolveImportStatement(importStatement);
                break;
            case NamespaceStatement namespaceStatement:
                ResolveNamespaceStatement(namespaceStatement);
                break;
            case ExpressionStatement expressionStatement:
                if (expressionStatement.Expression != null)
                {
                    ResolveExpression(expressionStatement.Expression);
                }
                break;
            case BlockStatement blockStatement:
                ResolveBlockStatement(blockStatement);
                break;
        }
    }

    private void ResolveImportStatement(ImportStatement statement)
    {
        if (!_scope.IsGlobalRoot)
        {
            _result.Diagnostics.Report(statement.Span,
                "import statements are only allowed at the top level",
                "N003");
            return;
        }

        if (string.IsNullOrWhiteSpace(statement.QualifiedName))
        {
            _result.Diagnostics.Report(statement.Span,
                "import statements must specify a qualified namespace",
                "N010");
            return;
        }

        _result.ImportedNamespaces.Add(statement.QualifiedName);

        var alias = statement.Alias;
        if (_result.ImportedTypeAliases.TryGetValue(alias, out var existingQualifiedName))
        {
            if (existingQualifiedName != statement.QualifiedName)
            {
                _result.Diagnostics.Report(statement.Span,
                    $"import alias '{alias}' conflicts with existing import '{existingQualifiedName}'",
                    "N004");
            }

            return;
        }

        _result.ImportedTypeAliases[alias] = statement.QualifiedName;
    }

    private void ResolveNamespaceStatement(NamespaceStatement statement)
    {
        if (!_scope.IsGlobalRoot)
        {
            _result.Diagnostics.Report(statement.Span,
                "namespace declarations are only allowed at the top level",
                "N009");
            return;
        }

        if (_result.FileNamespace == null)
        {
            _result.FileNamespace = statement.QualifiedName;
        }
    }

    private void ResolveFunctionDeclaration(FunctionDeclaration declaration)
    {
        EnterScope(isGlobalRoot: false, functionDepth: _scope.FunctionDepth + 1);

        foreach (var parameter in declaration.Parameters)
        {
            if (_scope.TryLookupInCurrent(parameter.Name, out _))
            {
                _result.Diagnostics.Report(parameter.Span, $"duplicate declaration of '{parameter.Name}'", "N002");
                continue;
            }

            var symbol = new NameSymbol(parameter.Name, NameSymbolKind.Parameter, parameter.Span, _scope.FunctionDepth);
            _scope.Symbols[parameter.Name] = symbol;
            _result.ParameterSymbols[parameter] = symbol;
        }

        ResolveBlockStatement(declaration.Body);
        LeaveScope();
    }

    private void ResolveLetStatement(LetStatement statement)
    {
        if (_scope.TryLookupInCurrent(statement.Name.Value, out _))
        {
            _result.Diagnostics.Report(statement.Name.Span, $"duplicate declaration of '{statement.Name.Value}'", "N002");
        }
        else
        {
            var kind = _scope.IsGlobalRoot ? NameSymbolKind.Global : NameSymbolKind.Local;
            var symbol = new NameSymbol(statement.Name.Value, kind, statement.Name.Span, _scope.FunctionDepth);
            _scope.Symbols[statement.Name.Value] = symbol;
            _result.IdentifierSymbols[statement.Name] = symbol;
            if (statement.IsMutable)
            {
                _result.MutableSymbols.Add(symbol);
            }
        }

        if (statement.Value != null)
        {
            ResolveExpression(statement.Value);
        }
    }

    private void ResolveAssignmentStatement(AssignmentStatement statement)
    {
        ResolveIdentifier(statement.Name);
        ResolveExpression(statement.Value);
    }

    private void ResolveIndexAssignmentStatement(IndexAssignmentStatement statement)
    {
        ResolveExpression(statement.Target.Left);
        ResolveExpression(statement.Target.Index);
        ResolveExpression(statement.Value);
    }

    private void ResolveForInStatement(ForInStatement statement)
    {
        ResolveExpression(statement.Iterable);

        EnterScope(isGlobalRoot: false, functionDepth: _scope.FunctionDepth);
        var symbol = new NameSymbol(statement.Iterator.Value, NameSymbolKind.Local, statement.Iterator.Span, _scope.FunctionDepth);
        _scope.Symbols[statement.Iterator.Value] = symbol;
        _result.IdentifierSymbols[statement.Iterator] = symbol;

        ResolveBlockStatement(statement.Body);
        LeaveScope();
    }

    private void ResolveBlockStatement(BlockStatement block)
    {
        EnterScope(isGlobalRoot: false, functionDepth: _scope.FunctionDepth);
        foreach (var statement in block.Statements)
        {
            ResolveStatement(statement);
        }
        LeaveScope();
    }

    private void ResolveExpression(IExpression expression)
    {
        switch (expression)
        {
            case Identifier identifier:
                ResolveIdentifier(identifier);
                break;
            case PrefixExpression prefixExpression:
                ResolveExpression(prefixExpression.Right);
                break;
            case InfixExpression infixExpression:
                ResolveExpression(infixExpression.Left);
                ResolveExpression(infixExpression.Right);
                break;
            case IfExpression ifExpression:
                ResolveExpression(ifExpression.Condition);
                ResolveBlockStatement(ifExpression.Consequence);
                if (ifExpression.Alternative != null)
                {
                    ResolveBlockStatement(ifExpression.Alternative);
                }
                break;
            case MatchExpression matchExpression:
                ResolveExpression(matchExpression.Target);
                foreach (var arm in matchExpression.Arms)
                {
                    ResolveMatchArm(arm);
                }
                break;
            case FunctionLiteral functionLiteral:
                ResolveFunctionLiteral(functionLiteral);
                break;
            case CallExpression callExpression:
                ResolveExpression(callExpression.Function);
                foreach (var argument in callExpression.Arguments)
                {
                    ResolveExpression(argument.Expression);
                }
                break;
            case MemberAccessExpression memberAccessExpression:
                ResolveMemberAccessObject(memberAccessExpression.Object);
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ResolveExpression(element);
                }
                break;
            case IndexExpression indexExpression:
                ResolveExpression(indexExpression.Left);
                ResolveExpression(indexExpression.Index);
                break;
            case NewExpression newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    ResolveExpression(argument);
                }
                break;
        }
    }

    private void ResolveMemberAccessObject(IExpression expression)
    {
        switch (expression)
        {
            case Identifier identifier:
                if (_scope.TryLookup(identifier.Value, out _))
                {
                    ResolveIdentifier(identifier);
                }
                break;
            case MemberAccessExpression nested:
                ResolveMemberAccessObject(nested.Object);
                break;
            default:
                ResolveExpression(expression);
                break;
        }
    }

    private void ResolveFunctionLiteral(FunctionLiteral functionLiteral)
    {
        EnterScope(isGlobalRoot: false, functionDepth: _scope.FunctionDepth + 1);
        _functionStack.Push(functionLiteral);

        foreach (var parameter in functionLiteral.Parameters)
        {
            if (_scope.TryLookupInCurrent(parameter.Name, out _))
            {
                _result.Diagnostics.Report(parameter.Span, $"duplicate declaration of '{parameter.Name}'", "N002");
                continue;
            }

            var symbol = new NameSymbol(parameter.Name, NameSymbolKind.Parameter, parameter.Span, _scope.FunctionDepth);
            _scope.Symbols[parameter.Name] = symbol;
            _result.ParameterSymbols[parameter] = symbol;
        }

        ResolveBlockStatement(functionLiteral.Body);

        _functionStack.Pop();
        LeaveScope();
    }

    private void ResolveMatchArm(MatchArm arm)
    {
        EnterScope(isGlobalRoot: false, functionDepth: _scope.FunctionDepth);
        foreach (var binding in arm.Bindings)
        {
            if (_scope.TryLookupInCurrent(binding.Value, out _))
            {
                _result.Diagnostics.Report(binding.Span, $"duplicate declaration of '{binding.Value}'", "N002");
                continue;
            }

            var symbol = new NameSymbol(binding.Value, NameSymbolKind.Local, binding.Span, _scope.FunctionDepth);
            _scope.Symbols[binding.Value] = symbol;
            _result.IdentifierSymbols[binding] = symbol;
        }

        ResolveBlockStatement(arm.Body);
        LeaveScope();
    }

    private void ResolveIdentifier(Identifier identifier)
    {
        if (!_scope.TryLookup(identifier.Value, out var symbol))
        {
            _result.Diagnostics.Report(identifier.Span, $"undefined variable '{identifier.Value}'", "N001");
            return;
        }

        _result.IdentifierSymbols[identifier] = symbol;

        if (_functionStack.Count == 0)
        {
            return;
        }

        if (symbol.Kind is NameSymbolKind.Global)
        {
            return;
        }

        if (symbol.FunctionDepth >= _scope.FunctionDepth)
        {
            return;
        }

        var currentFunction = _functionStack.Peek();
        if (!_result.FunctionCaptures.TryGetValue(currentFunction, out var captures))
        {
            captures = [];
            _result.FunctionCaptures[currentFunction] = captures;
        }

        if (!captures.Contains(symbol))
        {
            captures.Add(symbol);
        }
    }

    private void EnterScope(bool isGlobalRoot, int functionDepth)
    {
        _scope = new Scope(_scope, isGlobalRoot, functionDepth);
    }

    private void LeaveScope()
    {
        _scope = _scope.Parent!;
    }
}
