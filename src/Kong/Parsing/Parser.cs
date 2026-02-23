using Kong.Common;
using Kong.Lexing;
using System.Globalization;

namespace Kong.Parsing;

public enum Precedence
{
    Lowest = 0,
    LogicalOr,   // ||
    LogicalAnd,  // &&
    Equality,    // ==
    Comparison,  // > or <
    Sum,         // +
    Product,     // *
    Prefix,      // -X or !X
    FunctionCall, // myFunction(X)
    Index,       // array[index]
    MemberAccess, // object.member
}

public class Parser
{
    private readonly Lexer _lexer;
    private readonly DiagnosticBag _diagnostics;

    private Token _curToken;
    private Token _peekToken;

    private readonly Dictionary<TokenType, Func<IExpression>> _prefixParseFns;
    private readonly Dictionary<TokenType, Func<IExpression, IExpression>> _infixParseFns;
    private int _blockDepth;

    private static readonly Dictionary<TokenType, Precedence> Precedences = new()
    {
        { TokenType.Or, Precedence.LogicalOr },
        { TokenType.And, Precedence.LogicalAnd },
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
        { TokenType.Dot, Precedence.MemberAccess },
    };

    public Parser(Lexer lexer)
    {
        _lexer = lexer;
        _diagnostics = new DiagnosticBag();

        _prefixParseFns = new Dictionary<TokenType, Func<IExpression>>
        {
            { TokenType.Identifier, ParseIdentifier },
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
            { TokenType.LeftParenthesis, ParseGroupedExpression },
            { TokenType.If, ParseIfExpression },
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
        if (_blockDepth == 0 && CurTokenIs(TokenType.Function) && PeekTokenIs(TokenType.Identifier))
        {
            return ParseFunctionDeclaration();
        }

        return _curToken.Type switch
        {
            TokenType.Import => ParseImportStatement(),
            TokenType.Namespace => ParseNamespaceStatement(),
            TokenType.Let => ParseLetStatement(),
            TokenType.Var => ParseVarStatement(),
            TokenType.For => ParseForInStatement(),
            TokenType.Break => ParseBreakStatement(),
            TokenType.Continue => ParseContinueStatement(),
            TokenType.Return => ParseReturnStatement(),
            TokenType.Identifier => ParseIdentifierLedStatement(),
            _ => ParseExpressionStatement(),
        };
    }

    private IStatement? ParseIdentifierLedStatement()
    {
        var startSpan = _curToken.Span;
        var left = ParseExpression(Precedence.Lowest);
        if (left == null)
        {
            return null;
        }

        if (PeekTokenIs(TokenType.Assign))
        {
            NextToken();
            var assignToken = _curToken;
            NextToken();
            var value = ParseExpression(Precedence.Lowest);
            if (value == null)
            {
                return null;
            }

            if (PeekTokenIs(TokenType.Semicolon))
            {
                NextToken();
            }

            return left switch
            {
                Identifier identifier => new AssignmentStatement
                {
                    Token = identifier.Token,
                    Name = identifier,
                    Value = value,
                    Span = new Span(startSpan.Start, _curToken.Span.End),
                },
                IndexExpression indexExpression => new IndexAssignmentStatement
                {
                    Token = assignToken,
                    Target = indexExpression,
                    Value = value,
                    Span = new Span(startSpan.Start, _curToken.Span.End),
                },
                _ => ReportInvalidAssignmentTarget(startSpan),
            };
        }

        var expressionStatement = new ExpressionStatement
        {
            Token = left is Identifier id ? id.Token : _curToken,
            Expression = left,
            Span = new Span(startSpan.Start, _curToken.Span.End),
        };

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
            expressionStatement.Span = new Span(startSpan.Start, _curToken.Span.End);
        }

        return expressionStatement;
    }

    private IStatement? ReportInvalidAssignmentTarget(Span startSpan)
    {
        _diagnostics.Report(startSpan, "invalid assignment target; expected identifier or array index", "P004");
        return null;
    }

    private BreakStatement ParseBreakStatement()
    {
        var statement = new BreakStatement { Token = _curToken, Span = _curToken.Span };
        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
            statement.Span = new Span(statement.Span.Start, _curToken.Span.End);
        }

        return statement;
    }

    private ContinueStatement ParseContinueStatement()
    {
        var statement = new ContinueStatement { Token = _curToken, Span = _curToken.Span };
        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
            statement.Span = new Span(statement.Span.Start, _curToken.Span.End);
        }

        return statement;
    }

    private ForInStatement? ParseForInStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new ForInStatement { Token = _curToken };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        statement.Iterator = new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };

        if (!ExpectPeek(TokenType.In))
        {
            return null;
        }

        NextToken();
        statement.Iterable = ParseExpression(Precedence.Lowest)!;

        if (!ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        statement.Body = ParseBlockStatement();
        statement.Span = new Span(startSpan.Start, statement.Body.Span.End);
        return statement;
    }

    private LetStatement? ParseVarStatement()
    {
        var statement = ParseLetStatement();
        if (statement != null)
        {
            statement.IsMutable = true;
        }

        return statement;
    }

    private ImportStatement? ParseImportStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new ImportStatement
        {
            Token = _curToken,
        };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var segments = new List<string> { _curToken.Literal };
        while (PeekTokenIs(TokenType.Dot))
        {
            NextToken();
            if (!ExpectPeek(TokenType.Identifier))
            {
                return null;
            }

            segments.Add(_curToken.Literal);
        }

        statement.QualifiedName = string.Join('.', segments);

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
        }

        statement.Span = new Span(startSpan.Start, _curToken.Span.End);
        return statement;
    }

    private NamespaceStatement? ParseNamespaceStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new NamespaceStatement
        {
            Token = _curToken,
        };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var segments = new List<string> { _curToken.Literal };
        while (PeekTokenIs(TokenType.Dot))
        {
            NextToken();
            if (!ExpectPeek(TokenType.Identifier))
            {
                return null;
            }

            segments.Add(_curToken.Literal);
        }

        statement.QualifiedName = string.Join('.', segments);

        if (PeekTokenIs(TokenType.Semicolon))
        {
            NextToken();
        }

        statement.Span = new Span(startSpan.Start, _curToken.Span.End);
        return statement;
    }

    private FunctionDeclaration? ParseFunctionDeclaration()
    {
        var startSpan = _curToken.Span;
        var declaration = new FunctionDeclaration
        {
            Token = _curToken,
        };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        declaration.Name = new Identifier
        {
            Token = _curToken,
            Value = _curToken.Literal,
            Span = _curToken.Span,
        };

        if (!ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        declaration.Parameters = ParseFunctionParameters();

        if (PeekTokenIs(TokenType.Arrow))
        {
            NextToken();
            NextToken();

            declaration.ReturnTypeAnnotation = ParseTypeNode();
            if (declaration.ReturnTypeAnnotation == null)
            {
                return null;
            }
        }
        else
        {
            declaration.ReturnTypeAnnotation = new NamedType
            {
                Token = new Token(TokenType.Identifier, "void"),
                Name = "void",
                Span = declaration.Name.Span,
            };
        }

        if (!ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        declaration.Body = ParseBlockStatement();
        declaration.Span = new Span(startSpan.Start, declaration.Body.Span.End);
        return declaration;
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

    private AssignmentStatement? ParseAssignmentStatement()
    {
        var startSpan = _curToken.Span;
        var statement = new AssignmentStatement
        {
            Token = _curToken,
            Name = new Identifier
            {
                Token = _curToken,
                Value = _curToken.Literal,
                Span = _curToken.Span,
            },
        };

        if (!ExpectPeek(TokenType.Assign))
        {
            return null;
        }

        NextToken();
        statement.Value = ParseExpression(Precedence.Lowest)!;

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
        if (CurTokenIs(TokenType.Function))
        {
            return ParseFunctionTypeNode();
        }

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

    private ITypeNode? ParseFunctionTypeNode()
    {
        var startSpan = _curToken.Span.Start;
        var functionType = new FunctionType
        {
            Token = _curToken,
        };

        if (!ExpectPeek(TokenType.LeftParenthesis))
        {
            return null;
        }

        if (!PeekTokenIs(TokenType.RightParenthesis))
        {
            NextToken();
            var firstParameterType = ParseTypeNode();
            if (firstParameterType == null)
            {
                return null;
            }

            functionType.ParameterTypes.Add(firstParameterType);

            while (PeekTokenIs(TokenType.Comma))
            {
                NextToken();
                NextToken();

                var nextParameterType = ParseTypeNode();
                if (nextParameterType == null)
                {
                    return null;
                }

                functionType.ParameterTypes.Add(nextParameterType);
            }
        }

        if (!ExpectPeek(TokenType.RightParenthesis))
        {
            return null;
        }

        if (!ExpectPeek(TokenType.Arrow))
        {
            return null;
        }

        NextToken();
        var returnType = ParseTypeNode();
        if (returnType == null)
        {
            return null;
        }

        functionType.ReturnType = returnType;
        functionType.Span = new Span(startSpan, returnType.Span.End);
        return functionType;
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

        _blockDepth++;
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
        _blockDepth--;

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

    private IExpression ParseDoubleLiteral()
    {
        var literal = new DoubleLiteral
        {
            Token = _curToken,
            Span = _curToken.Span,
        };

        if (!double.TryParse(_curToken.Literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _diagnostics.Report(_curToken.Span, $"could not parse \"{_curToken.Literal}\" as double", "P003");
            return null!;
        }

        literal.Value = value;
        return literal;
    }

    private IExpression ParseCharLiteral()
    {
        var literal = new CharLiteral
        {
            Token = _curToken,
            Span = _curToken.Span,
        };

        if (_curToken.Literal.Length != 1)
        {
            _diagnostics.Report(_curToken.Span, $"could not parse \"{_curToken.Literal}\" as char", "P003");
            return null!;
        }

        literal.Value = _curToken.Literal[0];
        return literal;
    }

    private IExpression ParseByteLiteral()
    {
        var literal = new ByteLiteral
        {
            Token = _curToken,
            Span = _curToken.Span,
        };

        if (!byte.TryParse(_curToken.Literal, out var value))
        {
            _diagnostics.Report(_curToken.Span, $"could not parse \"{_curToken.Literal}\" as byte", "P003");
            return null!;
        }

        literal.Value = value;
        return literal;
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
            Arguments = ParseCallArguments(),
            // Span from start of function expression to closing ')'
            Span = new Span(function.Span.Start, _curToken.Span.End)
        };
        return exp;
    }

    private IExpression ParseMemberAccessExpression(IExpression obj)
    {
        var start = obj.Span.Start;
        var dotToken = _curToken;

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null!;
        }

        return new MemberAccessExpression
        {
            Token = dotToken,
            Object = obj,
            Member = _curToken.Literal,
            Span = new Span(start, _curToken.Span.End),
        };
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
            // Span from '[' to ']'
            Span = new Span(startSpan.Start, _curToken.Span.End)
        };
        return array;
    }

    private IExpression ParseNewExpression()
    {
        var startSpan = _curToken.Span;
        var expression = new NewExpression
        {
            Token = _curToken,
        };

        if (!ExpectPeek(TokenType.Identifier))
        {
            return null!;
        }

        var segments = new List<string> { _curToken.Literal };
        while (PeekTokenIs(TokenType.Dot))
        {
            NextToken();
            if (!ExpectPeek(TokenType.Identifier))
            {
                return null!;
            }

            segments.Add(_curToken.Literal);
        }

        expression.TypePath = string.Join('.', segments);

        if (!ExpectPeek(TokenType.LeftParenthesis))
        {
            return null!;
        }

        expression.Arguments = ParseExpressionList(TokenType.RightParenthesis);
        expression.Span = new Span(startSpan.Start, _curToken.Span.End);
        return expression;
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

    private List<CallArgument> ParseCallArguments()
    {
        var arguments = new List<CallArgument>();

        if (PeekTokenIs(TokenType.RightParenthesis))
        {
            NextToken();
            return arguments;
        }

        NextToken();
        var first = ParseCallArgument();
        if (first == null)
        {
            return null!;
        }

        arguments.Add(first);

        while (PeekTokenIs(TokenType.Comma))
        {
            NextToken();
            NextToken();
            var argument = ParseCallArgument();
            if (argument == null)
            {
                return null!;
            }

            arguments.Add(argument);
        }

        if (!ExpectPeek(TokenType.RightParenthesis))
        {
            return null!;
        }

        return arguments;
    }

    private CallArgument? ParseCallArgument()
    {
        var modifier = CallArgumentModifier.None;
        var token = _curToken;
        if (CurTokenIs(TokenType.Out))
        {
            modifier = CallArgumentModifier.Out;
            NextToken();
        }
        else if (CurTokenIs(TokenType.Ref))
        {
            modifier = CallArgumentModifier.Ref;
            NextToken();
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
