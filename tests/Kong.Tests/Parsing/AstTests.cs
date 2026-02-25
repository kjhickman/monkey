using Kong.Parsing;
using Kong.Lexing;

namespace Kong.Tests.Parsing;

public class AstTests
{
    [Fact]
    public void TestString()
    {
        var program = new Program
        {
            Statements = new List<IStatement>
            {
                new LetStatement
                {
                    Token = new Token(TokenType.Let, "let"),
                    Name = new Identifier
                    {
                        Token = new Token(TokenType.Ident, "myVar"),
                        Value = "myVar",
                    },
                    Value = new Identifier
                    {
                        Token = new Token(TokenType.Ident, "anotherVar"),
                        Value = "anotherVar",
                    },
                },
            },
        };

        Assert.Equal("let myVar = anotherVar;", program.String());
    }
}
