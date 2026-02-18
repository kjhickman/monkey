namespace Kong;

public enum Precedence
{
    Lowest = 0,
    Equality,    // ==
    Comparison,  // > or <
    Sum,         // +
    Product,     // *
    Prefix,      // -X or !X
    FunctionCall, // myFunction(X)
    Index,       // array[index]
}

public class Parser
{
    private readonly Lexer _lexer;
    private readonly DiagnosticBag _diagnostics;

    private Token _curToken;
    private Token _peekToken;

    private readonly Dictionary<TokenType, Func<IExpression>> _prefixParseFns;
    private readonly Dictionary<TokenType, Func<IExpression, IExpression>> _infixParseFns;

    private static readonly Dictionary<TokenType, Precedence> Precedences = new()
    {
        { TokenType.Equal, Precedence.Equality },
        { TokenType.NotEqual, Precedence.Equality },
        { TokenType.LessThan, Precedence.Comparison },
        { TokenType.GreaterThan, Precedence.Comparison },
        { TokenType.Plus, Precedence.Sum },
        { TokenType.Minus, Precedence.Sum },
        { TokenType.Slash, Precedence.Product },
        { TokenType.Asterisk, Precedence.Product },
        { TokenType.LeftParenthesis, Precedence.FunctionCall },
        { TokenType.LeftBracket, Precedence.Index },
    };

    public Parser(Lexer lexer)
    {
        _lexer = lexer;
        _diagnostics = new DiagnosticBag();

        _prefixParseFns = new Dictionary<TokenType, Func<IExpression>>
        {
            { TokenType.Identifier, ParseIdentifier },
            { TokenType.Integer, ParseIntegerLiteral },
            { TokenType.String, ParseStringLiteral },
            { TokenType.Function, ParseFunctionLiteral },
            { TokenType.True, ParseBoolean },
            { TokenType.False, ParseBoolean },
            { TokenType.Bang, ParsePrefixExpression },
            { TokenType.Minus, ParsePrefixExpression },
            { TokenType.LeftParenthesis, ParseGroupedExpression },
            { TokenType.If, ParseIfExpression },
            { TokenType.LeftBracket, ParseArrayLiteral },
        };

        _infixParseFns = new Dictionary<TokenType, Func<IExpression, IExpression>>
        {
            { TokenType.Plus, ParseInfixExpression },
            { TokenType.Minus, ParseInfixExpression },
            { TokenType.Asterisk, ParseInfixExpression },
            { TokenType.Slash, ParseInfixExpression },
            { TokenType.Equal, ParseInfixExpression },
            { TokenType.NotEqual, ParseInfixExpression },
            { TokenType.LessThan, ParseInfixExpression },
            { TokenType.GreaterThan, ParseInfixExpression },
            { TokenType.LeftParenthesis, ParseCallExpression },
            { TokenType.LeftBracket, ParseIndexExpression },
        };

        // Read two tokens, so _curToken and _peekToken are both set
        NextToken();
        NextToken();
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    public CompilationUnit ParseCompilationUnit()
    {
        var unit = new CompilationUnit();
        var start = _curToken.Span.Start;

        while (!CurTokenIs(TokenType.EndOfFile))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                unit.Statements.Add(statement);
            }
            NextToken();
        }

        if (unit.Statements.Count > 0)
        {
            unit.Span = new Span(start, unit.Statements[^1].Span.End);
        }

        return unit;
    }

    private void NextToken()
    {
        _curToken = _peekToken;
        _peekToken = _lexer.NextToken();
    }

    private bool CurTokenIs(TokenType t)
    {
        return _curToken.Type == t;
    }

    private bool PeekTokenIs(TokenType t)
    {
        return _peekToken.Type == t;
    }

    private bool ExpectPeek(TokenType t)
    {
        if (PeekTokenIs(t))
        {
            NextToken();
            return true;
        }

        PeekError(t);
        return false;
    }

    private void PeekError(TokenType t)
    {
        var msg = $"expected next token to be {t}, got {_peekToken.Type} instead";
        _diagnostics.Report(_peekToken.Span, msg, "P001");
    }

    private void NoPrefixParseFnError(TokenType t)
    {
        var msg = $"no prefix parse function for {t} found";
        _diagnostics.Report(_curToken.Span, msg, "P002");
    }

    private Precedence PeekPrecedence()
    {
        return Precedences.GetValueOrDefault(_peekToken.Type, Precedence.Lowest);
    }

    private Precedence CurPrecedence()
    {
        return Precedences.GetValueOrDefault(_curToken.Type, Precedence.Lowest);
    }

    private IStatement? ParseStatement()
    {
        return _curToken.Type switch
        {
            TokenType.Let => ParseLetStatement(),
            TokenType.Return => ParseReturnStatement(),
            _ => ParseExpressionStatement(),
        };
    }

    private LetStatement? ParseLetStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new LetStatement { Token = _curToken };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        statement.Name = new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };

        if (PeekTokenIs(TokenType.Colon))
        {
            NextToken();
            NextToken();

            statement.TypeAnnotation = ParseTypeNode();
            if (statement.TypeAnnotation == null)
            {
                return null;
            }
        }

        if (!ExpectPeek(TokenType.Assign))
        {
            return null;
        }

        NextToken();

        statement.Value = ParseExpression(Precedence.Lowest);

        if (statement.Value is FunctionLiteral fl)
        {
            fl.Name = statement.Name.Value;
        }

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
        }

        statement.Span = new Span(startSpan.Start, _curToken.Span.End);
        return statement;
    }

    private ITypeNode? ParseTypeNode()
    {
        ITypeNode? type = ParseTypePrimary();
        if (type == null)
        {
            return null;
        }

        while (PeekTokenIs(TokenType.LeftBracket))
        {
            var start = type.Span.Start;

            NextToken();
            if (!ExpectPeek(TokenType.RightBracket))
            {
                return null;
            }

            type = new ArrayType
            {
                Token = _curToken,
                ElementType = type,
                Span = new Span(start, _curToken.Span.End),
            };
        }

        return type;
    }

    private ITypeNode? ParseTypePrimary()
    {
        if (!CurTokenIs(TokenType.Identifier))
        {
            _diagnostics.Report(_curToken.Span, $"expected type name, got {_curToken.Type}", "P004");
            return null;
        }

        return new NamedType
        {
            Token = _curToken,
            Name = _curToken.Literal,
            Span = _curToken.Span,
        };
    }

    private ReturnStatement ParseReturnStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new ReturnStatement { Token = _curToken };

        NextToken();

        statement.ReturnValue = ParseExpression(Precedence.Lowest);

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
        }

        statement.Span = new Span(startSpan.Start, _curToken.Span.End);
        return statement;
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new ExpressionStatement
        {
            Token = _curToken,
            Expression = ParseExpression(Precedence.Lowest),
        };

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
        }

        statement.Span = new Span(startSpan.Start, _curToken.Span.End);
        return statement;
    }

    private BlockStatement ParseBlockStatement()
    {
        var startSpan = _curToken.Span;
        var block = new BlockStatement { Token = _curToken };

        NextToken();

        while (!CurTokenIs(TokenType.RightBrace) && !CurTokenIs(TokenType.EndOfFile))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                block.Statements.Add(stmt);
            }
            NextToken();
        }

        // _curToken is now the '}' token
        block.Span = new Span(startSpan.Start, _curToken.Span.End);
        return block;
    }

    private IExpression? ParseExpression(Precedence precedence)
    {
        if (!_prefixParseFns.TryGetValue(_curToken.Type, out var prefix))
        {
            NoPrefixParseFnError(_curToken.Type);
            return null;
        }

        var leftExpression = prefix();

        while (!PeekTokenIs(TokenType.Semicolon) && precedence < PeekPrecedence())
        {
            if (!_infixParseFns.TryGetValue(_peekToken.Type, out var infix))
            {
                return leftExpression;
            }

            NextToken();

            leftExpression = infix(leftExpression);
        }

        return leftExpression;
    }

    private IExpression ParsePrefixExpression()
    {
        var startSpan = _curToken.Span;
        var expression = new PrefixExpression
        {
            Token = _curToken,
            Operator = _curToken.Literal,
        };

        NextToken();

        expression.Right = ParseExpression(Precedence.Prefix)!;
        expression.Span = new Span(startSpan.Start, expression.Right.Span.End);

        return expression;
    }

    private IExpression ParseInfixExpression(IExpression left)
    {
        var expression = new InfixExpression
        {
            Token = _curToken,
            Operator = _curToken.Literal,
            Left = left,
        };

        var precedence = CurPrecedence();
        NextToken();
        expression.Right = ParseExpression(precedence)!;

        // Span covers from start of left to end of right
        expression.Span = new Span(left.Span.Start, expression.Right.Span.End);

        return expression;
    }

    private IExpression ParseIdentifier()
    {
        return new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };
    }

    private IExpression ParseIntegerLiteral()
    {
        var literal = new IntegerLiteral
        {
            Token = _curToken,
            Span = _curToken.Span,
        };

        if (!long.TryParse(_curToken.Literal, out var value))
        {
            var msg = $"could not parse \"{_curToken.Literal}\" as integer";
            _diagnostics.Report(_curToken.Span, msg, "P003");
            return null!;
        }

        literal.Value = value;

        return literal;
    }

    private IExpression ParseStringLiteral()
    {
        return new StringLiteral
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };
    }

    private IExpression ParseFunctionLiteral()
    {
        var startSpan = _curToken.Span;
        var literal = new FunctionLiteral { Token = _curToken };

        if (!ExpectPeek(TokenType.LeftParenthesis))
        {
            return null!;
        }

        literal.Parameters = ParseFunctionParameters();

        if (PeekTokenIs(TokenType.Arrow))
        {
            NextToken();
            NextToken();

            literal.ReturnTypeAnnotation = ParseTypeNode();
            if (literal.ReturnTypeAnnotation == null)
            {
                return null!;
            }
        }

        if (!ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        literal.Body = ParseBlockStatement();

        // Span from 'fn' to closing '}' of body
        literal.Span = new Span(startSpan.Start, literal.Body.Span.End);

        return literal;
    }

    private IExpression ParseBoolean()
    {
        return new BooleanLiteral
        {
            Token = _curToken,
            Value = CurTokenIs(TokenType.True),
            Span = _curToken.Span,
        };
    }

    private IExpression ParseGroupedExpression()
    {
        NextToken();

        var expression = ParseExpression(Precedence.Lowest);

        if (!ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return expression!;
    }

    private IExpression ParseIfExpression()
    {
        var startSpan = _curToken.Span;
        var expression = new IfExpression { Token = _curToken };

        if (!ExpectPeek(TokenType.LeftParenthesis))
        {
            return null!;
        }

        NextToken();
        expression.Condition = ParseExpression(Precedence.Lowest)!;

        if (!ExpectPeek(TokenType.RightParenthesis) || !ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        expression.Consequence = ParseBlockStatement();

        if (!PeekTokenIs(TokenType.Else))
        {
            expression.Span = new Span(startSpan.Start, expression.Consequence.Span.End);
            return expression;
        }

        NextToken();
        if (!ExpectPeek(TokenType.LeftBrace))
        {
            return null!;
        }

        expression.Alternative = ParseBlockStatement();
        expression.Span = new Span(startSpan.Start, expression.Alternative.Span.End);

        return expression;
    }

    private IExpression ParseCallExpression(IExpression function)
    {
        var exp = new CallExpression
        {
            Token = _curToken,
            Function = function,
            Arguments = ParseExpressionList(TokenType.RightParenthesis),
        };
        // Span from start of function expression to closing ')'
        exp.Span = new Span(function.Span.Start, _curToken.Span.End);
        return exp;
    }

    private List<FunctionParameter> ParseFunctionParameters()
    {
        var parameters = new List<FunctionParameter>();

        if (PeekTokenIs(TokenType.RightParenthesis))
        {
            NextToken();
            return parameters;
        }

        NextToken();

        var parameter = ParseFunctionParameter();
        if (parameter == null)
        {
            return null!;
        }
        parameters.Add(parameter);

        while (PeekTokenIs(TokenType.Comma))
        {
            NextToken();
            NextToken();
            parameter = ParseFunctionParameter();
            if (parameter == null)
            {
                return null!;
            }
            parameters.Add(parameter);
        }

        if (!ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return parameters;
    }

    private FunctionParameter? ParseFunctionParameter()
    {
        if (!CurTokenIs(TokenType.Identifier))
        {
            _diagnostics.Report(_curToken.Span, $"expected parameter name, got {_curToken.Type}", "P005");
            return null;
        }

        var parameter = new FunctionParameter
        {
            Token = _curToken,
            Name = _curToken.Literal,
            Span = _curToken.Span,
        };

        if (PeekTokenIs(TokenType.Colon))
        {
            NextToken();
            NextToken();

            parameter.TypeAnnotation = ParseTypeNode();
            if (parameter.TypeAnnotation == null)
            {
                return null;
            }

            parameter.Span = new Span(parameter.Span.Start, parameter.TypeAnnotation.Span.End);
        }

        return parameter;
    }

    private IExpression ParseArrayLiteral()
    {
        var startSpan = _curToken.Span;
        var array = new ArrayLiteral
        {
            Token = _curToken,
            Elements = ParseExpressionList(TokenType.RightBracket),
        };
        // Span from '[' to ']'
        array.Span = new Span(startSpan.Start, _curToken.Span.End);
        return array;
    }

    private List<IExpression> ParseExpressionList(TokenType end)
    {
        var list = new List<IExpression>();

        if (PeekTokenIs(end))
        {
            NextToken();
            return list;
        }

        NextToken();
        list.Add(ParseExpression(Precedence.Lowest)!);

        while (PeekTokenIs(TokenType.Comma))
        {
            NextToken();
            NextToken();
            list.Add(ParseExpression(Precedence.Lowest)!);
        }

        if (!ExpectPeek(end))
        {
            return null!;
        }

        return list;
    }

    private IExpression ParseIndexExpression(IExpression left)
    {
        var expression = new IndexExpression { Token = _curToken, Left = left };

        NextToken();
        expression.Index = ParseExpression(Precedence.Lowest)!;

        if (!ExpectPeek(TokenType.RightBracket))
        {
            return null!;
        }

        // Span from start of left expression to closing ']'
        expression.Span = new Span(left.Span.Start, _curToken.Span.End);

        return expression;
    }

}
