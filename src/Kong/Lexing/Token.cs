using Kong.Common;

namespace Kong.Lexing;

public enum TokenType
{
    Illegal,
    EndOfFile,

    // Identifiers + literals
    Identifier,
    Integer,
    Double,
    Char,
    Byte,
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
    And,
    Or,

    // Delimiters
    Comma,
    Dot,
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
    Var,
    True,
    False,
    If,
    Else,
    Break,
    Continue,
    Return,
    Import,
    Namespace,
    New,
    Out,
    Ref,
    For,
    In,
    Public,
    Enum,
    Match,
    Class,
    Interface,
    Impl,
    Self,
    Init,
}

public record struct Token(TokenType Type, string Literal, Span Span = default)
{
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "fn", TokenType.Function },
        { "let", TokenType.Let },
        { "var", TokenType.Var },
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "break", TokenType.Break },
        { "continue", TokenType.Continue },
        { "return", TokenType.Return },
        { "use", TokenType.Import },
        { "module", TokenType.Namespace },
        { "new", TokenType.New },
        { "out", TokenType.Out },
        { "ref", TokenType.Ref },
        { "for", TokenType.For },
        { "in", TokenType.In },
        { "public", TokenType.Public },
        { "enum", TokenType.Enum },
        { "match", TokenType.Match },
        { "class", TokenType.Class },
        { "interface", TokenType.Interface },
        { "impl", TokenType.Impl },
        { "self", TokenType.Self },
        { "init", TokenType.Init },
    };

    public static TokenType LookupIdentifier(string keyword)
    {
        return Keywords.GetValueOrDefault(keyword, TokenType.Identifier);
    }
}
