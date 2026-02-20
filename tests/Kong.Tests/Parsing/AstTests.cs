using Kong.Lexing;
using Kong.Parsing;

namespace Kong.Tests.Parsing;

public class AstTests
{
    [Fact]
    public void TestString()
    {
        var program = new CompilationUnit
        {
            Statements =
            [
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
            ],
        };

        Assert.Equal("let myVar = anotherVar;", program.String());
    }

    [Fact]
    public void TestTypeNodeString()
    {
        var intType = new NamedType
        {
            Token = new Token(TokenType.Identifier, "int"),
            Name = "int",
        };

        var arrayType = new ArrayType
        {
            Token = new Token(TokenType.LeftBracket, "["),
            ElementType = intType,
        };

        Assert.Equal("int", intType.String());
        Assert.Equal("int[]", arrayType.String());
    }

    [Fact]
    public void TestTypedNodeString()
    {
        var intType = new NamedType
        {
            Token = new Token(TokenType.Identifier, "int"),
            Name = "int",
        };

        var program = new CompilationUnit
        {
            Statements =
            [
                new LetStatement
                {
                    Token = new Token(TokenType.Let, "let"),
                    Name = new Identifier
                    {
                        Token = new Token(TokenType.Identifier, "x"),
                        Value = "x",
                    },
                    TypeAnnotation = intType,
                    Value = new IntegerLiteral
                    {
                        Token = new Token(TokenType.Integer, "5"),
                        Value = 5,
                    },
                },
            ],
        };

        Assert.Equal("let x: int = 5;", program.String());
    }
}
