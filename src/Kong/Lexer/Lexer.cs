using Kong.Token;

namespace Kong.Lexer;

public class Lexer
{
    private readonly string _input;
    private int _position;     // current position in input (points to current char)
    private int _readPosition; // current reading position in input (after current char)
    private char _ch;          // current character under examination

    public Lexer(string input)
    {
        _input = input;
        ReadChar();
    }

    public Token.Token NextToken()
    {
        Token.Token tok;

        SkipWhitespace();

        switch (_ch)
        {
            case '=':
                if (PeekChar() == '=')
                {
                    var ch = _ch;
                    ReadChar();
                    var literal = $"{ch}{_ch}";
                    tok = new Token.Token(TokenType.Eq, literal);
                }
                else
                {
                    tok = NewToken(TokenType.Assign, _ch);
                }
                break;
            case '+':
                tok = NewToken(TokenType.Plus, _ch);
                break;
            case '-':
                tok = NewToken(TokenType.Minus, _ch);
                break;
            case '!':
                if (PeekChar() == '=')
                {
                    var ch = _ch;
                    ReadChar();
                    var literal = $"{ch}{_ch}";
                    tok = new Token.Token(TokenType.NotEq, literal);
                }
                else
                {
                    tok = NewToken(TokenType.Bang, _ch);
                }
                break;
            case '/':
                tok = NewToken(TokenType.Slash, _ch);
                break;
            case '*':
                tok = NewToken(TokenType.Asterisk, _ch);
                break;
            case '<':
                tok = NewToken(TokenType.Lt, _ch);
                break;
            case '>':
                tok = NewToken(TokenType.Gt, _ch);
                break;
            case ';':
                tok = NewToken(TokenType.Semicolon, _ch);
                break;
            case ',':
                tok = NewToken(TokenType.Comma, _ch);
                break;
            case ':':
                tok = NewToken(TokenType.Colon, _ch);
                break;
            case '(':
                tok = NewToken(TokenType.LParen, _ch);
                break;
            case ')':
                tok = NewToken(TokenType.RParen, _ch);
                break;
            case '{':
                tok = NewToken(TokenType.LBrace, _ch);
                break;
            case '}':
                tok = NewToken(TokenType.RBrace, _ch);
                break;
            case '[':
                tok = NewToken(TokenType.LBracket, _ch);
                break;
            case ']':
                tok = NewToken(TokenType.RBracket, _ch);
                break;
            case '"':
                tok = new Token.Token(TokenType.String, ReadString());
                break;
            case '\0':
                tok = new Token.Token(TokenType.Eof, "");
                break;
            default:
                if (IsLetter(_ch))
                {
                    var literal = ReadIdentifier();
                    var type_ = Token.Token.LookupIdent(literal);
                    return new Token.Token(type_, literal);
                }
                else if (IsDigit(_ch))
                {
                    var literal = ReadNumber();
                    return new Token.Token(TokenType.Int, literal);
                }
                else
                {
                    tok = NewToken(TokenType.Illegal, _ch);
                }
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
        _ch = _readPosition >= _input.Length ? '\0' : _input[_readPosition];
        _position = _readPosition;
        _readPosition++;
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
            if (_ch == '"' || _ch == '\0')
            {
                break;
            }
        }
        return _input[position.._position];
    }

    private static Token.Token NewToken(TokenType type_, char ch)
    {
        return new Token.Token(type_, ch.ToString());
    }

    private static bool IsLetter(char ch)
    {
        return ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';
    }

    private static bool IsDigit(char ch)
    {
        return ch is >= '0' and <= '9';
    }
}
