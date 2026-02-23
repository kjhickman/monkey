using Kong.Lexing;

namespace Kong.Tests.Lexing;

public class LexerTests
{
    [Fact]
    public void TestNextToken()
    {
        var input = """
            let five: int = 5;
            let ten: int = 10;

            let add = fn(x, y) -> int {
              x + y;
            };

            let result: int = add(five, ten);
            !-/*5;
            5 < 10 > 5;

            if (5 < 10) {
                return true;
            } else {
                return false;
            }

            10 == 10;
            10 != 9;
            true && false || true;
            "foobar"
            "foo bar"
            [1, 2];
            """;

        var tests = new (TokenType expectedType, string expectedLiteral)[]
        {
            (TokenType.Let, "let"),
            (TokenType.Identifier, "five"),
            (TokenType.Colon, ":"),
            (TokenType.Identifier, "int"),
            (TokenType.Assign, "="),
            (TokenType.Integer, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Identifier, "ten"),
            (TokenType.Colon, ":"),
            (TokenType.Identifier, "int"),
            (TokenType.Assign, "="),
            (TokenType.Integer, "10"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Identifier, "add"),
            (TokenType.Assign, "="),
            (TokenType.Function, "fn"),
            (TokenType.LeftParenthesis, "("),
            (TokenType.Identifier, "x"),
            (TokenType.Comma, ","),
            (TokenType.Identifier, "y"),
            (TokenType.RightParenthesis, ")"),
            (TokenType.Arrow, "->"),
            (TokenType.Identifier, "int"),
            (TokenType.LeftBrace, "{"),
            (TokenType.Identifier, "x"),
            (TokenType.Plus, "+"),
            (TokenType.Identifier, "y"),
            (TokenType.Semicolon, ";"),
            (TokenType.RightBrace, "}"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Identifier, "result"),
            (TokenType.Colon, ":"),
            (TokenType.Identifier, "int"),
            (TokenType.Assign, "="),
            (TokenType.Identifier, "add"),
            (TokenType.LeftParenthesis, "("),
            (TokenType.Identifier, "five"),
            (TokenType.Comma, ","),
            (TokenType.Identifier, "ten"),
            (TokenType.RightParenthesis, ")"),
            (TokenType.Semicolon, ";"),
            (TokenType.Bang, "!"),
            (TokenType.Minus, "-"),
            (TokenType.Slash, "/"),
            (TokenType.Asterisk, "*"),
            (TokenType.Integer, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.Integer, "5"),
            (TokenType.LessThan, "<"),
            (TokenType.Integer, "10"),
            (TokenType.GreaterThan, ">"),
            (TokenType.Integer, "5"),
            (TokenType.Semicolon, ";"),
            (TokenType.If, "if"),
            (TokenType.LeftParenthesis, "("),
            (TokenType.Integer, "5"),
            (TokenType.LessThan, "<"),
            (TokenType.Integer, "10"),
            (TokenType.RightParenthesis, ")"),
            (TokenType.LeftBrace, "{"),
            (TokenType.Return, "return"),
            (TokenType.True, "true"),
            (TokenType.Semicolon, ";"),
            (TokenType.RightBrace, "}"),
            (TokenType.Else, "else"),
            (TokenType.LeftBrace, "{"),
            (TokenType.Return, "return"),
            (TokenType.False, "false"),
            (TokenType.Semicolon, ";"),
            (TokenType.RightBrace, "}"),
            (TokenType.Integer, "10"),
            (TokenType.Equal, "=="),
            (TokenType.Integer, "10"),
            (TokenType.Semicolon, ";"),
            (TokenType.Integer, "10"),
            (TokenType.NotEqual, "!="),
            (TokenType.Integer, "9"),
            (TokenType.Semicolon, ";"),
            (TokenType.True, "true"),
            (TokenType.And, "&&"),
            (TokenType.False, "false"),
            (TokenType.Or, "||"),
            (TokenType.True, "true"),
            (TokenType.Semicolon, ";"),
            (TokenType.String, "foobar"),
            (TokenType.String, "foo bar"),
            (TokenType.LeftBracket, "["),
            (TokenType.Integer, "1"),
            (TokenType.Comma, ","),
            (TokenType.Integer, "2"),
            (TokenType.RightBracket, "]"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        var l = new Lexer(input);

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

        var l = new Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Let, "let", 1, 1, 1, 4),
            (TokenType.Identifier, "x", 1, 5, 1, 6),
            (TokenType.Assign, "=", 1, 7, 1, 8),
            (TokenType.Integer, "5", 1, 9, 1, 10),
            (TokenType.Semicolon, ";", 1, 10, 1, 11),
            (TokenType.EndOfFile, "", 1, 11, 1, 11),
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

        var l = new Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            // Line 1: let a = 1;
            (TokenType.Let, "let", 1, 1, 1, 4),
            (TokenType.Identifier, "a", 1, 5, 1, 6),
            (TokenType.Assign, "=", 1, 7, 1, 8),
            (TokenType.Integer, "1", 1, 9, 1, 10),
            (TokenType.Semicolon, ";", 1, 10, 1, 11),
            // Line 2: let b = 2;
            (TokenType.Let, "let", 2, 1, 2, 4),
            (TokenType.Identifier, "b", 2, 5, 2, 6),
            (TokenType.Assign, "=", 2, 7, 2, 8),
            (TokenType.Integer, "2", 2, 9, 2, 10),
            (TokenType.Semicolon, ";", 2, 10, 2, 11),
            (TokenType.EndOfFile, "", 2, 11, 2, 11),
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
        var input = "10 == 9 != 8 && true || false";

        var l = new Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Integer, "10", 1, 1, 1, 3),
            (TokenType.Equal, "==", 1, 4, 1, 6),
            (TokenType.Integer, "9", 1, 7, 1, 8),
            (TokenType.NotEqual, "!=", 1, 9, 1, 11),
            (TokenType.Integer, "8", 1, 12, 1, 13),
            (TokenType.And, "&&", 1, 14, 1, 16),
            (TokenType.True, "true", 1, 17, 1, 21),
            (TokenType.Or, "||", 1, 22, 1, 24),
            (TokenType.False, "false", 1, 25, 1, 30),
            (TokenType.EndOfFile, "", 1, 30, 1, 30),
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

        var l = new Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.String, "hello", 1, 1, 1, 8),  // includes both quotes
            (TokenType.Integer, "42", 1, 9, 1, 11),
            (TokenType.EndOfFile, "", 1, 11, 1, 11),
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

        var l = new Lexer(input);

        var tests = new (TokenType type, string literal, int startLine, int startCol, int endLine, int endCol)[]
        {
            (TokenType.Function, "fn", 1, 1, 1, 3),
            (TokenType.LeftParenthesis, "(", 1, 3, 1, 4),
            (TokenType.Identifier, "x", 1, 4, 1, 5),
            (TokenType.RightParenthesis, ")", 1, 5, 1, 6),
            (TokenType.LeftBrace, "{", 1, 7, 1, 8),
            (TokenType.Return, "return", 2, 3, 2, 9),
            (TokenType.Identifier, "x", 2, 10, 2, 11),
            (TokenType.Semicolon, ";", 2, 11, 2, 12),
            (TokenType.RightBrace, "}", 3, 1, 3, 2),
            (TokenType.EndOfFile, "", 3, 2, 3, 2),
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
    public void TestLexesDotToken()
    {
        var input = "System.Console.WriteLine";
        var l = new Lexer(input);

        var tests = new (TokenType type, string literal)[]
        {
            (TokenType.Identifier, "System"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "Console"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "WriteLine"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = l.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesImportKeyword()
    {
        var input = "import System.Console;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Import, "import"),
            (TokenType.Identifier, "System"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "Console"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesPathImportStatement()
    {
        var input = "import \"./util.kg\";";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Import, "import"),
            (TokenType.String, "./util.kg"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesNamespaceKeyword()
    {
        var input = "namespace Foo.Bar;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Namespace, "namespace"),
            (TokenType.Identifier, "Foo"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "Bar"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesDoubleCharAndByteLiterals()
    {
        var input = "1.25; 'a'; 42b;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Double, "1.25"),
            (TokenType.Semicolon, ";"),
            (TokenType.Char, "a"),
            (TokenType.Semicolon, ";"),
            (TokenType.Byte, "42"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesEscapedCharLiterals()
    {
        var input = "'\\n' '\\\\' '\\\''";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Char, "\n"),
            (TokenType.Char, "\\"),
            (TokenType.Char, "'"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestSkipsLineComments()
    {
        var input = "let x = 1; // this is a comment\nlet y = x + 1;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Let, "let"),
            (TokenType.Identifier, "x"),
            (TokenType.Assign, "="),
            (TokenType.Integer, "1"),
            (TokenType.Semicolon, ";"),
            (TokenType.Let, "let"),
            (TokenType.Identifier, "y"),
            (TokenType.Assign, "="),
            (TokenType.Identifier, "x"),
            (TokenType.Plus, "+"),
            (TokenType.Integer, "1"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesNewKeyword()
    {
        var input = "new System.Text.StringBuilder();";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.New, "new"),
            (TokenType.Identifier, "System"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "Text"),
            (TokenType.Dot, "."),
            (TokenType.Identifier, "StringBuilder"),
            (TokenType.LeftParenthesis, "("),
            (TokenType.RightParenthesis, ")"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesVarAndAssignment()
    {
        var input = "var x = 1; x = x + 1;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Var, "var"),
            (TokenType.Identifier, "x"),
            (TokenType.Assign, "="),
            (TokenType.Integer, "1"),
            (TokenType.Semicolon, ";"),
            (TokenType.Identifier, "x"),
            (TokenType.Assign, "="),
            (TokenType.Identifier, "x"),
            (TokenType.Plus, "+"),
            (TokenType.Integer, "1"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesOutAndRefKeywords()
    {
        var input = "Foo(out x, ref y);";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Identifier, "Foo"),
            (TokenType.LeftParenthesis, "("),
            (TokenType.Out, "out"),
            (TokenType.Identifier, "x"),
            (TokenType.Comma, ","),
            (TokenType.Ref, "ref"),
            (TokenType.Identifier, "y"),
            (TokenType.RightParenthesis, ")"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesForInLoopTokens()
    {
        var input = "for i in xs { i; }";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.For, "for"),
            (TokenType.Identifier, "i"),
            (TokenType.In, "in"),
            (TokenType.Identifier, "xs"),
            (TokenType.LeftBrace, "{"),
            (TokenType.Identifier, "i"),
            (TokenType.Semicolon, ";"),
            (TokenType.RightBrace, "}"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }

    [Fact]
    public void TestLexesBreakAndContinueKeywords()
    {
        var input = "break; continue;";
        var lexer = new Lexer(input);

        var tests = new (TokenType Type, string Literal)[]
        {
            (TokenType.Break, "break"),
            (TokenType.Semicolon, ";"),
            (TokenType.Continue, "continue"),
            (TokenType.Semicolon, ";"),
            (TokenType.EndOfFile, ""),
        };

        foreach (var (type, literal) in tests)
        {
            var token = lexer.NextToken();
            Assert.Equal(type, token.Type);
            Assert.Equal(literal, token.Literal);
        }
    }
}
