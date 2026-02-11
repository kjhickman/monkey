using System.Text;
using Kong.Token;

namespace Kong.Ast;

public class Program : INode
{
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
    public Token.Token Token { get; set; } // the TokenType.Let token
    public Identifier Name { get; set; } = null!;
    public IExpression? Value { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append(TokenLiteral() + " ");
        sb.Append(Name.String());
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
    public Token.Token Token { get; set; } // the TokenType.Ident token
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Value;
}

public class ReturnStatement : IStatement
{
    public Token.Token Token { get; set; } // the 'return' token
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
    public Token.Token Token { get; set; } // the first token of the expression
    public IExpression? Expression { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Expression?.String() ?? "";
    }
}

public class IntegerLiteral : IExpression
{
    public Token.Token Token { get; set; }
    public long Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class PrefixExpression : IExpression
{
    public Token.Token Token { get; set; } // The prefix token, e.g. !
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
    public Token.Token Token { get; set; } // The operator token, e.g. +
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
    public Token.Token Token { get; set; }
    public bool Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class IfExpression : IExpression
{
    public Token.Token Token { get; set; } // The 'if' token
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
    public Token.Token Token { get; set; } // the { token
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
    public Token.Token Token { get; set; } // The 'fn' token
    public List<Identifier> Parameters { get; set; } = [];
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
        sb.Append(") ");
        sb.Append(Body.String());
        return sb.ToString();
    }
}

public class CallExpression : IExpression
{
    public Token.Token Token { get; set; } // The '(' token
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
    public Token.Token Token { get; set; }
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ArrayLiteral : IExpression
{
    public Token.Token Token { get; set; } // the '[' token
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
    public Token.Token Token { get; set; } // The [ token
    public IExpression Left { get; set; } = null!;
    public IExpression Index { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"({Left.String()}[{Index.String()}])";
    }
}

public class HashLiteral : IExpression
{
    public Token.Token Token { get; set; } // the '{' token
    public List<KeyValuePair<IExpression, IExpression>> Pairs { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var pairs = Pairs.Select(p => $"{p.Key.String()}:{p.Value.String()}");
        return $"{{{string.Join(", ", pairs)}}}";
    }
}
