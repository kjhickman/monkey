using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantics.Symbols;

namespace Kong.Semantics.Binding;

public sealed class Binder
{
    private static readonly HashSet<string> Builtins = ["puts", "len", "push"];

    private readonly List<string> _errors = [];
    private readonly Dictionary<FunctionLiteral, BoundFunctionExpression> _functions = [];
    private readonly HashSet<FunctionSymbol> _topLevelFunctions = [];

    public BindingResult Bind(Program program)
    {
        _errors.Clear();
        _functions.Clear();
        _topLevelFunctions.Clear();

        var globalScope = new SymbolScope();
        DeclareTopLevelFunctions(program, globalScope);

        var statements = new List<BoundStatement>(program.Statements.Count);
        foreach (var statement in program.Statements)
        {
            statements.Add(BindStatement(statement, globalScope, null));
        }

        var boundProgram = new BoundProgram(program, statements, new Dictionary<FunctionLiteral, BoundFunctionExpression>(_functions));
        return new BindingResult(boundProgram, _errors.ToList());
    }

    private void DeclareTopLevelFunctions(Program program, SymbolScope globalScope)
    {
        foreach (var statement in program.Statements)
        {
            if (statement is not LetStatement { Value: FunctionLiteral functionLiteral } letStatement)
            {
                continue;
            }

            if (globalScope.TryLookupFunction(letStatement.Name.Value, out _))
            {
                _errors.Add($"duplicate top-level function definition: {letStatement.Name.Value}");
                continue;
            }

            var parameterTypes = ParseParameterTypes(functionLiteral, reportErrors: true);
            var parameters = CreateParameterSymbols(functionLiteral, parameterTypes);
            var symbol = new FunctionSymbol(letStatement.Name.Value, parameters, TypeSymbol.Unknown);
            globalScope.Define(symbol);
            _topLevelFunctions.Add(symbol);
        }
    }

    private List<VariableSymbol> CreateParameterSymbols(FunctionLiteral functionLiteral, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        var symbols = new List<VariableSymbol>(functionLiteral.Parameters.Count);
        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            symbols.Add(new VariableSymbol(functionLiteral.Parameters[i].Name.Value, parameterTypes[i]));
        }

        return symbols;
    }

    private List<TypeSymbol> ParseParameterTypes(FunctionLiteral functionLiteral, bool reportErrors)
    {
        var parameterTypes = new List<TypeSymbol>(functionLiteral.Parameters.Count);
        foreach (var parameter in functionLiteral.Parameters)
        {
            if (parameter.TypeAnnotation is null)
            {
                if (reportErrors)
                {
                    _errors.Add($"missing type annotation for parameter '{parameter.Name.Value}'");
                }

                parameterTypes.Add(TypeSymbol.Unknown);
                continue;
            }

            var type = ParseTypeAnnotation(parameter.TypeAnnotation, out var error);
            if (error is not null && reportErrors)
            {
                _errors.Add($"invalid type annotation for parameter '{parameter.Name.Value}': {error}");
            }

            parameterTypes.Add(type);
        }

        return parameterTypes;
    }

    private static TypeSymbol ParseTypeAnnotation(ITypeExpression annotation, out string? error)
    {
        switch (annotation)
        {
            case NamedTypeExpression named:
                return named.Name switch
                {
                    "int" => SetNoError(TypeSymbol.Int, out error),
                    "bool" => SetNoError(TypeSymbol.Bool, out error),
                    "string" => SetNoError(TypeSymbol.String, out error),
                    _ => SetError(TypeSymbol.Unknown, $"unknown type '{named.Name}'", out error),
                };
            case ArrayTypeExpression arrayType:
            {
                var elementType = ParseTypeAnnotation(arrayType.ElementType, out error);
                if (error is not null)
                {
                    return TypeSymbol.Unknown;
                }

                return SetNoError(TypeSymbol.ArrayOf(elementType), out error);
            }
            case MapTypeExpression mapType:
            {
                var keyType = ParseTypeAnnotation(mapType.KeyType, out error);
                if (error is not null)
                {
                    return TypeSymbol.Unknown;
                }

                var valueType = ParseTypeAnnotation(mapType.ValueType, out error);
                if (error is not null)
                {
                    return TypeSymbol.Unknown;
                }

                return SetNoError(TypeSymbol.MapOf(keyType, valueType), out error);
            }
            default:
                return SetError(TypeSymbol.Unknown, $"unsupported type annotation '{annotation.GetType().Name}'", out error);
        }
    }

    private static TypeSymbol SetNoError(TypeSymbol type, out string? error)
    {
        error = null;
        return type;
    }

    private static TypeSymbol SetError(TypeSymbol type, string message, out string? error)
    {
        error = message;
        return type;
    }

    private BoundStatement BindStatement(IStatement statement, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        return statement switch
        {
            ExpressionStatement expressionStatement when expressionStatement.Expression is not null
                => new BoundExpressionStatement(expressionStatement, BindExpression(expressionStatement.Expression, scope, functionContext)),
            LetStatement letStatement when letStatement.Value is not null
                => BindLetStatement(letStatement, scope, functionContext),
            ReturnStatement returnStatement when returnStatement.ReturnValue is not null
                => new BoundReturnStatement(returnStatement, BindExpression(returnStatement.ReturnValue, scope, functionContext)),
            BlockStatement blockStatement
                => BindBlockStatement(blockStatement, new SymbolScope(scope), functionContext),
            _ => new BoundExpressionStatement(
                new ExpressionStatement { Token = new Token(TokenType.Illegal, "") },
                new BoundIdentifierExpression(new Identifier { Token = new Token(TokenType.Illegal, ""), Value = "<unsupported>" }, null)),
        };
    }

    private BoundBlockStatement BindBlockStatement(BlockStatement blockStatement, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        var statements = new List<BoundStatement>(blockStatement.Statements.Count);
        foreach (var statement in blockStatement.Statements)
        {
            statements.Add(BindStatement(statement, scope, functionContext));
        }

        return new BoundBlockStatement(blockStatement, statements);
    }

    private BoundLetStatement BindLetStatement(LetStatement letStatement, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        if (letStatement.Value is FunctionLiteral functionLiteral)
        {
            FunctionSymbol functionSymbol;
            if (scope.Parent is null && scope.TryLookupFunction(letStatement.Name.Value, out var existing) && existing is not null)
            {
                functionSymbol = existing;
            }
            else
            {
                var parameterTypes = ParseParameterTypes(functionLiteral, reportErrors: true);
                var parameters = CreateParameterSymbols(functionLiteral, parameterTypes);
                functionSymbol = new FunctionSymbol(letStatement.Name.Value, parameters, TypeSymbol.Unknown);
                scope.Define(functionSymbol);
            }

            var boundFunction = BindFunctionLiteral(functionLiteral, functionSymbol, scope);
            return new BoundLetStatement(letStatement, null, boundFunction);
        }

        var value = BindExpression(letStatement.Value!, scope, functionContext);
        var symbol = new VariableSymbol(letStatement.Name.Value, TypeSymbol.Unknown);
        scope.Define(symbol);
        return new BoundLetStatement(letStatement, symbol, value);
    }

    private BoundExpression BindExpression(IExpression expression, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        return expression switch
        {
            Identifier identifier => BindIdentifier(identifier, scope, functionContext),
            IntegerLiteral integerLiteral => new BoundIntegerLiteralExpression(integerLiteral),
            BooleanLiteral booleanLiteral => new BoundBooleanLiteralExpression(booleanLiteral),
            StringLiteral stringLiteral => new BoundStringLiteralExpression(stringLiteral),
            ArrayLiteral arrayLiteral => new BoundArrayLiteralExpression(arrayLiteral, arrayLiteral.Elements.Select(e => BindExpression(e, scope, functionContext)).ToList()),
            HashLiteral hashLiteral => new BoundHashLiteralExpression(
                hashLiteral,
                hashLiteral.Pairs
                    .Select(p => (BindExpression(p.Key, scope, functionContext), BindExpression(p.Value, scope, functionContext)))
                    .ToList()),
            PrefixExpression prefixExpression => new BoundPrefixExpression(prefixExpression, BindExpression(prefixExpression.Right, scope, functionContext)),
            InfixExpression infixExpression => new BoundInfixExpression(
                infixExpression,
                BindExpression(infixExpression.Left, scope, functionContext),
                BindExpression(infixExpression.Right, scope, functionContext)),
            IfExpression ifExpression => BindIfExpression(ifExpression, scope, functionContext),
            IndexExpression indexExpression => new BoundIndexExpression(
                indexExpression,
                BindExpression(indexExpression.Left, scope, functionContext),
                BindExpression(indexExpression.Index, scope, functionContext)),
            FunctionLiteral functionLiteral => BindFunctionLiteral(functionLiteral, null, scope),
            CallExpression callExpression => new BoundCallExpression(
                callExpression,
                BindExpression(callExpression.Function, scope, functionContext),
                callExpression.Arguments.Select(a => BindExpression(a, scope, functionContext)).ToList()),
            _ => new BoundIdentifierExpression(new Identifier { Token = new Token(TokenType.Illegal, ""), Value = "<unsupported>" }, null),
        };
    }

    private BoundExpression BindIfExpression(IfExpression ifExpression, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        var condition = BindExpression(ifExpression.Condition, scope, functionContext);
        var consequence = BindBlockStatement(ifExpression.Consequence, new SymbolScope(scope), functionContext);
        BoundBlockStatement? alternative = null;
        if (ifExpression.Alternative is not null)
        {
            alternative = BindBlockStatement(ifExpression.Alternative, new SymbolScope(scope), functionContext);
        }

        return new BoundIfExpression(ifExpression, condition, consequence, alternative);
    }

    private BoundExpression BindIdentifier(Identifier identifier, SymbolScope scope, FunctionBindingContext? functionContext)
    {
        if (!scope.TryLookup(identifier.Value, out var symbol) || symbol is null)
        {
            if (Builtins.Contains(identifier.Value))
            {
                return new BoundIdentifierExpression(identifier, null);
            }

            _errors.Add($"Undefined variable: {identifier.Value}");
            return new BoundIdentifierExpression(identifier, null);
        }

        var isTopLevelFunction = symbol is FunctionSymbol functionSymbol
            && _topLevelFunctions.Contains(functionSymbol);

        if (functionContext is not null
            && symbol.DeclaringScope is not null
            && !symbol.DeclaringScope.IsWithin(functionContext.FunctionRootScope)
            && !isTopLevelFunction)
        {
            functionContext.AddCapture(symbol);
        }

        return new BoundIdentifierExpression(identifier, symbol);
    }

    private BoundFunctionExpression BindFunctionLiteral(FunctionLiteral functionLiteral, FunctionSymbol? functionSymbol, SymbolScope parentScope)
    {
        var resolvedFunctionSymbol = functionSymbol ?? CreateAnonymousFunctionSymbol(functionLiteral);

        var functionScope = new SymbolScope(parentScope);
        if (!string.IsNullOrEmpty(functionLiteral.Name))
        {
            functionScope.Define(resolvedFunctionSymbol);
        }

        foreach (var parameter in resolvedFunctionSymbol.Parameters)
        {
            functionScope.Define(parameter);
        }

        var functionContext = new FunctionBindingContext(functionScope);
        var body = BindBlockStatement(functionLiteral.Body, functionScope, functionContext);

        var boundFunction = new BoundFunctionExpression(functionLiteral, resolvedFunctionSymbol, functionContext.Captures, body);
        _functions[functionLiteral] = boundFunction;
        return boundFunction;
    }

    private FunctionSymbol CreateAnonymousFunctionSymbol(FunctionLiteral functionLiteral)
    {
        var parameterTypes = ParseParameterTypes(functionLiteral, reportErrors: true);
        var parameters = CreateParameterSymbols(functionLiteral, parameterTypes);
        var name = string.IsNullOrEmpty(functionLiteral.Name) ? "<anonymous>" : functionLiteral.Name;
        return new FunctionSymbol(name, parameters, TypeSymbol.Unknown);
    }

    private sealed class FunctionBindingContext(SymbolScope functionRootScope)
    {
        private readonly HashSet<string> _captureNames = [];
        private readonly List<BoundCapture> _captures = [];

        public SymbolScope FunctionRootScope { get; } = functionRootScope;

        public IReadOnlyList<BoundCapture> Captures => _captures;

        public void AddCapture(Symbol symbol)
        {
            if (!_captureNames.Add(symbol.Name))
            {
                return;
            }

            _captures.Add(new BoundCapture(symbol));
        }
    }
}

public sealed record BindingResult(BoundProgram BoundProgram, IReadOnlyList<string> Errors);
