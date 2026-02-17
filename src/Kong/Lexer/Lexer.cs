using Kong.Token;

namespace Kong.Lexer;

public class Lexer
{
    private readonly string _input;
    private int _position;     // current position in input (points to current char)
    private int _readPosition; // current reading position in input (after current char)
    private char _ch;          // current character under examination

    private int _line = 1;     // line number of _ch (1-based)
    private int _column;       // column number of _ch (1-based, set by ReadChar)

    public Lexer(string input)
    {
        _input = input;
        ReadChar();
    }

    public Token.Token NextToken()
    {
        Token.Token tok;

        SkipWhitespace();

        // Snapshot the start position before consuming the token.
        var start = new Position(_line, _column);

        switch (_ch)
        {
            case '=':
                if (PeekChar() == '=')
                {
                    ReadChar();
                    tok = new Token.Token(TokenType.Equal, "==",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    tok = NewToken(TokenType.Assign, _ch, start);
                }
                break;
            case '+':
                tok = NewToken(TokenType.Plus, _ch, start);
                break;
            case '-':
                tok = NewToken(TokenType.Minus, _ch, start);
                break;
            case '!':
                if (PeekChar() == '=')
                {
                    ReadChar();
                    tok = new Token.Token(TokenType.NotEqual, "!=",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    tok = NewToken(TokenType.Bang, _ch, start);
                }
                break;
            case '/':
                tok = NewToken(TokenType.Slash, _ch, start);
                break;
            case '*':
                tok = NewToken(TokenType.Asterisk, _ch, start);
                break;
            case '<':
                tok = NewToken(TokenType.LessThan, _ch, start);
                break;
            case '>':
                tok = NewToken(TokenType.GreaterThan, _ch, start);
                break;
            case ';':
                tok = NewToken(TokenType.Semicolon, _ch, start);
                break;
            case ',':
                tok = NewToken(TokenType.Comma, _ch, start);
                break;
            case ':':
                tok = NewToken(TokenType.Colon, _ch, start);
                break;
            case '(':
                tok = NewToken(TokenType.LeftParenthesis, _ch, start);
                break;
            case ')':
                tok = NewToken(TokenType.RightParenthesis, _ch, start);
                break;
            case '{':
                tok = NewToken(TokenType.LeftBrace, _ch, start);
                break;
            case '}':
                tok = NewToken(TokenType.RightBrace, _ch, start);
                break;
            case '[':
                tok = NewToken(TokenType.LeftBracket, _ch, start);
                break;
            case ']':
                tok = NewToken(TokenType.RightBracket, _ch, start);
                break;
            case '"':
            {
                var value = ReadString();
                // After ReadString, _ch is the closing quote. End is one past it.
                var end = new Position(_line, _column + 1);
                tok = new Token.Token(TokenType.String, value, new Span(start, end));
                break;
            }
            case '\0':
                tok = new Token.Token(TokenType.EndOfFile, "", new Span(start, start));
                break;
            default:
                if (IsLetter(_ch))
                {
                    var literal = ReadIdentifier();
                    var type = Token.Token.LookupIdent(literal);
                    // After ReadIdentifier, _ch is the first non-letter char.
                    // _column points to that char, so the identifier ended at _column - 1.
                    // End column is one past the last char of the identifier.
                    var end = new Position(_line, _column);
                    return new Token.Token(type, literal, new Span(start, end));
                }

                if (IsDigit(_ch))
                {
                    var literal = ReadNumber();
                    var end = new Position(_line, _column);
                    return new Token.Token(TokenType.Integer, literal, new Span(start, end));
                }

                tok = NewToken(TokenType.Illegal, _ch, start);
                break;
        }

        ReadChar();
        return tok;
    }

    private void SkipWhitespace()
    {
        while (_ch is ' ' or '\t' or '\n' or '\r')
        {
            ReadChar();
        }
    }

    private void ReadChar()
    {
        // Track newlines: if the current character (about to be left behind) is '\n',
        // the next character starts a new line.
        var advancingPastNewline = _ch == '\n' && _position < _input.Length;

        _ch = _readPosition >= _input.Length ? '\0' : _input[_readPosition];
        _position = _readPosition;
        _readPosition++;

        if (advancingPastNewline)
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
    }

    private char PeekChar()
    {
        return _readPosition >= _input.Length ? '\0' : _input[_readPosition];
    }

    private string ReadIdentifier()
    {
        var position = _position;
        while (IsLetter(_ch))
        {
            ReadChar();
        }
        return _input[position.._position];
    }

    private string ReadNumber()
    {
        var position = _position;
        while (IsDigit(_ch))
        {
            ReadChar();
        }
        return _input[position.._position];
    }

    private string ReadString()
    {
        var position = _position + 1;
        while (true)
        {
            ReadChar();
            if (_ch is '"' or '\0')
            {
                break;
            }
        }
        return _input[position.._position];
    }

    private static Token.Token NewToken(TokenType type, char ch, Position start)
    {
        return new Token.Token(type, ch.ToString(),
            new Span(start, new Position(start.Line, start.Column + 1)));
    }

    private static bool IsLetter(char ch)
    {
        return ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    }

    private static bool IsDigit(char ch)
    {
        return ch is >= '0' and <= '9';
    }
}
