using Kong.Common;

namespace Kong.Lexing;

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

    public Token NextToken()
    {
        Token token;

        SkipIgnored();
        var start = new Position(_line, _column);

        switch (_ch)
        {
            case '=':
                if (PeekChar() == '=')
                {
                    ReadChar();
                    token = new Token(TokenType.Equal, "==",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else if (PeekChar() == '>')
                {
                    ReadChar();
                    token = new Token(TokenType.DoubleArrow, "=>",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    token = NewToken(TokenType.Assign, _ch, start);
                }
                break;
            case '+':
                token = NewToken(TokenType.Plus, _ch, start);
                break;
            case '-':
                if (PeekChar() == '>')
                {
                    ReadChar();
                    token = new Token(TokenType.Arrow, "->",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    token = NewToken(TokenType.Minus, _ch, start);
                }
                break;
            case '!':
                if (PeekChar() == '=')
                {
                    ReadChar();
                    token = new Token(TokenType.NotEqual, "!=",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    token = NewToken(TokenType.Bang, _ch, start);
                }
                break;
            case '/':
                token = NewToken(TokenType.Slash, _ch, start);
                break;
            case '&':
                if (PeekChar() == '&')
                {
                    ReadChar();
                    token = new Token(TokenType.And, "&&",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    token = NewToken(TokenType.Illegal, _ch, start);
                }
                break;
            case '|':
                if (PeekChar() == '|')
                {
                    ReadChar();
                    token = new Token(TokenType.Or, "||",
                        new Span(start, new Position(_line, _column + 1)));
                }
                else
                {
                    token = NewToken(TokenType.Illegal, _ch, start);
                }
                break;
            case '*':
                token = NewToken(TokenType.Asterisk, _ch, start);
                break;
            case '<':
                token = NewToken(TokenType.LessThan, _ch, start);
                break;
            case '>':
                token = NewToken(TokenType.GreaterThan, _ch, start);
                break;
            case ',':
                token = NewToken(TokenType.Comma, _ch, start);
                break;
            case '.':
                token = NewToken(TokenType.Dot, _ch, start);
                break;
            case ':':
                token = NewToken(TokenType.Colon, _ch, start);
                break;
            case '(':
                token = NewToken(TokenType.LeftParenthesis, _ch, start);
                break;
            case ')':
                token = NewToken(TokenType.RightParenthesis, _ch, start);
                break;
            case '{':
                token = NewToken(TokenType.LeftBrace, _ch, start);
                break;
            case '}':
                token = NewToken(TokenType.RightBrace, _ch, start);
                break;
            case '[':
                token = NewToken(TokenType.LeftBracket, _ch, start);
                break;
            case ']':
                token = NewToken(TokenType.RightBracket, _ch, start);
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
                    token = NewToken(TokenType.Illegal, _ch, start);
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
                if (char.IsAsciiLetter(_ch) || _ch == '_')
                {
                    var literal = ReadIdentifier();
                    var type = Token.LookupIdentifier(literal);
                    var end = new Position(_line, _column);
                    return new Token(type, literal, new Span(start, end));
                }

                if (char.IsAsciiDigit(_ch))
                {
                    var literal = ReadNumber(out var tokenType);
                    var end = new Position(_line, _column);
                    return new Token(tokenType, literal, new Span(start, end));
                }

                token = NewToken(TokenType.Illegal, _ch, start);
                break;
        }

        ReadChar();
        return token;
    }

    private void SkipIgnored()
    {
        while (true)
        {
            var skipped = false;

            while (_ch is ' ' or '\t' or '\n' or '\r')
            {
                skipped = true;
                ReadChar();
            }

            if (_ch == '/' && PeekChar() == '/')
            {
                skipped = true;
                while (_ch is not '\n' and not '\0')
                {
                    ReadChar();
                }
            }

            if (!skipped)
            {
                return;
            }
        }
    }

    private void ReadChar()
    {
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
        while (char.IsAsciiLetterOrDigit(_ch) || _ch == '_')
        {
            ReadChar();
        }
        return _input[position.._position];
    }

    private string ReadNumber(out TokenType tokenType)
    {
        tokenType = TokenType.Integer;
        var position = _position;
        while (char.IsAsciiDigit(_ch))
        {
            ReadChar();
        }

        if (_ch == '.' && char.IsAsciiDigit(PeekChar()))
        {
            tokenType = TokenType.Double;
            ReadChar();
            while (char.IsAsciiDigit(_ch))
            {
                ReadChar();
            }

            return _input[position.._position];
        }

        if (_ch == 'b')
        {
            tokenType = TokenType.Byte;
            var literal = _input[position.._position];
            ReadChar();
            return literal;
        }

        return _input[position.._position];
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

    private static Token NewToken(TokenType type, char ch, Position start)
    {
        // todo: avoid ToString allocations
        return new Token(type, ch.ToString(), new Span(start, new Position(start.Line, start.Column + 1)));
    }
}
