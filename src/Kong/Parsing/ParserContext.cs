using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

internal sealed class ParserContext
{
    private readonly Lexer _lexer;

    internal ParserContext(Lexer lexer)
    {
        _lexer = lexer;
        Diagnostics = new DiagnosticBag();
        NextToken();
        NextToken();
    }

    internal DiagnosticBag Diagnostics { get; }
    internal Token CurToken { get; private set; }
    internal Token PeekToken { get; private set; }
    internal int BlockDepth { get; set; }
    internal int LoopExpressionDepth { get; set; }

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

    internal void NextToken()
    {
        CurToken = PeekToken;
        PeekToken = _lexer.NextToken();
    }

    internal bool CurTokenIs(TokenType tokenType)
    {
        return CurToken.Type == tokenType;
    }

    internal bool PeekTokenIs(TokenType tokenType)
    {
        return PeekToken.Type == tokenType;
    }

    internal bool ExpectPeek(TokenType tokenType)
    {
        if (PeekTokenIs(tokenType))
        {
            NextToken();
            return true;
        }

        PeekError(tokenType);
        return false;
    }

    internal void NoPrefixParseFnError(TokenType tokenType)
    {
        var message = $"no prefix parse function for {tokenType} found";
        Diagnostics.Report(CurToken.Span, message, "P002");
    }

    internal Precedence PeekPrecedence()
    {
        return Precedences.GetValueOrDefault(PeekToken.Type, Precedence.Lowest);
    }

    internal Precedence CurPrecedence()
    {
        return Precedences.GetValueOrDefault(CurToken.Type, Precedence.Lowest);
    }

    private void PeekError(TokenType tokenType)
    {
        var message = $"expected next token to be {tokenType}, got {PeekToken.Type} instead";
        Diagnostics.Report(PeekToken.Span, message, "P001");
    }
}
