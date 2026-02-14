using Kong.Lexer;
using Kong.Token;

namespace Kong.Tests;

public class LexerTests
{
    [Fact]
    public void TestNextToken()
    {
        var input = """
            let five = 5;
            let ten = 10;

            let add = fn(x, y) {
              x + y;
            };

            let result = add(five, ten);
            !-/*5;
            5 < 10 > 5;

            if (5 < 10) {
                return true;
            } else {
                return false;
            }

            10 == 10;
            10 != 9;
            "foobar"
            "foo bar"
            [1, 2];
            {"foo": "bar"}
            """;

        var tests = new (TokenType expectedType, string expectedLiteral)[]
        {
            (TokenType.Let, "let"),
            (TokenType.Ident, "five"),
            (TokenType.Assign, "="),
            (TokenType.Int, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Ident, "ten"),
            (TokenType.Assign, "="),
            (TokenType.Int, "10"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Ident, "add"),
            (TokenType.Assign, "="),
            (TokenType.Function, "fn"),
            (TokenType.LParen, "("),
            (TokenType.Ident, "x"),
            (TokenType.Comma, ","),
            (TokenType.Ident, "y"),
            (TokenType.RParen, ")"),
            (TokenType.LBrace, "{"),
            (TokenType.Ident, "x"),
            (TokenType.Plus, "+"),
            (TokenType.Ident, "y"),
            (TokenType.Semicolon, ";"),
            (TokenType.RBrace, "}"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Ident, "result"),
            (TokenType.Assign, "="),
            (TokenType.Ident, "add"),
            (TokenType.LParen, "("),
            (TokenType.Ident, "five"),
            (TokenType.Comma, ","),
            (TokenType.Ident, "ten"),
            (TokenType.RParen, ")"),
            (TokenType.Semicolon, ";"),
            (TokenType.Bang, "!"),
            (TokenType.Minus, "-"),
            (TokenType.Slash, "/"),
            (TokenType.Asterisk, "*"),
            (TokenType.Int, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.Int, "5"),
            (TokenType.Lt, "<"),
            (TokenType.Int, "10"),
            (TokenType.Gt, ">"),
            (TokenType.Int, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.If, "if"),
            (TokenType.LParen, "("),
            (TokenType.Int, "5"),
            (TokenType.Lt, "<"),
            (TokenType.Int, "10"),
            (TokenType.RParen, ")"),
            (TokenType.LBrace, "{"),
            (TokenType.Return, "return"),
            (TokenType.True, "true"),
            (TokenType.Semicolon, ";"),
            (TokenType.RBrace, "}"),
            (TokenType.Else, "else"),
            (TokenType.LBrace, "{"),
            (TokenType.Return, "return"),
            (TokenType.False, "false"),
            (TokenType.Semicolon, ";"),
            (TokenType.RBrace, "}"),
            (TokenType.Int, "10"),
            (TokenType.Eq, "=="),
            (TokenType.Int, "10"),
            (TokenType.Semicolon, ";"),
            (TokenType.Int, "10"),
            (TokenType.NotEq, "!="),
            (TokenType.Int, "9"),
            (TokenType.Semicolon, ";"),
            (TokenType.String, "foobar"),
            (TokenType.String, "foo bar"),
            (TokenType.LBracket, "["),
            (TokenType.Int, "1"),
            (TokenType.Comma, ","),
            (TokenType.Int, "2"),
            (TokenType.RBracket, "]"),
            (TokenType.Semicolon, ";"),
            (TokenType.LBrace, "{"),
            (TokenType.String, "foo"),
            (TokenType.Colon, ":"),
            (TokenType.String, "bar"),
            (TokenType.RBrace, "}"),
            (TokenType.Eof, ""),
        };

        var l = new Lexer.Lexer(input);

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();

            Assert.Equal(tests[i].expectedType, tok.Type);
            Assert.Equal(tests[i].expectedLiteral, tok.Literal);
        }
    }

    [Fact]
    public void TestTokenSpans_SingleLine()
    {
        // Input:  let x = 5;
        // Cols:   1234567890
        var input = "let x = 5;";

        var l = new Lexer.Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Let, "let", 1, 1, 1, 4),
            (TokenType.Ident, "x", 1, 5, 1, 6),
            (TokenType.Assign, "=", 1, 7, 1, 8),
            (TokenType.Int, "5", 1, 9, 1, 10),
            (TokenType.Semicolon, ";", 1, 10, 1, 11),
            (TokenType.Eof, "", 1, 11, 1, 11),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();
            Assert.Equal(tests[i].type, tok.Type);
            Assert.Equal(tests[i].literal, tok.Literal);
            Assert.Equal(tests[i].startLine, tok.Span.Start.Line);
            Assert.Equal(tests[i].startCol, tok.Span.Start.Column);
            Assert.Equal(tests[i].endLine, tok.Span.End.Line);
            Assert.Equal(tests[i].endCol, tok.Span.End.Column);
        }
    }

    [Fact]
    public void TestTokenSpans_MultiLine()
    {
        var input = "let a = 1;\nlet b = 2;";

        var l = new Lexer.Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            // Line 1: let a = 1;
            (TokenType.Let, "let", 1, 1, 1, 4),
            (TokenType.Ident, "a", 1, 5, 1, 6),
            (TokenType.Assign, "=", 1, 7, 1, 8),
            (TokenType.Int, "1", 1, 9, 1, 10),
            (TokenType.Semicolon, ";", 1, 10, 1, 11),
            // Line 2: let b = 2;
            (TokenType.Let, "let", 2, 1, 2, 4),
            (TokenType.Ident, "b", 2, 5, 2, 6),
            (TokenType.Assign, "=", 2, 7, 2, 8),
            (TokenType.Int, "2", 2, 9, 2, 10),
            (TokenType.Semicolon, ";", 2, 10, 2, 11),
            (TokenType.Eof, "", 2, 11, 2, 11),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();
            Assert.Equal(tests[i].type, tok.Type);
            Assert.Equal(tests[i].literal, tok.Literal);
            Assert.Equal(tests[i].startLine, tok.Span.Start.Line);
            Assert.Equal(tests[i].startCol, tok.Span.Start.Column);
            Assert.Equal(tests[i].endLine, tok.Span.End.Line);
            Assert.Equal(tests[i].endCol, tok.Span.End.Column);
        }
    }

    [Fact]
    public void TestTokenSpans_MultiCharOperators()
    {
        var input = "10 == 9 != 8";

        var l = new Lexer.Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Int, "10", 1, 1, 1, 3),
            (TokenType.Eq, "==", 1, 4, 1, 6),
            (TokenType.Int, "9", 1, 7, 1, 8),
            (TokenType.NotEq, "!=", 1, 9, 1, 11),
            (TokenType.Int, "8", 1, 12, 1, 13),
            (TokenType.Eof, "", 1, 13, 1, 13),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();
            Assert.Equal(tests[i].type, tok.Type);
            Assert.Equal(tests[i].literal, tok.Literal);
            Assert.Equal(tests[i].startLine, tok.Span.Start.Line);
            Assert.Equal(tests[i].startCol, tok.Span.Start.Column);
            Assert.Equal(tests[i].endLine, tok.Span.End.Line);
            Assert.Equal(tests[i].endCol, tok.Span.End.Column);
        }
    }

    [Fact]
    public void TestTokenSpans_StringLiteral()
    {
        // Input:  "hello" 42
        // Cols:   12345678901
        var input = "\"hello\" 42";

        var l = new Lexer.Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.String, "hello", 1, 1, 1, 8),  // includes both quotes
            (TokenType.Int, "42", 1, 9, 1, 11),
            (TokenType.Eof, "", 1, 11, 1, 11),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();
            Assert.Equal(tests[i].type, tok.Type);
            Assert.Equal(tests[i].literal, tok.Literal);
            Assert.Equal(tests[i].startLine, tok.Span.Start.Line);
            Assert.Equal(tests[i].startCol, tok.Span.Start.Column);
            Assert.Equal(tests[i].endLine, tok.Span.End.Line);
            Assert.Equal(tests[i].endCol, tok.Span.End.Column);
        }
    }

    [Fact]
    public void TestTokenSpans_FunctionDefinition()
    {
        var input = "fn(x) {\n  return x;\n}";

        var l = new Lexer.Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Function, "fn", 1, 1, 1, 3),
            (TokenType.LParen, "(", 1, 3, 1, 4),
            (TokenType.Ident, "x", 1, 4, 1, 5),
            (TokenType.RParen, ")", 1, 5, 1, 6),
            (TokenType.LBrace, "{", 1, 7, 1, 8),
            (TokenType.Return, "return", 2, 3, 2, 9),
            (TokenType.Ident, "x", 2, 10, 2, 11),
            (TokenType.Semicolon, ";", 2, 11, 2, 12),
            (TokenType.RBrace, "}", 3, 1, 3, 2),
            (TokenType.Eof, "", 3, 2, 3, 2),
        };

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();
            Assert.Equal(tests[i].type, tok.Type);
            Assert.Equal(tests[i].literal, tok.Literal);
            Assert.Equal(tests[i].startLine, tok.Span.Start.Line);
            Assert.Equal(tests[i].startCol, tok.Span.Start.Column);
            Assert.Equal(tests[i].endLine, tok.Span.End.Line);
            Assert.Equal(tests[i].endCol, tok.Span.End.Column);
        }
    }
}
