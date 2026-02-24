using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class CompilationUnit : INode
{
    public Span Span { get; set; }
    public List<IStatement> Statements { get; set; } = [];

    public string TokenLiteral()
    {
        return Statements.Count > 0 ? Statements[0].TokenLiteral() : "";
    }

    public string String()
    {
        var sb = new StringBuilder();
        foreach (var s in Statements)
        {
            sb.Append(s.String());
        }
        return sb.ToString();
    }
}

public class FunctionParameter : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the parameter identifier token
    public string Name { get; set; } = "";
    public ITypeNode? TypeAnnotation { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        if (TypeAnnotation == null)
        {
            return Name;
        }

        return $"{Name}: {TypeAnnotation.String()}";
    }
}

public class MatchArm : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the variant identifier token
    public Identifier VariantName { get; set; } = null!;
    public List<Identifier> Bindings { get; set; } = [];
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var bindingText = Bindings.Count == 0
            ? ""
            : $"({string.Join(", ", Bindings.Select(b => b.String()))})";
        return $"{VariantName.String()}{bindingText} => {Body.String()}";
    }
}

public enum CallArgumentModifier
{
    None,
    Out,
    Ref,
}

public class CallArgument : INode
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public CallArgumentModifier Modifier { get; set; }
    public IExpression Expression { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Modifier switch
        {
            CallArgumentModifier.Out => $"out {Expression.String()}",
            CallArgumentModifier.Ref => $"ref {Expression.String()}",
            _ => Expression.String(),
        };
    }
}
