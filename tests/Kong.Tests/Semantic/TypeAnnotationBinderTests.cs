using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

namespace Kong.Tests.Semantic;

public class TypeAnnotationBinderTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("char")]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("nint")]
    [InlineData("nuint")]
    [InlineData("float")]
    [InlineData("decimal")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("void")]
    [InlineData("null")]
    public void TestBindNamedType(string typeName)
    {
        var diagnostics = new DiagnosticBag();
        var typeNode = new NamedType
        {
            Token = new Token(TokenType.Identifier, typeName),
            Name = typeName,
            Span = new Span(new Position(1, 1), new Position(1, 1 + typeName.Length)),
        };

        var type = TypeAnnotationBinder.Bind(typeNode, diagnostics);

        Assert.NotNull(type);
        Assert.Equal(typeName, type.Name);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void TestBindArrayType()
    {
        var diagnostics = new DiagnosticBag();
        var elementType = new NamedType
        {
            Token = new Token(TokenType.Identifier, "int"),
            Name = "int",
            Span = new Span(new Position(1, 1), new Position(1, 4)),
        };

        var typeNode = new ArrayType
        {
            Token = new Token(TokenType.LeftBracket, "["),
            ElementType = elementType,
            Span = new Span(new Position(1, 1), new Position(1, 6)),
        };

        var type = TypeAnnotationBinder.Bind(typeNode, diagnostics);

        var arrayType = Assert.IsType<ArrayTypeSymbol>(type);
        Assert.Same(TypeSymbols.Int, arrayType.ElementType);
        Assert.Equal("int[]", arrayType.Name);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void TestBindUnknownNamedTypeReportsDiagnostic()
    {
        var diagnostics = new DiagnosticBag();
        var typeNode = new NamedType
        {
            Token = new Token(TokenType.Identifier, "widget"),
            Name = "widget",
            Span = new Span(new Position(2, 5), new Position(2, 11)),
        };

        var type = TypeAnnotationBinder.Bind(typeNode, diagnostics);

        Assert.Null(type);
        Assert.True(diagnostics.HasErrors);
        Assert.Single(diagnostics.All);
        Assert.Equal("T001", diagnostics.All[0].Code);
        Assert.Equal("unknown type 'widget'", diagnostics.All[0].Message);
        Assert.Equal(typeNode.Span, diagnostics.All[0].Span);
    }

    [Fact]
    public void TestBindArrayTypeWithUnknownElementTypeReportsDiagnostic()
    {
        var diagnostics = new DiagnosticBag();
        var typeNode = new ArrayType
        {
            Token = new Token(TokenType.LeftBracket, "["),
            ElementType = new NamedType
            {
                Token = new Token(TokenType.Identifier, "widget"),
                Name = "widget",
                Span = new Span(new Position(1, 1), new Position(1, 7)),
            },
            Span = new Span(new Position(1, 1), new Position(1, 9)),
        };

        var type = TypeAnnotationBinder.Bind(typeNode, diagnostics);

        Assert.Null(type);
        Assert.True(diagnostics.HasErrors);
        Assert.Single(diagnostics.All);
        Assert.Equal("T001", diagnostics.All[0].Code);
    }

    [Fact]
    public void TestBindFunctionType()
    {
        var diagnostics = new DiagnosticBag();
        var typeNode = new FunctionType
        {
            Token = new Token(TokenType.Function, "fn"),
            ParameterTypes =
            [
                new NamedType { Token = new Token(TokenType.Identifier, "int"), Name = "int" },
                new ArrayType
                {
                    Token = new Token(TokenType.LeftBracket, "["),
                    ElementType = new NamedType { Token = new Token(TokenType.Identifier, "int"), Name = "int" },
                },
            ],
            ReturnType = new NamedType { Token = new Token(TokenType.Identifier, "bool"), Name = "bool" },
        };

        var type = TypeAnnotationBinder.Bind(typeNode, diagnostics);

        var functionType = Assert.IsType<FunctionTypeSymbol>(type);
        Assert.Equal(2, functionType.ParameterTypes.Count);
        Assert.Equal(TypeSymbols.Int, functionType.ParameterTypes[0]);
        Assert.Equal(new ArrayTypeSymbol(TypeSymbols.Int), functionType.ParameterTypes[1]);
        Assert.Equal(TypeSymbols.Bool, functionType.ReturnType);
        Assert.False(diagnostics.HasErrors);
    }
}
