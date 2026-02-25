using Kong.Lexing;

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

        var l = new Lexing.Lexer(input);

        for (var i = 0; i < tests.Length; i++)
        {
            var tok = l.NextToken();

            Assert.Equal(tests[i].expectedType, tok.Type);
            Assert.Equal(tests[i].expectedLiteral, tok.Literal);
        }
    }
}
