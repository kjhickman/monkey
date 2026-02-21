using Kong.Common;

namespace Kong.Lexing;

public enum TokenType
{
    Illegal,
    EndOfFile,

    // Identifiers + literals
    Identifier,
    Integer,
    String,

    // Operators
    Assign,
    Plus,
    Minus,
    Arrow,
    Bang,
    Asterisk,
    Slash,

    LessThan,
    GreaterThan,

    Equal,
    NotEqual,

    // Delimiters
    Comma,
    Dot,
    Semicolon,
    Colon,

    LeftParenthesis,
    RightParenthesis,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,

    // Keywords
    Function,
    Let,
    True,
    False,
    If,
    Else,
    Return,
    Import,
    Namespace,
}

public record struct Token(TokenType Type, string Literal, Span Span = default)
{
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "fn", TokenType.Function },
        { "let", TokenType.Let },
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "return", TokenType.Return },
        { "import", TokenType.Import },
        { "namespace", TokenType.Namespace },
    };

    public static TokenType LookupIdentifier(string keyword)
    {
        return Keywords.GetValueOrDefault(keyword, TokenType.Identifier);
    }
}
