using System.Text;

namespace Kong;

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

public class LetStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Let token
    public Identifier Name { get; set; } = null!;
    public ITypeNode? TypeAnnotation { get; set; }
    public IExpression? Value { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append(TokenLiteral() + " ");
        sb.Append(Name.String());
        if (TypeAnnotation != null)
        {
            sb.Append(": ");
            sb.Append(TypeAnnotation.String());
        }
        sb.Append(" = ");
        if (Value != null)
        {
            sb.Append(Value.String());
        }
        sb.Append(';');
        return sb.ToString();
    }
}

public class Identifier : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Value;
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

public class ReturnStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'return' token
    public IExpression? ReturnValue { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append(TokenLiteral() + " ");
        if (ReturnValue != null)
        {
            sb.Append(ReturnValue.String());
        }
        sb.Append(';');
        return sb.ToString();
    }
}

public class ExpressionStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the first token of the expression
    public IExpression? Expression { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Expression?.String() ?? "";
    }
}

public class IntegerLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public long Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class PrefixExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The prefix token, e.g. !
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Operator}{Right.String()})";
    }
}

public class InfixExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The operator token, e.g. +
    public IExpression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Left.String()} {Operator} {Right.String()})";
    }
}

public class BooleanLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public bool Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class IfExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The 'if' token
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Consequence { get; set; } = null!;
    public BlockStatement? Alternative { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append("if");
        sb.Append(Condition.String());
        sb.Append(' ');
        sb.Append(Consequence.String());
        if (Alternative != null)
        {
            sb.Append("else ");
            sb.Append(Alternative.String());
        }
        return sb.ToString();
    }
}

public class BlockStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the { token
    public List<IStatement> Statements { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

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

public class FunctionLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The 'fn' token
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        var paramStrings = Parameters.Select(p => p.String());
        sb.Append(TokenLiteral());
        if (Name != "")
        {
            sb.Append($"<{Name}>");
        }
        sb.Append('(');
        sb.Append(string.Join(", ", paramStrings));
        sb.Append(')');
        if (ReturnTypeAnnotation != null)
        {
            sb.Append(" -> ");
            sb.Append(ReturnTypeAnnotation.String());
        }
        sb.Append(' ');
        sb.Append(Body.String());
        return sb.ToString();
    }
}

public class NamedType : ITypeNode
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Name;
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
        return $"fn({string.Join(", ", parameters)}) -> {ReturnType.String()}";
    }
}

public class CallExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The '(' token
    public IExpression Function { get; set; } = null!; // Identifier or FunctionLiteral
    public List<IExpression> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = Arguments.Select(a => a.String());
        return $"{Function.String()}({string.Join(", ", args)})";
    }
}

public class StringLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ArrayLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '[' token
    public List<IExpression> Elements { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var elements = Elements.Select(e => e.String());
        return $"[{string.Join(", ", elements)}]";
    }
}

public class IndexExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The [ token
    public IExpression Left { get; set; } = null!;
    public IExpression Index { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Left.String()}[{Index.String()}])";
    }
}
