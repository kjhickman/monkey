using Kong.Diagnostics;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantics;
using Kong.Semantics.Binding;

namespace Kong.Lowering;

public sealed class CanonicalLowerer
{
    private int _nextTempId;
    private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);

    public LoweringResult Lower(Program program, BoundProgram boundProgram)
    {
        var diagnostics = new DiagnosticBag();
        var model = boundProgram.TypeInfo;
        if (model is null)
        {
            diagnostics.Add(CompilationStage.Lowering, "missing semantic model for lowering");
            return new LoweringResult(program, boundProgram, diagnostics);
        }

        _nextTempId = 0;
        _reservedNames.Clear();
        ReserveNames(program);
        program.Statements = LowerStatements(program.Statements, model, blockProducesValue: false);

        var loweredBoundProgram = new BoundProgram(program, boundProgram.Statements, boundProgram.Functions);
        loweredBoundProgram.SetTypeInfo(model);
        return new LoweringResult(program, loweredBoundProgram, diagnostics);
    }

    private List<IStatement> LowerStatements(IReadOnlyList<IStatement> statements, SemanticModel model, bool blockProducesValue)
    {
        var lowered = new List<IStatement>();
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var preserveExpressionIf = blockProducesValue && i == statements.Count - 1;
            lowered.AddRange(LowerStatement(statement, model, preserveExpressionIf));
        }

        return lowered;
    }

    private IEnumerable<IStatement> LowerStatement(IStatement statement, SemanticModel model, bool preserveExpressionIf)
    {
        switch (statement)
        {
            case LetStatement { Value: not null } letStatement:
            {
                var setup = new List<IStatement>();
                var loweredValue = LowerExpression(letStatement.Value, model, setup);
                letStatement.Value = loweredValue;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return letStatement;
                yield break;
            }
            case ReturnStatement { ReturnValue: not null } returnStatement:
            {
                var setup = new List<IStatement>();
                var loweredValue = LowerExpression(returnStatement.ReturnValue, model, setup);
                returnStatement.ReturnValue = loweredValue;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return returnStatement;
                yield break;
            }
            case ExpressionStatement { Expression: IfExpression ifExpression } expressionStatement:
            {
                if (preserveExpressionIf)
                {
                    var setup = new List<IStatement>();
                    var loweredExpression = LowerExpression(ifExpression, model, setup);
                    expressionStatement.Expression = loweredExpression;
                    foreach (var prep in setup)
                    {
                        yield return prep;
                    }

                    yield return expressionStatement;
                    yield break;
                }

                foreach (var loweredIfStatement in LowerExpressionIfStatement(ifExpression, model))
                {
                    yield return loweredIfStatement;
                }

                _ = expressionStatement;
                yield break;
            }
            case ExpressionStatement { Expression: not null } expressionStatement:
            {
                var setup = new List<IStatement>();
                var loweredExpression = LowerExpression(expressionStatement.Expression, model, setup);
                expressionStatement.Expression = loweredExpression;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return expressionStatement;
                yield break;
            }
            case BlockStatement blockStatement:
                blockStatement.Statements = LowerStatements(blockStatement.Statements, model, blockProducesValue: false);
                yield return blockStatement;
                yield break;
            default:
                yield return statement;
                yield break;
        }
    }

    private IEnumerable<IStatement> LowerExpressionIfStatement(IfExpression ifExpression, SemanticModel model)
    {
        var setup = new List<IStatement>();
        var loweredCondition = LowerExpression(ifExpression.Condition, model, setup);
        foreach (var prep in setup)
        {
            yield return prep;
        }

        ifExpression.Condition = loweredCondition;
        ifExpression.Consequence.Statements = LowerStatements(ifExpression.Consequence.Statements, model, blockProducesValue: false);
        var alternative = ifExpression.Alternative ?? new BlockStatement { Token = new Token(TokenType.LBrace, "{"), Statements = [] };
        alternative.Statements = LowerStatements(alternative.Statements, model, blockProducesValue: false);

        yield return new IfStatement
        {
            Token = ifExpression.Token,
            Condition = ifExpression.Condition,
            Consequence = ifExpression.Consequence,
            Alternative = alternative,
        };
    }

    private IExpression LowerExpression(IExpression expression, SemanticModel model, List<IStatement> setup)
    {
        switch (expression)
        {
            case CallExpression callExpression:
                return LowerCallExpression(callExpression, model, setup);
            case InfixExpression infixExpression:
                infixExpression.Left = LowerExpression(infixExpression.Left, model, setup);
                infixExpression.Right = LowerExpression(infixExpression.Right, model, setup);
                return infixExpression;
            case PrefixExpression prefixExpression:
                prefixExpression.Right = LowerExpression(prefixExpression.Right, model, setup);
                return prefixExpression;
            case IndexExpression indexExpression:
                indexExpression.Left = LowerExpression(indexExpression.Left, model, setup);
                indexExpression.Index = LowerExpression(indexExpression.Index, model, setup);
                if (RequiresTemporary(indexExpression.Left))
                {
                    indexExpression.Left = CreateTemporary(indexExpression.Left, model, setup);
                }

                if (RequiresTemporary(indexExpression.Index))
                {
                    indexExpression.Index = CreateTemporary(indexExpression.Index, model, setup);
                }

                return indexExpression;
            case IfExpression ifExpression:
                ifExpression.Condition = LowerExpression(ifExpression.Condition, model, setup);
                ifExpression.Consequence.Statements = LowerStatements(ifExpression.Consequence.Statements, model, blockProducesValue: true);
                if (ifExpression.Alternative is not null)
                {
                    ifExpression.Alternative.Statements = LowerStatements(ifExpression.Alternative.Statements, model, blockProducesValue: true);
                }

                return ifExpression;
            case ArrayLiteral arrayLiteral:
                for (var i = 0; i < arrayLiteral.Elements.Count; i++)
                {
                    var loweredElement = LowerExpression(arrayLiteral.Elements[i], model, setup);
                    if (RequiresTemporary(loweredElement))
                    {
                        loweredElement = CreateTemporary(loweredElement, model, setup);
                    }

                    arrayLiteral.Elements[i] = loweredElement;
                }

                return arrayLiteral;
            case HashLiteral hashLiteral:
                for (var i = 0; i < hashLiteral.Pairs.Count; i++)
                {
                    var pair = hashLiteral.Pairs[i];
                    var loweredKey = LowerExpression(pair.Key, model, setup);
                    var loweredValue = LowerExpression(pair.Value, model, setup);

                    if (RequiresTemporary(loweredKey))
                    {
                        loweredKey = CreateTemporary(loweredKey, model, setup);
                    }

                    if (RequiresTemporary(loweredValue))
                    {
                        loweredValue = CreateTemporary(loweredValue, model, setup);
                    }

                    hashLiteral.Pairs[i] = new KeyValuePair<IExpression, IExpression>(loweredKey, loweredValue);
                }

                return hashLiteral;
            case FunctionLiteral functionLiteral:
                functionLiteral.Body.Statements = LowerStatements(functionLiteral.Body.Statements, model, blockProducesValue: true);
                return functionLiteral;
            default:
                return expression;
        }
    }

    private IExpression LowerCallExpression(CallExpression callExpression, SemanticModel model, List<IStatement> setup)
    {
        callExpression.Function = LowerExpression(callExpression.Function, model, setup);
        if (RequiresTemporary(callExpression.Function))
        {
            callExpression.Function = CreateTemporary(callExpression.Function, model, setup);
        }

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            var loweredArgument = LowerExpression(callExpression.Arguments[i], model, setup);
            if (RequiresTemporary(loweredArgument))
            {
                loweredArgument = CreateTemporary(loweredArgument, model, setup);
            }

            callExpression.Arguments[i] = loweredArgument;
        }

        if (callExpression.Function is Identifier identifier && IsBuiltin(identifier.Value))
        {
            var intrinsic = new IntrinsicCallExpression
            {
                Token = identifier.Token,
                Name = identifier.Value,
                Arguments = callExpression.Arguments,
            };
            model.AddNodeType(intrinsic, model.GetNodeTypeSymbol(callExpression));
            return intrinsic;
        }

        return callExpression;
    }

    private Identifier CreateTemporary(IExpression valueExpression, SemanticModel model, List<IStatement> setup)
    {
        var tempName = NextUniqueTemporaryName();
        var tempIdentifier = new Identifier
        {
            Token = new Token(TokenType.Ident, tempName),
            Value = tempName,
        };
        var tempLet = new LetStatement
        {
            Token = new Token(TokenType.Let, "let"),
            Name = new Identifier
            {
                Token = new Token(TokenType.Ident, tempName),
                Value = tempName,
            },
            Value = valueExpression,
        };

        var valueType = model.GetNodeTypeSymbol(valueExpression);
        model.AddNodeType(tempIdentifier, valueType);
        model.AddNodeType(tempLet.Name, valueType);
        model.AddNodeType(tempLet, valueType);

        setup.Add(tempLet);
        return tempIdentifier;
    }

    private string NextUniqueTemporaryName()
    {
        string name;
        do
        {
            name = $"__lowered_tmp_{_nextTempId++}";
        }
        while (!_reservedNames.Add(name));

        return name;
    }

    private void ReserveNames(Program program)
    {
        foreach (var statement in program.Statements)
        {
            ReserveNames(statement);
        }
    }

    private void ReserveNames(IStatement statement)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                _reservedNames.Add(letStatement.Name.Value);
                if (letStatement.Value is not null)
                {
                    ReserveNames(letStatement.Value);
                }

                break;
            case AssignStatement assignStatement:
                _reservedNames.Add(assignStatement.Name.Value);
                ReserveNames(assignStatement.Value);
                break;
            case ReturnStatement { ReturnValue: not null } returnStatement:
                ReserveNames(returnStatement.ReturnValue);
                break;
            case ExpressionStatement { Expression: not null } expressionStatement:
                ReserveNames(expressionStatement.Expression);
                break;
            case IfStatement ifStatement:
                ReserveNames(ifStatement.Condition);
                foreach (var inner in ifStatement.Consequence.Statements)
                {
                    ReserveNames(inner);
                }

                foreach (var inner in ifStatement.Alternative.Statements)
                {
                    ReserveNames(inner);
                }

                break;
            case BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    ReserveNames(inner);
                }

                break;
        }
    }

    private void ReserveNames(IExpression expression)
    {
        switch (expression)
        {
            case Identifier identifier:
                _reservedNames.Add(identifier.Value);
                break;
            case PrefixExpression prefixExpression:
                ReserveNames(prefixExpression.Right);
                break;
            case InfixExpression infixExpression:
                ReserveNames(infixExpression.Left);
                ReserveNames(infixExpression.Right);
                break;
            case IfExpression ifExpression:
                ReserveNames(ifExpression.Condition);
                foreach (var inner in ifExpression.Consequence.Statements)
                {
                    ReserveNames(inner);
                }

                if (ifExpression.Alternative is not null)
                {
                    foreach (var inner in ifExpression.Alternative.Statements)
                    {
                        ReserveNames(inner);
                    }
                }

                break;
            case FunctionLiteral functionLiteral:
                foreach (var parameter in functionLiteral.Parameters)
                {
                    _reservedNames.Add(parameter.Name.Value);
                }

                foreach (var inner in functionLiteral.Body.Statements)
                {
                    ReserveNames(inner);
                }

                break;
            case CallExpression callExpression:
                ReserveNames(callExpression.Function);
                foreach (var argument in callExpression.Arguments)
                {
                    ReserveNames(argument);
                }

                break;
            case IntrinsicCallExpression intrinsicCall:
                foreach (var argument in intrinsicCall.Arguments)
                {
                    ReserveNames(argument);
                }

                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ReserveNames(element);
                }

                break;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    ReserveNames(pair.Key);
                    ReserveNames(pair.Value);
                }

                break;
            case IndexExpression indexExpression:
                ReserveNames(indexExpression.Left);
                ReserveNames(indexExpression.Index);
                break;
        }
    }

    private static bool RequiresTemporary(IExpression expression)
    {
        return expression switch
        {
            Identifier => false,
            IntegerLiteral => false,
            BooleanLiteral => false,
            StringLiteral => false,
            FunctionLiteral => false,
            _ => true,
        };
    }

    private static bool IsBuiltin(string name)
    {
        return name is "puts" or "len" or "push";
    }
}
