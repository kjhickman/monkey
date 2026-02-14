using Kong.Ast;
using Kong.Diagnostics;
using Kong.Token;

namespace Kong.Parser;

public enum Precedence
{
    Lowest = 0,
    Equals,      // ==
    LessGreater, // > or <
    Sum,         // +
    Product,     // *
    Prefix,      // -X or !X
    Call,        // myFunction(X)
    Index,       // array[index]
}

public class Parser
{
    private readonly Lexer.Lexer _lexer;
    private readonly DiagnosticBag _diagnostics;

    private Token.Token _curToken;
    private Token.Token _peekToken;

    private readonly Dictionary<TokenType, Func<IExpression>> _prefixParseFns;
    private readonly Dictionary<TokenType, Func<IExpression, IExpression>> _infixParseFns;

    private static readonly Dictionary<TokenType, Precedence> Precedences = new()
    {
        { TokenType.Eq, Precedence.Equals },
        { TokenType.NotEq, Precedence.Equals },
        { TokenType.Lt, Precedence.LessGreater },
        { TokenType.Gt, Precedence.LessGreater },
        { TokenType.Plus, Precedence.Sum },
        { TokenType.Minus, Precedence.Sum },
        { TokenType.Slash, Precedence.Product },
        { TokenType.Asterisk, Precedence.Product },
        { TokenType.LParen, Precedence.Call },
        { TokenType.LBracket, Precedence.Index },
    };

    public Parser(Lexer.Lexer lexer)
    {
        _lexer = lexer;
        _diagnostics = new DiagnosticBag();

        _prefixParseFns = new Dictionary<TokenType, Func<IExpression>>
        {
            { TokenType.Ident, ParseIdentifier },
            { TokenType.Int, ParseIntegerLiteral },
            { TokenType.String, ParseStringLiteral },
            { TokenType.Function, ParseFunctionLiteral },
            { TokenType.True, ParseBoolean },
            { TokenType.False, ParseBoolean },
            { TokenType.Bang, ParsePrefixExpression },
            { TokenType.Minus, ParsePrefixExpression },
            { TokenType.LParen, ParseGroupedExpression },
            { TokenType.If, ParseIfExpression },
            { TokenType.LBracket, ParseArrayLiteral },
            { TokenType.LBrace, ParseHashLiteral },
        };

        _infixParseFns = new Dictionary<TokenType, Func<IExpression, IExpression>>
        {
            { TokenType.Plus, ParseInfixExpression },
            { TokenType.Minus, ParseInfixExpression },
            { TokenType.Asterisk, ParseInfixExpression },
            { TokenType.Slash, ParseInfixExpression },
            { TokenType.Eq, ParseInfixExpression },
            { TokenType.NotEq, ParseInfixExpression },
            { TokenType.Lt, ParseInfixExpression },
            { TokenType.Gt, ParseInfixExpression },
            { TokenType.LParen, ParseCallExpression },
            { TokenType.LBracket, ParseIndexExpression },
        };

        // Read two tokens, so _curToken and _peekToken are both set
        NextToken();
        NextToken();
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    public Ast.Program ParseProgram()
    {
        var program = new Ast.Program();
        var start = _curToken.Span.Start;

        while (!CurTokenIs(TokenType.Eof))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                program.Statements.Add(stmt);
            }
            NextToken();
        }

        if (program.Statements.Count > 0)
        {
            program.Span = new Span(start, program.Statements[^1].Span.End);
        }

        return program;
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

        if (!ExpectPeek(TokenType.Ident))
        {
            return null;
        }

        statement.Name = new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };

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

        while (!CurTokenIs(TokenType.RBrace) && !CurTokenIs(TokenType.Eof))
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

        if (!ExpectPeek(TokenType.LParen))
        {
            return null!;
        }

        literal.Parameters = ParseFunctionParameters();

        if (!ExpectPeek(TokenType.LBrace))
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

        if (!ExpectPeek(TokenType.RParen))
        {
            return null!;
        }

        return expression!;
    }

    private IExpression ParseIfExpression()
    {
        var startSpan = _curToken.Span;
        var expression = new IfExpression { Token = _curToken };

        if (!ExpectPeek(TokenType.LParen))
        {
            return null!;
        }

        NextToken();
        expression.Condition = ParseExpression(Precedence.Lowest)!;

        if (!ExpectPeek(TokenType.RParen) || !ExpectPeek(TokenType.LBrace))
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
        if (!ExpectPeek(TokenType.LBrace))
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
            Arguments = ParseExpressionList(TokenType.RParen),
        };
        // Span from start of function expression to closing ')'
        exp.Span = new Span(function.Span.Start, _curToken.Span.End);
        return exp;
    }

    private List<Identifier> ParseFunctionParameters()
    {
        var identifiers = new List<Identifier>();

        if (PeekTokenIs(TokenType.RParen))
        {
            NextToken();
            return identifiers;
        }

        NextToken();

        var identifier = new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };
        identifiers.Add(identifier);

        while (PeekTokenIs(TokenType.Comma))
        {
            NextToken();
            NextToken();
            identifier = new Identifier
            {
                Token = _curToken,
                Value = _curToken.Literal,
                Span = _curToken.Span,
            };
            identifiers.Add(identifier);
        }

        if (!ExpectPeek(TokenType.RParen))
        {
            return null!;
        }

        return identifiers;
    }

    private IExpression ParseArrayLiteral()
    {
        var startSpan = _curToken.Span;
        var array = new ArrayLiteral
        {
            Token = _curToken,
            Elements = ParseExpressionList(TokenType.RBracket),
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

        if (!ExpectPeek(TokenType.RBracket))
        {
            return null!;
        }

        // Span from start of left expression to closing ']'
        expression.Span = new Span(left.Span.Start, _curToken.Span.End);

        return expression;
    }

    private IExpression ParseHashLiteral()
    {
        var startSpan = _curToken.Span;
        var hash = new HashLiteral { Token = _curToken };

        while (!PeekTokenIs(TokenType.RBrace))
        {
            NextToken();
            var key = ParseExpression(Precedence.Lowest)!;

            if (!ExpectPeek(TokenType.Colon))
            {
                return null!;
            }

            NextToken();
            var value = ParseExpression(Precedence.Lowest)!;

            hash.Pairs.Add(new KeyValuePair<IExpression, IExpression>(key, value));

            if (!PeekTokenIs(TokenType.RBrace) && !ExpectPeek(TokenType.Comma))
            {
                return null!;
            }
        }

        if (!ExpectPeek(TokenType.RBrace))
        {
            return null!;
        }

        // Span from '{' to '}'
        hash.Span = new Span(startSpan.Start, _curToken.Span.End);

        return hash;
    }
}
