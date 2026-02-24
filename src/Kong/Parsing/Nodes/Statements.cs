using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class LetStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Let token
    public Identifier Name { get; set; } = null!;
    public ITypeNode? TypeAnnotation { get; set; }
    public IExpression? Value { get; set; }
    public bool IsMutable { get; set; }

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
        return sb.ToString();
    }
}

public class AssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the identifier token on the LHS
    public Identifier Name { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Name.String()} = {Value.String()}";
    }
}

public class IndexAssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '=' token
    public IndexExpression Target { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Target.String()} = {Value.String()}";
    }
}

public class MemberAssignmentStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the '=' token
    public MemberAccessExpression Target { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Target.String()} = {Value.String()}";
    }
}

public class BreakStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public IExpression? Value { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String() => Value == null ? "break" : $"break {Value.String()}";
}

public class ContinueStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String() => "continue";
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

public class ImportStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'use' token
    public string QualifiedName { get; set; } = "";

    public string Alias
    {
        get
        {
            var lastDot = QualifiedName.LastIndexOf('.');
            return lastDot < 0 ? QualifiedName : QualifiedName[(lastDot + 1)..];
        }
    }

    public string TokenLiteral() => Token.Literal;

    public string String() => $"use {QualifiedName}";
}

public class NamespaceStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'module' token
    public string QualifiedName { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String() => $"module {QualifiedName}";
}

public class ForInStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'for' token
    public Identifier Iterator { get; set; } = null!;
    public IExpression Iterable { get; set; } = null!;
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"for {Iterator.String()} in {Iterable.String()} {Body.String()}";
    }
}

public class WhileStatement : IStatement
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'while' token
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"while {Condition.String()} {Body.String()}";
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
