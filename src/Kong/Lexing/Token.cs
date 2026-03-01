namespace Kong.Lexing;

public enum TokenType
{
    Illegal,
    Eof,

    // Identifiers + literals
    Ident,
    Int,
    Float,
    String,
    Char,

    // Operators
    Assign,
    Plus,
    Minus,
    Bang,
    Asterisk,
    Slash,
    Percent,

    Lt,
    Gt,

    Eq,
    NotEq,

    // Delimiters
    Comma,
    Semicolon,
    Colon,

    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,

    // Keywords
    Function,
    Let,
    True,
    False,
    If,
    Else,
    Return,
}

public record struct Token(TokenType Type, string Literal, int Line = 0, int Column = 0)
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
    };

    public static TokenType LookupIdent(string ident)
    {
        return Keywords.GetValueOrDefault(ident, TokenType.Ident);
    }
}
