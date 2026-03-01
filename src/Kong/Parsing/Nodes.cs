using System.Text;
using Kong.Lexing;

namespace Kong.Parsing;

public class Program : INode
{
    public Token Token { get; set; }
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
    public Token Token { get; set; } // the TokenType.Let or TokenType.Var token
    public Identifier Name { get; set; } = null!;
    public IExpression? Value { get; set; }
    public bool IsMutable { get; set; }

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
    public Token Token { get; set; } // the TokenType.Ident token
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Value;
}

public class ReturnStatement : IStatement
{
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
    public Token Token { get; set; } // the first token of the expression
    public IExpression? Expression { get; set; }

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return Expression?.String() ?? "";
    }
}

public class AssignStatement : IStatement
{
    public Token Token { get; set; } // the '=' token
    public Identifier Name { get; set; } = null!;
    public IExpression Value { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        return $"{Name.String()} = {Value.String()};";
    }
}

public class IfStatement : IStatement
{
    public Token Token { get; set; } // the 'if' token
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Consequence { get; set; } = null!;
    public BlockStatement Alternative { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var sb = new StringBuilder();
        sb.Append("if");
        sb.Append(Condition.String());
        sb.Append(' ');
        sb.Append(Consequence.String());
        sb.Append("else ");
        sb.Append(Alternative.String());
        return sb.ToString();
    }
}

public class IntegerLiteral : IExpression
{
    public Token Token { get; set; }
    public long Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class PrefixExpression : IExpression
{
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
    public Token Token { get; set; }
    public bool Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class IfExpression : IExpression
{
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
    public Token Token { get; set; } // The 'fn' token
    public List<FunctionParameter> Parameters { get; set; } = [];
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

public interface ITypeExpression : INode;

public class NamedTypeExpression : ITypeExpression
{
    public Token Token { get; set; }
    public string Name { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Name;
}

public class ArrayTypeExpression : ITypeExpression
{
    public Token Token { get; set; } // The '[' token
    public ITypeExpression ElementType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"{ElementType.String()}[]";
}

public class MapTypeExpression : ITypeExpression
{
    public Token Token { get; set; } // The 'map' token
    public ITypeExpression KeyType { get; set; } = null!;
    public ITypeExpression ValueType { get; set; } = null!;

    public string TokenLiteral() => Token.Literal;
    public string String() => $"map[{KeyType.String()}]{ValueType.String()}";
}

public class FunctionParameter : INode
{
    public Token Token { get; set; }
    public Identifier Name { get; set; } = null!;
    public ITypeExpression? TypeAnnotation { get; set; }

    public string TokenLiteral() => Name.TokenLiteral();
    public string String()
    {
        if (TypeAnnotation is null)
        {
            return Name.String();
        }

        return $"{Name.String()}: {TypeAnnotation.String()}";
    }
}

public class CallExpression : IExpression
{
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

public class IntrinsicCallExpression : IExpression
{
    public Token Token { get; set; } // synthetic intrinsic token
    public string Name { get; set; } = "";
    public List<IExpression> Arguments { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var args = string.Join(", ", Arguments.Select(a => a.String()));
        return $"@{Name}({args})";
    }
}

public class StringLiteral : IExpression
{
    public Token Token { get; set; }
    public string Value { get; set; } = "";

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class CharLiteral : IExpression
{
    public Token Token { get; set; }
    public char Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => $"'{Token.Literal}'";
}

public class DoubleLiteral : IExpression
{
    public Token Token { get; set; }
    public double Value { get; set; }

    public string TokenLiteral() => Token.Literal;
    public string String() => Token.Literal;
}

public class ArrayLiteral : IExpression
{
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
    public Token Token { get; set; } // The [ token
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
    public Token Token { get; set; } // the '{' token
    public List<KeyValuePair<IExpression, IExpression>> Pairs { get; set; } = [];

    public string TokenLiteral() => Token.Literal;

    public string String()
    {
        var pairs = Pairs.Select(p => $"{p.Key.String()}:{p.Value.String()}");
        return $"{{{string.Join(", ", pairs)}}}";
    }
}
