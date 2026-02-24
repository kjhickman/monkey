using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class NamedType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Name;
}

public class GenericType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the base type identifier token
    public string Name { get; set; } = "";
    public List<ITypeNode> TypeArguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{Name}<{string.Join(", ", TypeArguments.Select(t => t.String()))}>";
}

public class ArrayType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '[' token
    public ITypeNode ElementType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{ElementType.String()}[]";
}

public class FunctionType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'fn' token
    public List<ITypeNode> ParameterTypes { get; set; } = [];
    public ITypeNode ReturnType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var parameters = ParameterTypes.Select(p => p.String());
        return $"({string.Join(", ", parameters)}) -> {ReturnType.String()}";
    }
}
