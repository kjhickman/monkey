using System.Text;
using Kong.Common;
using Kong.Lexing;

namespace Kong.Parsing;

public class Identifier : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the TokenType.Identifier token
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Value;
}

public class LoopExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'loop' token
    public BlockStatement Body { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"loop {Body.String()}";
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

public class DoubleLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public double Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class CharLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public char Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ByteLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; }
    public byte Value { get; set; }

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

public class MatchExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'match' token
    public IExpression Target { get; set; } = null!;
    public List<MatchArm> Arms { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"match {Target.String()} {{ {string.Join(", ", Arms.Select(a => a.String()))} }}";
    }
}

public class FunctionLiteral : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The 'fn' token
    public bool IsLambda { get; set; }
    public List<FunctionParameter> Parameters { get; set; } = [];
    public ITypeNode? ReturnTypeAnnotation { get; set; }
    public BlockStatement Body { get; set; } = null!;
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        var paramStrings = Parameters.Select(p => p.String());
        if (IsLambda)
        {
            sb.Append('(');
            sb.Append(string.Join(", ", paramStrings));
            sb.Append(") => ");
            sb.Append(Body.String());
            return sb.ToString();
        }

        sb.Append(TokenLiteral());
        sb.Append(' ');
        sb.Append(Name);
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

public class CallExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The '(' token
    public IExpression Function { get; set; } = null!; // Identifier or FunctionLiteral
    public List<CallArgument> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = Arguments.Select(a => a.String());
        return $"{Function.String()}({string.Join(", ", args)})";
    }
}

public class MemberAccessExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // The '.' token
    public IExpression Object { get; set; } = null!;
    public string Member { get; set; } = "";

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Object.String()}.{Member}";
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

public class NewExpression : IExpression
{
    public Span Span { get; set; }
    public Token Token { get; set; } // the 'new' token
    public string TypePath { get; set; } = "";
    public List<ITypeNode> TypeArguments { get; set; } = [];
    public List<IExpression> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = Arguments.Select(a => a.String());
        var typeArguments = TypeArguments.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", TypeArguments.Select(t => t.String()))}>";
        return $"new {TypePath}{typeArguments}({string.Join(", ", args)})";
    }
}
