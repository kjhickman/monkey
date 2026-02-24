using Kong.Common;
using Kong.Lexing;
using System.Globalization;

namespace Kong.Parsing;

internal sealed class ExpressionParser
{
    private readonly ParserContext _context;
    private readonly Dictionary<TokenType, Func<IExpression>> _prefixParseFns;
    private readonly Dictionary<TokenType, Func<IExpression, IExpression>> _infixParseFns;
    private StatementParser _statementParser = null!;
    private TypeParser _typeParser = null!;

    internal ExpressionParser(ParserContext context)
    {
        _context = context;

        _prefixParseFns = new Dictionary<TokenType, Func<IExpression>>
        {
            { TokenType.Identifier, ParseIdentifier },
            { TokenType.Self, ParseIdentifier },
            { TokenType.Integer, ParseIntegerLiteral },
            { TokenType.Double, ParseDoubleLiteral },
            { TokenType.Char, ParseCharLiteral },
            { TokenType.Byte, ParseByteLiteral },
            { TokenType.String, ParseStringLiteral },
            { TokenType.Function, ParseFunctionLiteral },
            { TokenType.True, ParseBoolean },
            { TokenType.False, ParseBoolean },
            { TokenType.Bang, ParsePrefixExpression },
            { TokenType.Minus, ParsePrefixExpression },
            { TokenType.LeftParenthesis, ParseGroupedOrLambdaExpression },
            { TokenType.If, ParseIfExpression },
            { TokenType.Loop, ParseLoopExpression },
            { TokenType.Match, ParseMatchExpression },
            { TokenType.LeftBracket, ParseArrayLiteral },
            { TokenType.New, ParseNewExpression },
        };

        _infixParseFns = new Dictionary<TokenType, Func<IExpression, IExpression>>
        {
            { TokenType.Plus, ParseInfixExpression },
            { TokenType.Minus, ParseInfixExpression },
            { TokenType.Asterisk, ParseInfixExpression },
            { TokenType.Slash, ParseInfixExpression },
            { TokenType.Equal, ParseInfixExpression },
            { TokenType.NotEqual, ParseInfixExpression },
            { TokenType.And, ParseInfixExpression },
            { TokenType.Or, ParseInfixExpression },
            { TokenType.LessThan, ParseInfixExpression },
            { TokenType.GreaterThan, ParseInfixExpression },
            { TokenType.LeftParenthesis, ParseCallExpression },
            { TokenType.LeftBracket, ParseIndexExpression },
            { TokenType.Dot, ParseMemberAccessExpression },
        };
    }

    internal void Initialize(StatementParser statementParser, TypeParser typeParser)
    {
        _statementParser = statementParser;
        _typeParser = typeParser;
    }

    internal bool CanParsePrefix(TokenType tokenType)
    {
        return _prefixParseFns.ContainsKey(tokenType);
    }

    internal IExpression? ParseExpression(Precedence precedence)
    {
        if (!_prefixParseFns.TryGetValue(_context.CurToken.Type, out var prefix))
        {
            _context.NoPrefixParseFnError(_context.CurToken.Type);
            return null;
        }

        var leftExpression = prefix();
        if (leftExpression == null)
        {
            return null;
        }

        while (precedence < _context.PeekPrecedence())
        {
            if (!_infixParseFns.TryGetValue(_context.PeekToken.Type, out var infix))
            {
                return leftExpression;
            }

            _context.NextToken();

            leftExpression = infix(leftExpression);
        }

        return leftExpression;
    }

    internal IExpression ParsePrefixExpression()
    {
        var startSpan = _context.CurToken.Span;
        var expression = new PrefixExpression
        {
            Token = _context.CurToken,
            Operator = _context.CurToken.Literal,
        };

        _context.NextToken();

        expression.Right = ParseExpression(Precedence.Prefix)!;
        expression.Span = new Span(startSpan.Start, expression.Right.Span.End);

        return expression;
    }

    internal IExpression ParseInfixExpression(IExpression left)
    {
        var expression = new InfixExpression
        {
            Token = _context.CurToken,
            Operator = _context.CurToken.Literal,
            Left = left,
        };

        var precedence = _context.CurPrecedence();
        _context.NextToken();
        expression.Right = ParseExpression(precedence)!;

        // Span covers from start of left to end of right
        expression.Span = new Span(left.Span.Start, expression.Right.Span.End);

        return expression;
    }

    internal IExpression ParseIdentifier()
    {
        if (_context.PeekTokenIs(TokenType.DoubleArrow))
        {
            return ParseSingleParameterLambda();
        }

        return new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };
    }

    internal IExpression ParseSingleParameterLambda()
    {
        var startSpan = _context.CurToken.Span.Start;
        var parameter = new FunctionParameter
        {
            Token = _context.CurToken,
            Name = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        _context.NextToken();
        _context.NextToken();
        var bodyExpression = ParseExpression(Precedence.Lowest);
        if (bodyExpression == null)
        {
            return null!;
        }

        var bodyStatement = new ExpressionStatement
        {
            Token = _context.CurToken,
            Expression = bodyExpression,
            Span = bodyExpression.Span,
        };

        var literal = new FunctionLiteral
        {
            Token = parameter.Token,
            IsLambda = true,
            Parameters = [parameter],
            Body = new BlockStatement
            {
                Token = _context.CurToken,
                Statements = [bodyStatement],
                Span = bodyExpression.Span,
            },
            Span = new Span(startSpan, bodyExpression.Span.End),
        };

        return literal;
    }

    internal IExpression ParseIntegerLiteral()
    {
        var literal = new IntegerLiteral
        {
            Token = _context.CurToken,
            Span = _context.CurToken.Span,
        };

        if (!long.TryParse(_context.CurToken.Literal, out var value))
        {
            var msg = $"could not parse \"{_context.CurToken.Literal}\" as integer";
            _context.Diagnostics.Report(_context.CurToken.Span, msg, "P003");
            return null!;
        }

        literal.Value = value;

        return literal;
    }

    internal IExpression ParseStringLiteral()
    {
        return new StringLiteral
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };
    }

    internal IExpression ParseDoubleLiteral()
    {
        var literal = new DoubleLiteral
        {
            Token = _context.CurToken,
            Span = _context.CurToken.Span,
        };

        if (!double.TryParse(_context.CurToken.Literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _context.Diagnostics.Report(_context.CurToken.Span, $"could not parse \"{_context.CurToken.Literal}\" as double", "P003");
            return null!;
        }

        literal.Value = value;
        return literal;
    }

    internal IExpression ParseCharLiteral()
    {
        var literal = new CharLiteral
        {
            Token = _context.CurToken,
            Span = _context.CurToken.Span,
        };

        if (_context.CurToken.Literal.Length != 1)
        {
            _context.Diagnostics.Report(_context.CurToken.Span, $"could not parse \"{_context.CurToken.Literal}\" as char", "P003");
            return null!;
        }

        literal.Value = _context.CurToken.Literal[0];
        return literal;
    }

    internal IExpression ParseByteLiteral()
    {
        var literal = new ByteLiteral
        {
            Token = _context.CurToken,
            Span = _context.CurToken.Span,
        };

        if (!byte.TryParse(_context.CurToken.Literal, out var value))
        {
            _context.Diagnostics.Report(_context.CurToken.Span, $"could not parse \"{_context.CurToken.Literal}\" as byte", "P003");
            return null!;
        }

        literal.Value = value;
        return literal;
    }

    internal IExpression ParseFunctionLiteral()
    {
        _context.Diagnostics.Report(_context.CurToken.Span,
            "anonymous function literals must use '(args) => expr' syntax",
            "P006");
        return null!;
    }

    internal IExpression ParseGroupedOrLambdaExpression()
    {
        var startSpan = _context.CurToken.Span.Start;

        if (!_context.PeekTokenIs(TokenType.RightParenthesis) && !_context.PeekTokenIs(TokenType.Identifier) && !_context.PeekTokenIs(TokenType.Self))
        {
            return ParseGroupedExpression();
        }

        var (parameters, endSpan, isLambda) = TryParseLambdaParameters();
        if (!isLambda)
        {
            return ParseGroupedExpression();
        }

        var literal = new FunctionLiteral
        {
            Token = _context.CurToken,
            IsLambda = true,
            Parameters = parameters,
        };

        _context.NextToken();
        var bodyExpression = ParseExpression(Precedence.Lowest);
        if (bodyExpression == null)
        {
            return null!;
        }

        var bodyStatement = new ExpressionStatement
        {
            Token = _context.CurToken,
            Expression = bodyExpression,
            Span = bodyExpression.Span,
        };

        literal.Body = new BlockStatement
        {
            Token = _context.CurToken,
            Statements = [bodyStatement],
            Span = bodyExpression.Span,
        };
        literal.Span = new Span(startSpan, bodyExpression.Span.End);
        return literal;
    }

    internal (List<FunctionParameter> Parameters, Position EndSpan, bool IsLambda) TryParseLambdaParameters()
    {
        var parameters = new List<FunctionParameter>();
        var endSpan = default(Position);

        if (_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            if (_context.PeekTokenIs(TokenType.DoubleArrow))
            {
                _context.NextToken();
                endSpan = _context.CurToken.Span.End;
                return (parameters, endSpan, true);
            }

            return (parameters, default, false);
        }

        _context.NextToken();
        var parameter = ParseLambdaParameter();
        if (parameter == null)
        {
            return (parameters, default, false);
        }

        parameters.Add(parameter);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();
            var nextParameter = ParseLambdaParameter();
            if (nextParameter == null)
            {
                return (parameters, default, false);
            }

            parameters.Add(nextParameter);
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return (parameters, default, false);
        }

        endSpan = _context.CurToken.Span.End;
        if (!_context.ExpectPeek(TokenType.DoubleArrow))
        {
            return (parameters, endSpan, false);
        }

        return (parameters, endSpan, true);
    }

    internal FunctionParameter? ParseLambdaParameter()
    {
        if (!_context.CurTokenIs(TokenType.Identifier) && !_context.CurTokenIs(TokenType.Self))
        {
            return null;
        }

        var parameter = new FunctionParameter
        {
            Token = _context.CurToken,
            Name = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.Colon))
        {
            _context.NextToken();
            _context.NextToken();
            parameter.TypeAnnotation = _typeParser.ParseTypeNode();
            if (parameter.TypeAnnotation == null)
            {
                return null;
            }

            parameter.Span = new Span(parameter.Span.Start, parameter.TypeAnnotation.Span.End);
        }

        return parameter;
    }

    internal IExpression ParseBoolean()
    {
        return new BooleanLiteral
        {
            Token = _context.CurToken,
            Value = _context.CurTokenIs(TokenType.True),
            Span = _context.CurToken.Span,
        };
    }

    internal IExpression ParseGroupedExpression()
    {
        _context.NextToken();

        var expression = ParseExpression(Precedence.Lowest);

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return expression!;
    }

    internal IExpression ParseIfExpression()
    {
        var startSpan = _context.CurToken.Span;
        var expression = new IfExpression { Token = _context.CurToken };

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null!;
        }

        _context.NextToken();
        expression.Condition = ParseExpression(Precedence.Lowest)!;

        if (!_context.ExpectPeek(TokenType.RightParenthesis) || !_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        expression.Consequence = _statementParser.ParseBlockStatement();

        if (!_context.PeekTokenIs(TokenType.Else))
        {
            expression.Span = new Span(startSpan.Start, expression.Consequence.Span.End);
            return expression;
        }

        _context.NextToken();
        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        expression.Alternative = _statementParser.ParseBlockStatement();
        expression.Span = new Span(startSpan.Start, expression.Alternative.Span.End);

        return expression;
    }

    internal IExpression ParseLoopExpression()
    {
        var startSpan = _context.CurToken.Span;
        var expression = new LoopExpression { Token = _context.CurToken };

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        _context.LoopExpressionDepth++;
        expression.Body = _statementParser.ParseBlockStatement();
        _context.LoopExpressionDepth--;
        expression.Span = new Span(startSpan.Start, expression.Body.Span.End);
        return expression;
    }

    internal IExpression ParseMatchExpression()
    {
        var startSpan = _context.CurToken.Span;
        var expression = new MatchExpression
        {
            Token = _context.CurToken,
        };

        if (_context.PeekTokenIs(TokenType.LeftParenthesis))
        {
            _context.Diagnostics.Report(
                _context.PeekToken.Span,
                "match target must not be parenthesized; use 'match <expr> { ... }'",
                "P001");

            _context.NextToken();
            _context.NextToken();
            var parenthesizedTarget = ParseExpression(Precedence.Lowest);
            if (parenthesizedTarget == null)
            {
                return null!;
            }

            expression.Target = parenthesizedTarget;

            if (!_context.ExpectPeek(TokenType.RightParenthesis))
            {
                return null!;
            }
        }
        else
        {
            _context.NextToken();
            var target = ParseExpression(Precedence.Lowest);
            if (target == null)
            {
                return null!;
            }

            expression.Target = target;
        }

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        while (!_context.PeekTokenIs(TokenType.RightBrace))
        {
            var arm = ParseMatchArm();
            if (arm == null)
            {
                return null!;
            }

            expression.Arms.Add(arm);

            if (_context.PeekTokenIs(TokenType.Comma))
            {
                _context.NextToken();
            }
        }

        if (!_context.ExpectPeek(TokenType.RightBrace))
        {
            return null!;
        }

        expression.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return expression;
    }

    internal MatchArm? ParseMatchArm()
    {
        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var variantToken = _context.CurToken;
        var arm = new MatchArm
        {
            Token = variantToken,
            VariantName = new Identifier
            {
                Token = variantToken,
                Value = variantToken.Literal,
                Span = variantToken.Span,
            },
        };

        if (_context.PeekTokenIs(TokenType.LeftParenthesis))
        {
            _context.NextToken();
            arm.Bindings = ParseMatchArmBindings();
            if (arm.Bindings.Count == 0 && !_context.CurTokenIs(TokenType.RightParenthesis))
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.DoubleArrow))
        {
            return null;
        }

        _context.NextToken();
        var bodyToken = _context.CurToken;
        var bodyExpression = ParseExpression(Precedence.Lowest);
        if (bodyExpression == null)
        {
            return null;
        }

        var bodyStatement = new ExpressionStatement
        {
            Token = bodyToken,
            Expression = bodyExpression,
            Span = bodyExpression.Span,
        };

        arm.Body = new BlockStatement
        {
            Token = bodyToken,
            Statements = [bodyStatement],
            Span = bodyExpression.Span,
        };
        arm.Span = new Span(variantToken.Span.Start, bodyExpression.Span.End);
        return arm;
    }

    internal List<Identifier> ParseMatchArmBindings()
    {
        var bindings = new List<Identifier>();
        if (_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            return bindings;
        }

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return [];
        }

        bindings.Add(new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        });

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return [];
            }

            bindings.Add(new Identifier
            {
                Token = _context.CurToken,
                Value = _context.CurToken.Literal,
                Span = _context.CurToken.Span,
            });
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return [];
        }

        return bindings;
    }

    internal IExpression ParseCallExpression(IExpression function)
    {
        var exp = new CallExpression
        {
            Token = _context.CurToken,
            Function = function,
            Arguments = ParseCallArguments(),
            // Span from start of function expression to closing ')'
            Span = new Span(function.Span.Start, _context.CurToken.Span.End)
        };
        return exp;
    }

    internal IExpression ParseMemberAccessExpression(IExpression obj)
    {
        var start = obj.Span.Start;
        var dotToken = _context.CurToken;

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null!;
        }

        return new MemberAccessExpression
        {
            Token = dotToken,
            Object = obj,
            Member = _context.CurToken.Literal,
            Span = new Span(start, _context.CurToken.Span.End),
        };
    }

    internal IExpression ParseArrayLiteral()
    {
        var startSpan = _context.CurToken.Span;
        var array = new ArrayLiteral
        {
            Token = _context.CurToken,
            Elements = ParseExpressionList(TokenType.RightBracket),
            // Span from '[' to ']'
            Span = new Span(startSpan.Start, _context.CurToken.Span.End)
        };
        return array;
    }

    internal IExpression ParseNewExpression()
    {
        var startSpan = _context.CurToken.Span;
        var expression = new NewExpression
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null!;
        }

        var segments = new List<string> { _context.CurToken.Literal };
        while (_context.PeekTokenIs(TokenType.Dot))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return null!;
            }

            segments.Add(_context.CurToken.Literal);
        }

        expression.TypePath = string.Join('.', segments);

        if (_context.PeekTokenIs(TokenType.LessThan))
        {
            _context.NextToken();
            expression.TypeArguments = _typeParser.ParseTypeArgumentList();
            if (expression.TypeArguments.Count == 0)
            {
                return null!;
            }
        }

        if (!_context.ExpectPeek(TokenType.LeftParenthesis))
        {
            return null!;
        }

        expression.Arguments = ParseExpressionList(TokenType.RightParenthesis);
        expression.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return expression;
    }

    internal List<IExpression> ParseExpressionList(TokenType end)
    {
        var list = new List<IExpression>();

        if (_context.PeekTokenIs(end))
        {
            _context.NextToken();
            return list;
        }

        _context.NextToken();
        list.Add(ParseExpression(Precedence.Lowest)!);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();
            list.Add(ParseExpression(Precedence.Lowest)!);
        }

        if (!_context.ExpectPeek(end))
        {
            return null!;
        }

        return list;
    }

    internal List<CallArgument> ParseCallArguments()
    {
        var arguments = new List<CallArgument>();

        if (_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            return arguments;
        }

        _context.NextToken();
        var first = ParseCallArgument();
        if (first == null)
        {
            return null!;
        }

        arguments.Add(first);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();
            var argument = ParseCallArgument();
            if (argument == null)
            {
                return null!;
            }

            arguments.Add(argument);
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return arguments;
    }

    internal CallArgument? ParseCallArgument()
    {
        var modifier = CallArgumentModifier.None;
        var token = _context.CurToken;
        if (_context.CurTokenIs(TokenType.Out))
        {
            modifier = CallArgumentModifier.Out;
            _context.NextToken();
        }
        else if (_context.CurTokenIs(TokenType.Ref))
        {
            modifier = CallArgumentModifier.Ref;
            _context.NextToken();
        }

        var expression = ParseExpression(Precedence.Lowest);
        if (expression == null)
        {
            return null;
        }

        return new CallArgument
        {
            Token = token,
            Modifier = modifier,
            Expression = expression,
            Span = expression.Span,
        };
    }

    internal IExpression ParseIndexExpression(IExpression left)
    {
        var expression = new IndexExpression { Token = _context.CurToken, Left = left };

        _context.NextToken();
        expression.Index = ParseExpression(Precedence.Lowest)!;

        if (!_context.ExpectPeek(TokenType.RightBracket))
        {
            return null!;
        }

        // Span from start of left expression to closing ']'
        expression.Span = new Span(left.Span.Start, _context.CurToken.Span.End);

        return expression;
    }

}
