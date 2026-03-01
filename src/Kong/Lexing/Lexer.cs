namespace Kong.Lexing;

public class Lexer
{
    private readonly string _input;
    private int _position;     // current position in input (points to current char)
    private int _readPosition; // current reading position in input (after current char)
    private char _ch;          // current character under examination
    private int _line = 1;
    private int _column;

    public Lexer(string input)
    {
        _input = input;
        ReadChar();
    }

    public Token NextToken()
    {
        Token tok;

        SkipWhitespace();

        var line = _line;
        var col = _column;

        switch (_ch)
        {
            case '=':
                if (PeekChar() == '=')
                {
                    var ch = _ch;
                    ReadChar();
                    var literal = $"{ch}{_ch}";
                    tok = new Token(TokenType.Eq, literal, line, col);
                }
                else
                {
                    tok = NewToken(TokenType.Assign, _ch, line, col);
                }
                break;
            case '+':
                tok = NewToken(TokenType.Plus, _ch, line, col);
                break;
            case '-':
                tok = NewToken(TokenType.Minus, _ch, line, col);
                break;
            case '!':
                if (PeekChar() == '=')
                {
                    var ch = _ch;
                    ReadChar();
                    var literal = $"{ch}{_ch}";
                    tok = new Token(TokenType.NotEq, literal, line, col);
                }
                else
                {
                    tok = NewToken(TokenType.Bang, _ch, line, col);
                }
                break;
            case '/':
                if (PeekChar() == '/')
                {
                    SkipLineComment();
                    return NextToken();
                }

                tok = NewToken(TokenType.Slash, _ch, line, col);
                break;
            case '*':
                tok = NewToken(TokenType.Asterisk, _ch, line, col);
                break;
            case '<':
                tok = NewToken(TokenType.Lt, _ch, line, col);
                break;
            case '>':
                tok = NewToken(TokenType.Gt, _ch, line, col);
                break;
            case ';':
                tok = NewToken(TokenType.Semicolon, _ch, line, col);
                break;
            case ',':
                tok = NewToken(TokenType.Comma, _ch, line, col);
                break;
            case ':':
                tok = NewToken(TokenType.Colon, _ch, line, col);
                break;
            case '(':
                tok = NewToken(TokenType.LParen, _ch, line, col);
                break;
            case ')':
                tok = NewToken(TokenType.RParen, _ch, line, col);
                break;
            case '{':
                tok = NewToken(TokenType.LBrace, _ch, line, col);
                break;
            case '}':
                tok = NewToken(TokenType.RBrace, _ch, line, col);
                break;
            case '[':
                tok = NewToken(TokenType.LBracket, _ch, line, col);
                break;
            case ']':
                tok = NewToken(TokenType.RBracket, _ch, line, col);
                break;
            case '"':
                tok = new Token(TokenType.String, ReadString(), line, col);
                break;
            case '\'':
                tok = new Token(TokenType.Char, ReadCharLiteral(), line, col);
                break;
            case '\0':
                tok = new Token(TokenType.Eof, "", line, col);
                break;
            default:
                if (IsLetter(_ch))
                {
                    var literal = ReadIdentifier();
                    var type = Token.LookupIdent(literal);
                    return new Token(type, literal, line, col);
                }

                if (IsDigit(_ch))
                {
                    var (literal, isFloat) = ReadNumber();
                    return new Token(isFloat ? TokenType.Float : TokenType.Int, literal, line, col);
                }

                tok = NewToken(TokenType.Illegal, _ch, line, col);
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
        if (_ch == '\n')
        {
            _line++;
            _column = 0;
        }

        _ch = _readPosition >= _input.Length ? '\0' : _input[_readPosition];
        _position = _readPosition;
        _readPosition++;
        _column++;
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

    private (string literal, bool isFloat) ReadNumber()
    {
        var position = _position;
        while (IsDigit(_ch))
        {
            ReadChar();
        }

        if (_ch == '.' && IsDigit(PeekChar()))
        {
            ReadChar(); // consume '.'
            while (IsDigit(_ch))
            {
                ReadChar();
            }
            return (_input[position.._position], true);
        }

        return (_input[position.._position], false);
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

    private string ReadCharLiteral()
    {
        ReadChar();
        var ch = _ch;
        ReadChar(); // consume closing '
        return ch.ToString();
    }

    private static Token NewToken(TokenType type, char ch, int line, int column)
    {
        return new Token(type, ch.ToString(), line, column);
    }

    private void SkipLineComment()
    {
        while (_ch is not '\n' and not '\r' and not '\0')
        {
            ReadChar();
        }
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
