using Kong.Parsing;
using Kong.Semantics.Symbols;

namespace Kong.Semantics.Binding;

public sealed class BoundProgram
{
    public BoundProgram(Program syntax, IReadOnlyList<BoundStatement> statements, IReadOnlyDictionary<FunctionLiteral, BoundFunctionExpression> functions)
    {
        Syntax = syntax;
        Statements = statements;
        Functions = functions;
    }

    public Program Syntax { get; }

    public IReadOnlyList<BoundStatement> Statements { get; }

    public IReadOnlyDictionary<FunctionLiteral, BoundFunctionExpression> Functions { get; }

    public SemanticModel? TypeInfo { get; private set; }

    public void SetTypeInfo(SemanticModel typeInfo)
    {
        TypeInfo = typeInfo;
    }

    public IReadOnlyList<string> GetClosureCaptureNames(FunctionLiteral functionLiteral)
    {
        if (!Functions.TryGetValue(functionLiteral, out var boundFunction))
        {
            return [];
        }

        return boundFunction.Captures.Select(c => c.Name).ToList();
    }
}

public abstract class BoundNode(INode syntax)
{
    public INode Syntax { get; } = syntax;
}

public abstract class BoundStatement(IStatement syntax) : BoundNode(syntax)
{
}

public abstract class BoundExpression(IExpression syntax) : BoundNode(syntax)
{
}

public sealed class BoundExpressionStatement(ExpressionStatement syntax, BoundExpression expression) : BoundStatement(syntax)
{
    public BoundExpression Expression { get; } = expression;
}

public sealed class BoundLetStatement(LetStatement syntax, VariableSymbol? variable, BoundExpression value) : BoundStatement(syntax)
{
    public VariableSymbol? Variable { get; } = variable;

    public BoundExpression Value { get; } = value;
}

public sealed class BoundAssignStatement(AssignStatement syntax, VariableSymbol variable, BoundExpression value) : BoundStatement(syntax)
{
    public VariableSymbol Variable { get; } = variable;

    public BoundExpression Value { get; } = value;
}

public sealed class BoundReturnStatement(ReturnStatement syntax, BoundExpression value) : BoundStatement(syntax)
{
    public BoundExpression Value { get; } = value;
}

public sealed class BoundBlockStatement(BlockStatement syntax, IReadOnlyList<BoundStatement> statements) : BoundStatement(syntax)
{
    public IReadOnlyList<BoundStatement> Statements { get; } = statements;
}

public sealed class BoundIdentifierExpression(Identifier syntax, Symbol? Symbol) : BoundExpression(syntax)
{
    public Symbol? Symbol { get; } = Symbol;
}

public sealed class BoundIntegerLiteralExpression(IntegerLiteral syntax) : BoundExpression(syntax)
{
}

public sealed class BoundBooleanLiteralExpression(BooleanLiteral syntax) : BoundExpression(syntax)
{
}

public sealed class BoundCharLiteralExpression(CharLiteral syntax) : BoundExpression(syntax)
{
}

public sealed class BoundDoubleLiteralExpression(DoubleLiteral syntax) : BoundExpression(syntax)
{
}

public sealed class BoundStringLiteralExpression(StringLiteral syntax) : BoundExpression(syntax)
{
}

public sealed class BoundArrayLiteralExpression(ArrayLiteral syntax, IReadOnlyList<BoundExpression> elements) : BoundExpression(syntax)
{
    public IReadOnlyList<BoundExpression> Elements { get; } = elements;
}

public sealed class BoundHashLiteralExpression(HashLiteral syntax, IReadOnlyList<(BoundExpression Key, BoundExpression Value)> pairs) : BoundExpression(syntax)
{
    public IReadOnlyList<(BoundExpression Key, BoundExpression Value)> Pairs { get; } = pairs;
}

public sealed class BoundPrefixExpression(PrefixExpression syntax, BoundExpression right) : BoundExpression(syntax)
{
    public BoundExpression Right { get; } = right;
}

public sealed class BoundInfixExpression(InfixExpression syntax, BoundExpression left, BoundExpression right) : BoundExpression(syntax)
{
    public BoundExpression Left { get; } = left;

    public BoundExpression Right { get; } = right;
}

public sealed class BoundIfExpression(IfExpression syntax, BoundExpression condition, BoundBlockStatement consequence, BoundBlockStatement? alternative) : BoundExpression(syntax)
{
    public BoundExpression Condition { get; } = condition;

    public BoundBlockStatement Consequence { get; } = consequence;

    public BoundBlockStatement? Alternative { get; } = alternative;
}

public sealed class BoundIndexExpression(IndexExpression syntax, BoundExpression left, BoundExpression index) : BoundExpression(syntax)
{
    public BoundExpression Left { get; } = left;

    public BoundExpression Index { get; } = index;
}

public sealed class BoundFunctionExpression(FunctionLiteral syntax, FunctionSymbol Symbol, IReadOnlyList<BoundCapture> captures, BoundBlockStatement body) : BoundExpression(syntax)
{
    public FunctionSymbol Symbol { get; } = Symbol;

    public IReadOnlyList<BoundCapture> Captures { get; } = captures;

    public BoundBlockStatement Body { get; } = body;
}

public sealed class BoundCallExpression(CallExpression syntax, BoundExpression function, IReadOnlyList<BoundExpression> arguments) : BoundExpression(syntax)
{
    public BoundExpression Function { get; } = function;

    public IReadOnlyList<BoundExpression> Arguments { get; } = arguments;
}

public sealed class BoundCapture(Symbol Symbol)
{
    public Symbol Symbol { get; } = Symbol;

    public string Name => Symbol.Name;
}
