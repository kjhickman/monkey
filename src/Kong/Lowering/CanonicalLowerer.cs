using Kong.Diagnostics;
using Kong.Lexing;
using Kong.Semantics;
using Kong.Semantics.Binding;
using P = Kong.Parsing;

namespace Kong.Lowering;

public sealed class CanonicalLowerer
{
    private int _nextTempId;
    private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);

    public LoweringResult Lower(P.Program program, BoundProgram boundProgram)
    {
        var diagnostics = new DiagnosticBag();
        var model = boundProgram.TypeInfo;
        if (model is null)
        {
            diagnostics.Add(CompilationStage.Lowering, "missing semantic model for lowering");
            return new LoweringResult(new Program(), boundProgram, diagnostics);
        }

        _nextTempId = 0;
        _reservedNames.Clear();
        ReserveNames(program);
        program.Statements = LowerStatements(program.Statements, model, blockProducesValue: false);

        var loweredProgram = ConvertProgram(program, model, boundProgram);
        return new LoweringResult(loweredProgram, boundProgram, diagnostics);
    }

    private static Program ConvertProgram(P.Program program, SemanticModel model, BoundProgram boundProgram)
    {
        var lowered = new Program();
        lowered.Statements = program.Statements.Select(statement => ConvertStatement(statement, model, boundProgram)).ToList();
        foreach (var signature in model.GetFunctionSignatures())
        {
            lowered.FunctionSignatures[signature.Name] = new FunctionSignature(
                signature.Name,
                signature.ParameterTypes.Select(t => t.ToKongType()).ToList(),
                signature.ReturnType.ToKongType());
        }

        return lowered;
    }

    private static IStatement ConvertStatement(P.IStatement statement, SemanticModel model, BoundProgram boundProgram)
    {
        return statement switch
        {
            P.LetStatement { Value: not null } letStatement => new LetStatement
            {
                Token = letStatement.Token,
                Name = new Identifier { Token = letStatement.Name.Token, Value = letStatement.Name.Value, Type = model.GetNodeType(letStatement.Name) },
                Value = ConvertExpression(letStatement.Value, model, boundProgram),
            },
            P.AssignStatement assignStatement => new AssignStatement
            {
                Token = assignStatement.Token,
                Name = new Identifier { Token = assignStatement.Name.Token, Value = assignStatement.Name.Value, Type = model.GetNodeType(assignStatement.Name) },
                Value = ConvertExpression(assignStatement.Value, model, boundProgram),
            },
            P.ReturnStatement { ReturnValue: not null } returnStatement => new ReturnStatement
            {
                Token = returnStatement.Token,
                ReturnValue = ConvertExpression(returnStatement.ReturnValue, model, boundProgram),
            },
            P.ExpressionStatement { Expression: not null } expressionStatement => new ExpressionStatement
            {
                Token = expressionStatement.Token,
                Expression = ConvertExpression(expressionStatement.Expression, model, boundProgram),
            },
            P.IfStatement ifStatement => new IfStatement
            {
                Token = ifStatement.Token,
                Condition = ConvertExpression(ifStatement.Condition, model, boundProgram),
                Consequence = ConvertBlock(ifStatement.Consequence, model, boundProgram),
                Alternative = ConvertBlock(ifStatement.Alternative, model, boundProgram),
            },
            P.BlockStatement blockStatement => ConvertBlock(blockStatement, model, boundProgram),
            _ => throw new InvalidOperationException($"Unsupported lowered statement type: {statement.GetType().Name}"),
        };
    }

    private static BlockStatement ConvertBlock(P.BlockStatement block, SemanticModel model, BoundProgram boundProgram)
    {
        return new BlockStatement
        {
            Token = block.Token,
            Statements = block.Statements.Select(statement => ConvertStatement(statement, model, boundProgram)).ToList(),
        };
    }

    private static IExpression ConvertExpression(P.IExpression expression, SemanticModel model, BoundProgram boundProgram)
    {
        return expression switch
        {
            P.Identifier identifier => new Identifier
            {
                Token = identifier.Token,
                Value = identifier.Value,
                Type = model.GetNodeType(identifier),
            },
            P.IntegerLiteral integerLiteral => new IntegerLiteral
            {
                Token = integerLiteral.Token,
                Value = integerLiteral.Value,
            },
            P.BooleanLiteral booleanLiteral => new BooleanLiteral
            {
                Token = booleanLiteral.Token,
                Value = booleanLiteral.Value,
            },
            P.StringLiteral stringLiteral => new StringLiteral
            {
                Token = stringLiteral.Token,
                Value = stringLiteral.Value,
            },
            P.PrefixExpression prefixExpression => new PrefixExpression
            {
                Token = prefixExpression.Token,
                Operator = prefixExpression.Operator,
                Right = ConvertExpression(prefixExpression.Right, model, boundProgram),
                Type = model.GetNodeType(prefixExpression),
            },
            P.InfixExpression infixExpression => new InfixExpression
            {
                Token = infixExpression.Token,
                Left = ConvertExpression(infixExpression.Left, model, boundProgram),
                Operator = infixExpression.Operator,
                Right = ConvertExpression(infixExpression.Right, model, boundProgram),
                Type = model.GetNodeType(infixExpression),
            },
            P.IfExpression ifExpression => new IfExpression
            {
                Token = ifExpression.Token,
                Condition = ConvertExpression(ifExpression.Condition, model, boundProgram),
                Consequence = ConvertBlock(ifExpression.Consequence, model, boundProgram),
                Alternative = ifExpression.Alternative is null
                    ? null
                    : ConvertBlock(ifExpression.Alternative, model, boundProgram),
                Type = model.GetNodeType(ifExpression),
            },
            P.ArrayLiteral arrayLiteral => new ArrayLiteral
            {
                Token = arrayLiteral.Token,
                Elements = arrayLiteral.Elements.Select(element => ConvertExpression(element, model, boundProgram)).ToList(),
            },
            P.HashLiteral hashLiteral => new HashLiteral
            {
                Token = hashLiteral.Token,
                Pairs = hashLiteral.Pairs
                    .Select(pair => new KeyValuePair<IExpression, IExpression>(
                        ConvertExpression(pair.Key, model, boundProgram),
                        ConvertExpression(pair.Value, model, boundProgram)))
                    .ToList(),
            },
            P.IndexExpression indexExpression => new IndexExpression
            {
                Token = indexExpression.Token,
                Left = ConvertExpression(indexExpression.Left, model, boundProgram),
                Index = ConvertExpression(indexExpression.Index, model, boundProgram),
                Type = model.GetNodeType(indexExpression),
            },
            P.CallExpression callExpression => new CallExpression
            {
                Token = callExpression.Token,
                Function = ConvertExpression(callExpression.Function, model, boundProgram),
                Arguments = callExpression.Arguments.Select(argument => ConvertExpression(argument, model, boundProgram)).ToList(),
                Type = model.GetNodeType(callExpression),
            },
            P.IntrinsicCallExpression intrinsicCallExpression => new IntrinsicCallExpression
            {
                Token = intrinsicCallExpression.Token,
                Name = intrinsicCallExpression.Name,
                Arguments = intrinsicCallExpression.Arguments.Select(argument => ConvertExpression(argument, model, boundProgram)).ToList(),
                Type = model.GetNodeType(intrinsicCallExpression),
            },
            P.FunctionLiteral functionLiteral => new FunctionLiteral
            {
                Token = functionLiteral.Token,
                Name = functionLiteral.Name,
                Parameters = functionLiteral.Parameters.Select(parameter => new FunctionParameter
                {
                    Name = new Identifier
                    {
                        Token = parameter.Name.Token,
                        Value = parameter.Name.Value,
                        Type = model.GetNodeType(parameter.Name),
                    },
                }).ToList(),
                Body = ConvertBlock(functionLiteral.Body, model, boundProgram),
                Captures = boundProgram.GetClosureCaptureNames(functionLiteral).ToList(),
                Type = model.GetNodeType(functionLiteral),
            },
            _ => throw new InvalidOperationException($"Unsupported lowered expression type: {expression.GetType().Name}"),
        };
    }

    private List<P.IStatement> LowerStatements(IReadOnlyList<P.IStatement> statements, SemanticModel model, bool blockProducesValue)
    {
        var lowered = new List<P.IStatement>();
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var preserveExpressionIf = blockProducesValue && i == statements.Count - 1;
            lowered.AddRange(LowerStatement(statement, model, preserveExpressionIf));
        }

        return lowered;
    }

    private IEnumerable<P.IStatement> LowerStatement(P.IStatement statement, SemanticModel model, bool preserveExpressionIf)
    {
        switch (statement)
        {
            case P.LetStatement { Value: not null } letStatement:
            {
                var setup = new List<P.IStatement>();
                var loweredValue = LowerExpression(letStatement.Value, model, setup);
                letStatement.Value = loweredValue;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return letStatement;
                yield break;
            }
            case P.ReturnStatement { ReturnValue: not null } returnStatement:
            {
                var setup = new List<P.IStatement>();
                var loweredValue = LowerExpression(returnStatement.ReturnValue, model, setup);
                returnStatement.ReturnValue = loweredValue;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return returnStatement;
                yield break;
            }
            case P.ExpressionStatement { Expression: P.IfExpression ifExpression } expressionStatement:
            {
                if (preserveExpressionIf)
                {
                    var setup = new List<P.IStatement>();
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
            case P.ExpressionStatement { Expression: not null } expressionStatement:
            {
                var setup = new List<P.IStatement>();
                var loweredExpression = LowerExpression(expressionStatement.Expression, model, setup);
                expressionStatement.Expression = loweredExpression;
                foreach (var prep in setup)
                {
                    yield return prep;
                }

                yield return expressionStatement;
                yield break;
            }
            case P.BlockStatement blockStatement:
                blockStatement.Statements = LowerStatements(blockStatement.Statements, model, blockProducesValue: false);
                yield return blockStatement;
                yield break;
            default:
                yield return statement;
                yield break;
        }
    }

    private IEnumerable<P.IStatement> LowerExpressionIfStatement(P.IfExpression ifExpression, SemanticModel model)
    {
        var setup = new List<P.IStatement>();
        var loweredCondition = LowerExpression(ifExpression.Condition, model, setup);
        foreach (var prep in setup)
        {
            yield return prep;
        }

        ifExpression.Condition = loweredCondition;
        ifExpression.Consequence.Statements = LowerStatements(ifExpression.Consequence.Statements, model, blockProducesValue: false);
        var alternative = ifExpression.Alternative ?? new P.BlockStatement { Token = new Token(TokenType.LBrace, "{"), Statements = [] };
        alternative.Statements = LowerStatements(alternative.Statements, model, blockProducesValue: false);

        yield return new P.IfStatement
        {
            Token = ifExpression.Token,
            Condition = ifExpression.Condition,
            Consequence = ifExpression.Consequence,
            Alternative = alternative,
        };
    }

    private P.IExpression LowerExpression(P.IExpression expression, SemanticModel model, List<P.IStatement> setup)
    {
        switch (expression)
        {
            case P.CallExpression callExpression:
                return LowerCallExpression(callExpression, model, setup);
            case P.InfixExpression infixExpression:
                infixExpression.Left = LowerExpression(infixExpression.Left, model, setup);
                infixExpression.Right = LowerExpression(infixExpression.Right, model, setup);
                return infixExpression;
            case P.PrefixExpression prefixExpression:
                prefixExpression.Right = LowerExpression(prefixExpression.Right, model, setup);
                return prefixExpression;
            case P.IndexExpression indexExpression:
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
            case P.IfExpression ifExpression:
                ifExpression.Condition = LowerExpression(ifExpression.Condition, model, setup);
                ifExpression.Consequence.Statements = LowerStatements(ifExpression.Consequence.Statements, model, blockProducesValue: true);
                if (ifExpression.Alternative is not null)
                {
                    ifExpression.Alternative.Statements = LowerStatements(ifExpression.Alternative.Statements, model, blockProducesValue: true);
                }

                return ifExpression;
            case P.ArrayLiteral arrayLiteral:
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
            case P.HashLiteral hashLiteral:
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

                    hashLiteral.Pairs[i] = new KeyValuePair<P.IExpression, P.IExpression>(loweredKey, loweredValue);
                }

                return hashLiteral;
            case P.FunctionLiteral functionLiteral:
                functionLiteral.Body.Statements = LowerStatements(functionLiteral.Body.Statements, model, blockProducesValue: true);
                return functionLiteral;
            default:
                return expression;
        }
    }

    private P.IExpression LowerCallExpression(P.CallExpression callExpression, SemanticModel model, List<P.IStatement> setup)
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

        if (callExpression.Function is P.Identifier identifier && IsBuiltin(identifier.Value))
        {
            var intrinsic = new P.IntrinsicCallExpression
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

    private P.Identifier CreateTemporary(P.IExpression valueExpression, SemanticModel model, List<P.IStatement> setup)
    {
        var tempName = NextUniqueTemporaryName();
        var tempIdentifier = new P.Identifier
        {
            Token = new Token(TokenType.Ident, tempName),
            Value = tempName,
        };
        var tempLet = new P.LetStatement
        {
            Token = new Token(TokenType.Let, "let"),
            Name = new P.Identifier
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

    private void ReserveNames(P.Program program)
    {
        foreach (var statement in program.Statements)
        {
            ReserveNames(statement);
        }
    }

    private void ReserveNames(P.IStatement statement)
    {
        switch (statement)
        {
            case P.LetStatement letStatement:
                _reservedNames.Add(letStatement.Name.Value);
                if (letStatement.Value is not null)
                {
                    ReserveNames(letStatement.Value);
                }

                break;
            case P.AssignStatement assignStatement:
                _reservedNames.Add(assignStatement.Name.Value);
                ReserveNames(assignStatement.Value);
                break;
            case P.ReturnStatement { ReturnValue: not null } returnStatement:
                ReserveNames(returnStatement.ReturnValue);
                break;
            case P.ExpressionStatement { Expression: not null } expressionStatement:
                ReserveNames(expressionStatement.Expression);
                break;
            case P.IfStatement ifStatement:
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
            case P.BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    ReserveNames(inner);
                }

                break;
        }
    }

    private void ReserveNames(P.IExpression expression)
    {
        switch (expression)
        {
            case P.Identifier identifier:
                _reservedNames.Add(identifier.Value);
                break;
            case P.PrefixExpression prefixExpression:
                ReserveNames(prefixExpression.Right);
                break;
            case P.InfixExpression infixExpression:
                ReserveNames(infixExpression.Left);
                ReserveNames(infixExpression.Right);
                break;
            case P.IfExpression ifExpression:
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
            case P.FunctionLiteral functionLiteral:
                foreach (var parameter in functionLiteral.Parameters)
                {
                    _reservedNames.Add(parameter.Name.Value);
                }

                foreach (var inner in functionLiteral.Body.Statements)
                {
                    ReserveNames(inner);
                }

                break;
            case P.CallExpression callExpression:
                ReserveNames(callExpression.Function);
                foreach (var argument in callExpression.Arguments)
                {
                    ReserveNames(argument);
                }

                break;
            case P.IntrinsicCallExpression intrinsicCall:
                foreach (var argument in intrinsicCall.Arguments)
                {
                    ReserveNames(argument);
                }

                break;
            case P.ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ReserveNames(element);
                }

                break;
            case P.HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    ReserveNames(pair.Key);
                    ReserveNames(pair.Value);
                }

                break;
            case P.IndexExpression indexExpression:
                ReserveNames(indexExpression.Left);
                ReserveNames(indexExpression.Index);
                break;
        }
    }

    private static bool RequiresTemporary(P.IExpression expression)
    {
        return expression switch
        {
            P.Identifier => false,
            P.IntegerLiteral => false,
            P.BooleanLiteral => false,
            P.StringLiteral => false,
            P.FunctionLiteral => false,
            _ => true,
        };
    }

    private static bool IsBuiltin(string name)
    {
        return name is "puts" or "len" or "push";
    }
}
