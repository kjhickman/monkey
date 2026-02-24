using Kong.Common;

namespace Kong.Lexing;

public class Lexer
{
    private readonly string _input;
    private readonly int _inputLength;
    private int _position;     // current position in input (points to current char)
    private int _readPosition; // current reading position in input (after current char)
    private char _ch;          // current character under examination

    private int _line = 1;     // line number of _ch (1-based)
    private int _column;       // column number of _ch (1-based, set by ReadChar)

    public Lexer(string input)
    {
        _input = input;
        _inputLength = input.Length;
        ReadChar();
    }

    public Token NextToken()
    {
        Token token;

        SkipIgnored();
        var start = new Position(_line, _column);

        switch (_ch)
        {
            case '=':
                var equalsNext = PeekChar();
                if (equalsNext == '=')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.Equal, "==", start);
                }
                else if (equalsNext == '>')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.DoubleArrow, "=>", start);
                }
                else
                {
                    token = NewSingleCharToken(TokenType.Assign, "=", start);
                }
                break;
            case '+':
                token = NewSingleCharToken(TokenType.Plus, "+", start);
                break;
            case '-':
                if (PeekChar() == '>')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.Arrow, "->", start);
                }
                else
                {
                    token = NewSingleCharToken(TokenType.Minus, "-", start);
                }
                break;
            case '!':
                if (PeekChar() == '=')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.NotEqual, "!=", start);
                }
                else
                {
                    token = NewSingleCharToken(TokenType.Bang, "!", start);
                }
                break;
            case '/':
                token = NewSingleCharToken(TokenType.Slash, "/", start);
                break;
            case '&':
                if (PeekChar() == '&')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.And, "&&", start);
                }
                else
                {
                    token = NewSingleCharToken(TokenType.Illegal, "&", start);
                }
                break;
            case '|':
                if (PeekChar() == '|')
                {
                    ReadChar();
                    token = NewDoubleCharToken(TokenType.Or, "||", start);
                }
                else
                {
                    token = NewSingleCharToken(TokenType.Illegal, "|", start);
                }
                break;
            case '*':
                token = NewSingleCharToken(TokenType.Asterisk, "*", start);
                break;
            case '<':
                token = NewSingleCharToken(TokenType.LessThan, "<", start);
                break;
            case '>':
                token = NewSingleCharToken(TokenType.GreaterThan, ">", start);
                break;
            case ',':
                token = NewSingleCharToken(TokenType.Comma, ",", start);
                break;
            case '.':
                token = NewSingleCharToken(TokenType.Dot, ".", start);
                break;
            case ':':
                token = NewSingleCharToken(TokenType.Colon, ":", start);
                break;
            case '(':
                token = NewSingleCharToken(TokenType.LeftParenthesis, "(", start);
                break;
            case ')':
                token = NewSingleCharToken(TokenType.RightParenthesis, ")", start);
                break;
            case '{':
                token = NewSingleCharToken(TokenType.LeftBrace, "{", start);
                break;
            case '}':
                token = NewSingleCharToken(TokenType.RightBrace, "}", start);
                break;
            case '[':
                token = NewSingleCharToken(TokenType.LeftBracket, "[", start);
                break;
            case ']':
                token = NewSingleCharToken(TokenType.RightBracket, "]", start);
                break;
            case '"':
            {
                var value = ReadString();
                var end = new Position(_line, _column + 1);
                token = new Token(TokenType.String, value, new Span(start, end));
                break;
            }
            case '\'':
            {
                if (!TryReadCharLiteral(out var value))
                {
                    token = NewCurrentCharToken(TokenType.Illegal, start);
                    break;
                }

                var end = new Position(_line, _column + 1);
                token = new Token(TokenType.Char, value.ToString(), new Span(start, end));
                break;
            }
            case '\0':
                token = new Token(TokenType.EndOfFile, "", new Span(start, start));
                break;
            default:
                if (IsIdentifierStart(_ch))
                {
                    var literal = ReadIdentifier();
                    var type = Token.LookupIdentifier(literal);
                    var end = new Position(_line, _column);
                    return new Token(type, literal, new Span(start, end));
                }

                if (IsDigit(_ch))
                {
                    var literal = ReadNumber(out var tokenType);
                    var end = new Position(_line, _column);
                    return new Token(tokenType, literal, new Span(start, end));
                }

                token = NewCurrentCharToken(TokenType.Illegal, start);
                break;
        }

        ReadChar();
        return token;
    }

    private void SkipIgnored()
    {
        while (true)
        {
            while (_ch is ' ' or '\t' or '\n' or '\r')
            {
                ReadChar();
            }

            if (_ch != '/' || PeekChar() != '/')
            {
                return;
            }

            while (_ch is not '\n' and not '\0')
            {
                ReadChar();
            }
        }
    }

    private void ReadChar()
    {
        var advancingPastNewline = _ch == '\n' && _position < _inputLength;

        _ch = _readPosition >= _inputLength ? '\0' : _input[_readPosition];
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
        return _readPosition >= _inputLength ? '\0' : _input[_readPosition];
    }

    private string ReadIdentifier()
    {
        var position = _position;
        while (IsIdentifierPart(_ch))
        {
            ReadChar();
        }
        return _input[position.._position];
    }

    private string ReadNumber(out TokenType tokenType)
    {
        tokenType = TokenType.Integer;
        var position = _position;
        while (IsDigit(_ch))
        {
            ReadChar();
        }

        if (_ch == '.' && IsDigit(PeekChar()))
        {
            tokenType = TokenType.Double;
            ReadChar();
            while (IsDigit(_ch))
            {
                ReadChar();
            }
        }

        var end = _position;
        if (tokenType == TokenType.Integer && _ch == 'b')
        {
            tokenType = TokenType.Byte;
            ReadChar();
        }

        return _input[position..end];
    }

    private bool TryReadCharLiteral(out char value)
    {
        value = '\0';

        ReadChar();
        if (_ch is '\0' or '\n' or '\r')
        {
            return false;
        }

        if (_ch == '\\')
        {
            ReadChar();
            value = _ch switch
            {
                '\\' => '\\',
                '\'' => '\'',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                _ => '\0',
            };

            if (value == '\0' && _ch != '0')
            {
                return false;
            }
        }
        else
        {
            value = _ch;
        }

        ReadChar();
        return _ch == '\'';
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

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsAsciiLetter(ch) || ch == '_';
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsAsciiLetterOrDigit(ch) || ch == '_';
    }

    private static bool IsDigit(char ch)
    {
        return char.IsAsciiDigit(ch);
    }

    private static Token NewSingleCharToken(TokenType type, string literal, Position start)
    {
        return new Token(type, literal, new Span(start, new Position(start.Line, start.Column + 1)));
    }

    private Token NewCurrentCharToken(TokenType type, Position start)
    {
        return NewSingleCharToken(type, _ch.ToString(), start);
    }

    private Token NewDoubleCharToken(TokenType type, string literal, Position start)
    {
        return new Token(type, literal, new Span(start, new Position(_line, _column + 1)));
    }
}
