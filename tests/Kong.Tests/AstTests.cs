namespace Kong.Tests;

public class AstTests
{
    [Fact]
    public void TestString()
    {
        var program = new CompilationUnit
        {
            Statements = new List<IStatement>
            {
                new LetStatement
                {
                    Token = new Token(TokenType.Let, "let"),
                    Name = new Identifier
                    {
                        Token = new Token(TokenType.Identifier, "myVar"),
                        Value = "myVar",
                    },
                    Value = new Identifier
                    {
                        Token = new Token(TokenType.Identifier, "anotherVar"),
                        Value = "anotherVar",
                    },
                },
            },
        };

        Assert.Equal("let myVar = anotherVar;", program.String());
    }
}
