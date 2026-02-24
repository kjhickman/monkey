using Kong.Common;
using Kong.Lexing;
using System.Globalization;

namespace Kong.Parsing;

internal sealed class TypeParser
{
    private readonly ParserContext _context;

    internal TypeParser(ParserContext context)
    {
        _context = context;
    }

    internal ITypeNode? ParseTypeNode()
    {
        ITypeNode? type = ParseTypePrimary();
        if (type == null)
        {
            return null;
        }

        while (_context.PeekTokenIs(TokenType.LeftBracket))
        {
            var start = type.Span.Start;

            _context.NextToken();
            if (!_context.ExpectPeek(TokenType.RightBracket))
            {
                return null;
            }

            type = new ArrayType
            {
                Token = _context.CurToken,
                ElementType = type,
                Span = new Span(start, _context.CurToken.Span.End),
            };
        }

        return type;
    }

    internal ITypeNode? ParseTypePrimary()
    {
        if (_context.CurTokenIs(TokenType.LeftParenthesis))
        {
            var start = _context.CurToken.Span.Start;
            return ParseLambdaTypeNode(start);
        }

        if (!_context.CurTokenIs(TokenType.Identifier))
        {
            _context.Diagnostics.Report(_context.CurToken.Span, $"expected type name, got {_context.CurToken.Type}", "P004");
            return null;
        }

        var identifierToken = _context.CurToken;
        var startSpan = _context.CurToken.Span;
        var typeName = _context.CurToken.Literal;
        if (!_context.PeekTokenIs(TokenType.LessThan))
        {
            return new NamedType
            {
                Token = _context.CurToken,
                Name = typeName,
                Span = startSpan,
            };
        }

        _context.NextToken();
        var typeArguments = ParseTypeArgumentList();
        if (typeArguments.Count == 0)
        {
            return null;
        }

        return new GenericType
        {
            Token = identifierToken,
            Name = typeName,
            TypeArguments = typeArguments,
            Span = new Span(startSpan.Start, _context.CurToken.Span.End),
        };
    }

    internal List<ITypeNode> ParseTypeArgumentList()
    {
        var typeArguments = new List<ITypeNode>();

        _context.NextToken();
        var firstType = ParseTypeNode();
        if (firstType == null)
        {
            return [];
        }

        typeArguments.Add(firstType);

        while (_context.PeekTokenIs(TokenType.Comma))
        {
            _context.NextToken();
            _context.NextToken();

            var nextType = ParseTypeNode();
            if (nextType == null)
            {
                return [];
            }

            typeArguments.Add(nextType);
        }

        if (!_context.ExpectPeek(TokenType.GreaterThan))
        {
            return [];
        }

        return typeArguments;
    }

    internal ITypeNode? ParseFunctionTypeNode()
    {
        _context.Diagnostics.Report(_context.CurToken.Span,
            "function type literals must use '(T1, T2) -> T' syntax",
            "P004");
        return null;
    }

    internal ITypeNode? ParseLambdaTypeNode(Position startSpan)
    {
        var functionType = new FunctionType
        {
            Token = _context.CurToken,
        };

        if (!_context.PeekTokenIs(TokenType.RightParenthesis))
        {
            _context.NextToken();
            var firstParameterType = ParseTypeNode();
            if (firstParameterType == null)
            {
                return null;
            }

            functionType.ParameterTypes.Add(firstParameterType);

            while (_context.PeekTokenIs(TokenType.Comma))
            {
                _context.NextToken();
                _context.NextToken();

                var nextParameterType = ParseTypeNode();
                if (nextParameterType == null)
                {
                    return null;
                }

                functionType.ParameterTypes.Add(nextParameterType);
            }
        }

        if (!_context.ExpectPeek(TokenType.RightParenthesis))
        {
            return null;
        }

        if (!_context.ExpectPeek(TokenType.Arrow))
        {
            return null;
        }

        _context.NextToken();
        var returnType = ParseTypeNode();
        if (returnType == null)
        {
            return null;
        }

        functionType.ReturnType = returnType;
        functionType.Span = new Span(startSpan, returnType.Span.End);
        return functionType;
    }

}
