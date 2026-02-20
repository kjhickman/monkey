using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public enum NameSymbolKind
{
    Global,
    Local,
    Parameter,
    Builtin,
}

public readonly record struct NameSymbol(string Name, NameSymbolKind Kind, Span DeclarationSpan, int FunctionDepth);

public class NameResolution
{
    public Dictionary<Identifier, NameSymbol> IdentifierSymbols { get; } = [];
    public Dictionary<FunctionParameter, NameSymbol> ParameterSymbols { get; } = [];
    public Dictionary<FunctionLiteral, List<NameSymbol>> FunctionCaptures { get; } = [];
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
        _scope = new Scope(parent: null, isGlobalRoot: true, functionDepth: 0);
        PredeclareBuiltins();
        PredeclareTopLevelFunctions(unit);

        foreach (var statement in unit.Statements)
        {
            ResolveStatement(statement);
        }

        return _result;
    }

    private void PredeclareTopLevelFunctions(CompilationUnit unit)
    {
        foreach (var statement in unit.Statements)
        {
            if (statement is not FunctionDeclaration functionDeclaration)
            {
                continue;
            }

            if (_scope.TryLookupInCurrent(functionDeclaration.Name.Value, out _))
            {
                _result.Diagnostics.Report(functionDeclaration.Name.Span, $"duplicate declaration of '{functionDeclaration.Name.Value}'", "N002");
                continue;
            }

            var symbol = new NameSymbol(functionDeclaration.Name.Value, NameSymbolKind.Global, functionDeclaration.Name.Span, _scope.FunctionDepth);
            _scope.Symbols[functionDeclaration.Name.Value] = symbol;
            _result.IdentifierSymbols[functionDeclaration.Name] = symbol;
        }
    }

    private void PredeclareBuiltins()
    {
        foreach (var name in BuiltinRegistry.Default.GetAllPublicNames())
        {
            var symbol = new NameSymbol(name, NameSymbolKind.Builtin, Span.Empty, FunctionDepth: 0);
            _scope.Symbols[name] = symbol;
        }
    }

    private void ResolveStatement(IStatement statement)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                ResolveLetStatement(letStatement);
                break;
            case FunctionDeclaration functionDeclaration:
                ResolveFunctionDeclaration(functionDeclaration);
                break;
            case ReturnStatement returnStatement:
                if (returnStatement.ReturnValue != null)
                {
                    ResolveExpression(returnStatement.ReturnValue);
                }
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
        }

        if (statement.Value != null)
        {
            ResolveExpression(statement.Value);
        }
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
            case FunctionLiteral functionLiteral:
                ResolveFunctionLiteral(functionLiteral);
                break;
            case CallExpression callExpression:
                ResolveExpression(callExpression.Function);
                foreach (var argument in callExpression.Arguments)
                {
                    ResolveExpression(argument);
                }
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

        if (symbol.Kind is NameSymbolKind.Global or NameSymbolKind.Builtin)
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
