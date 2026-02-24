using Kong.Common;
using Kong.Lexing;
using System.Globalization;

namespace Kong.Parsing;

internal sealed class StatementParser
{
    private readonly ParserContext _context;
    private DeclarationParser _declarationParser = null!;
    private ExpressionParser _expressionParser = null!;
    private TypeParser _typeParser = null!;

    internal StatementParser(ParserContext context)
    {
        _context = context;
    }

    internal void Initialize(DeclarationParser declarationParser, ExpressionParser expressionParser, TypeParser typeParser)
    {
        _declarationParser = declarationParser;
        _expressionParser = expressionParser;
        _typeParser = typeParser;
    }

    internal IStatement? ParseStatement()
    {
        if (_context.CurTokenIs(TokenType.Public))
        {
            return ParsePublicTopLevelDeclaration();
        }

        return _context.CurToken.Type switch
        {
            TokenType.Function when _context.BlockDepth == 0 && _context.PeekTokenIs(TokenType.Identifier) => _declarationParser.ParseFunctionDeclaration(),
            TokenType.Import => ParseImportStatement(),
            TokenType.Namespace => ParseNamespaceStatement(),
            TokenType.Enum when _context.BlockDepth == 0 => _declarationParser.ParseEnumDeclaration(),
            TokenType.Class when _context.BlockDepth == 0 => _declarationParser.ParseClassDeclaration(),
            TokenType.Interface when _context.BlockDepth == 0 => _declarationParser.ParseInterfaceDeclaration(),
            TokenType.Impl when _context.BlockDepth == 0 => _declarationParser.ParseImplBlock(),
            TokenType.Let => ParseLetStatement(),
            TokenType.Var => ParseVarStatement(),
            TokenType.For => ParseForInStatement(),
            TokenType.While => ParseWhileStatement(),
            TokenType.Break => ParseBreakStatement(),
            TokenType.Continue => ParseContinueStatement(),
            TokenType.Return => ParseReturnStatement(),
            TokenType.Identifier => ParseIdentifierLedStatement(),
            TokenType.Self => ParseIdentifierLedStatement(),
            _ => ParseExpressionStatement(),
        };
    }

    internal IStatement? ParsePublicTopLevelDeclaration()
    {
        if (_context.BlockDepth == 0 && _context.PeekTokenIs(TokenType.Function))
        {
            return _declarationParser.ParseFunctionDeclaration();
        }

        if (_context.BlockDepth == 0 && _context.PeekTokenIs(TokenType.Class))
        {
            _context.NextToken();
            return _declarationParser.ParseClassDeclaration(isPublic: true);
        }

        if (_context.BlockDepth == 0 && _context.PeekTokenIs(TokenType.Interface))
        {
            _context.NextToken();
            return _declarationParser.ParseInterfaceDeclaration(isPublic: true);
        }

        _context.Diagnostics.Report(_context.CurToken.Span,
            "'public' is only allowed on top-level function/class/interface declarations and impl methods",
            "P006");
        return null;
    }

    internal IStatement? ParseIdentifierLedStatement()
    {
        var startSpan = _context.CurToken.Span;
        var left = _expressionParser.ParseExpression(Precedence.Lowest);
        if (left == null)
        {
            return null;
        }

        if (_context.PeekTokenIs(TokenType.Assign))
        {
            _context.NextToken();
            var assignToken = _context.CurToken;
            _context.NextToken();
            var value = _expressionParser.ParseExpression(Precedence.Lowest);
            if (value == null)
            {
                return null;
            }

            return left switch
            {
                Identifier identifier => new AssignmentStatement
                {
                    Token = identifier.Token,
                    Name = identifier,
                    Value = value,
                    Span = new Span(startSpan.Start, _context.CurToken.Span.End),
                },
                IndexExpression indexExpression => new IndexAssignmentStatement
                {
                    Token = assignToken,
                    Target = indexExpression,
                    Value = value,
                    Span = new Span(startSpan.Start, _context.CurToken.Span.End),
                },
                MemberAccessExpression memberAccessExpression => new MemberAssignmentStatement
                {
                    Token = assignToken,
                    Target = memberAccessExpression,
                    Value = value,
                    Span = new Span(startSpan.Start, _context.CurToken.Span.End),
                },
                _ => ReportInvalidAssignmentTarget(startSpan),
            };
        }

        var expressionStatement = new ExpressionStatement
        {
            Token = left is Identifier id ? id.Token : _context.CurToken,
            Expression = left,
            Span = new Span(startSpan.Start, _context.CurToken.Span.End),
        };

        return expressionStatement;
    }

    internal IStatement? ReportInvalidAssignmentTarget(Span startSpan)
    {
        _context.Diagnostics.Report(startSpan, "invalid assignment target; expected identifier, member access, or array index", "P004");
        return null;
    }

    internal BreakStatement ParseBreakStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new BreakStatement { Token = _context.CurToken, Span = _context.CurToken.Span };
        if (_context.LoopExpressionDepth > 0 && _expressionParser.CanParsePrefix(_context.PeekToken.Type))
        {
            _context.NextToken();
            statement.Value = _expressionParser.ParseExpression(Precedence.Lowest);
            if (statement.Value != null)
            {
                statement.Span = new Span(startSpan.Start, statement.Value.Span.End);
            }
        }

        return statement;
    }

    internal ContinueStatement ParseContinueStatement()
    {
        var statement = new ContinueStatement { Token = _context.CurToken, Span = _context.CurToken.Span };
        return statement;
    }

    internal ForInStatement? ParseForInStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new ForInStatement { Token = _context.CurToken };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        statement.Iterator = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (!_context.ExpectPeek(TokenType.In))
        {
            return null;
        }

        _context.NextToken();
        statement.Iterable = _expressionParser.ParseExpression(Precedence.Lowest)!;

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        statement.Body = ParseBlockStatement();
        statement.Span = new Span(startSpan.Start, statement.Body.Span.End);
        return statement;
    }

    internal WhileStatement? ParseWhileStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new WhileStatement { Token = _context.CurToken };

        _context.NextToken();
        var condition = _expressionParser.ParseExpression(Precedence.Lowest);
        if (condition == null)
        {
            return null;
        }

        statement.Condition = condition;

        if (!_context.ExpectPeek(TokenType.LeftBrace))
        {
            return null;
        }

        statement.Body = ParseBlockStatement();
        statement.Span = new Span(startSpan.Start, statement.Body.Span.End);
        return statement;
    }

    internal LetStatement? ParseVarStatement()
    {
        var statement = ParseLetStatement();
        if (statement != null)
        {
            statement.IsMutable = true;
        }

        return statement;
    }

    internal ImportStatement? ParseImportStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new ImportStatement
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var segments = new List<string> { _context.CurToken.Literal };
        while (_context.PeekTokenIs(TokenType.Dot))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return null;
            }

            segments.Add(_context.CurToken.Literal);
        }

        statement.QualifiedName = string.Join('.', segments);

        statement.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return statement;
    }

    internal NamespaceStatement? ParseNamespaceStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new NamespaceStatement
        {
            Token = _context.CurToken,
        };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        var segments = new List<string> { _context.CurToken.Literal };
        while (_context.PeekTokenIs(TokenType.Dot))
        {
            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.Identifier))
            {
                return null;
            }

            segments.Add(_context.CurToken.Literal);
        }

        statement.QualifiedName = string.Join('.', segments);

        statement.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return statement;
    }

    internal LetStatement? ParseLetStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new LetStatement { Token = _context.CurToken };

        if (!_context.ExpectPeek(TokenType.Identifier))
        {
            return null;
        }

        statement.Name = new Identifier
        {
            Token = _context.CurToken,
            Value = _context.CurToken.Literal,
            Span = _context.CurToken.Span,
        };

        if (_context.PeekTokenIs(TokenType.Colon))
        {
            _context.NextToken();
            _context.NextToken();

            statement.TypeAnnotation = _typeParser.ParseTypeNode();
            if (statement.TypeAnnotation == null)
            {
                return null;
            }
        }

        if (!_context.ExpectPeek(TokenType.Assign))
        {
            return null;
        }

        _context.NextToken();

        statement.Value = _expressionParser.ParseExpression(Precedence.Lowest);

        if (statement.Value is FunctionLiteral fl)
        {
            fl.Name = statement.Name.Value;
        }

        statement.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return statement;
    }

    internal ReturnStatement ParseReturnStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new ReturnStatement { Token = _context.CurToken };

        _context.NextToken();

        statement.ReturnValue = _expressionParser.ParseExpression(Precedence.Lowest);

        statement.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return statement;
    }

    internal ExpressionStatement ParseExpressionStatement()
    {
        var startSpan = _context.CurToken.Span;
        var statement = new ExpressionStatement
        {
            Token = _context.CurToken,
            Expression = _expressionParser.ParseExpression(Precedence.Lowest),
        };

        statement.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return statement;
    }

    internal BlockStatement ParseBlockStatement()
    {
        var startSpan = _context.CurToken.Span;
        var block = new BlockStatement { Token = _context.CurToken };

        _context.BlockDepth++;
        _context.NextToken();

        while (!_context.CurTokenIs(TokenType.RightBrace) && !_context.CurTokenIs(TokenType.EndOfFile))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                block.Statements.Add(stmt);
            }
            _context.NextToken();
        }
        _context.BlockDepth--;

        block.Span = new Span(startSpan.Start, _context.CurToken.Span.End);
        return block;
    }
}
