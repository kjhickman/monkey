using Kong.Lexing;
using Kong.Semantics;

namespace Kong.Lowering;

public sealed class Program
{
    public List<IStatement> Statements { get; set; } = [];

    public Dictionary<string, FunctionSignature> FunctionSignatures { get; set; } = new(StringComparer.Ordinal);
}

public sealed record FunctionSignature(string Name, IReadOnlyList<KongType> ParameterTypes, KongType ReturnType);

public interface INode;
public interface IStatement : INode;
public interface IExpression : INode
{
    KongType Type { get; }
}

public sealed class LetStatement : IStatement
{
    public Token Token { get; set; }
    public Identifier Name { get; set; } = null!;
    public IExpression Value { get; set; } = null!;
}

public sealed class AssignStatement : IStatement
{
    public Token Token { get; set; }
    public Identifier Name { get; set; } = null!;
    public IExpression Value { get; set; } = null!;
}

public sealed class ReturnStatement : IStatement
{
    public Token Token { get; set; }
    public IExpression ReturnValue { get; set; } = null!;
}

public sealed class ExpressionStatement : IStatement
{
    public Token Token { get; set; }
    public IExpression Expression { get; set; } = null!;
}

public sealed class IfStatement : IStatement
{
    public Token Token { get; set; }
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Consequence { get; set; } = null!;
    public BlockStatement Alternative { get; set; } = null!;
}

public sealed class BlockStatement : IStatement
{
    public Token Token { get; set; }
    public List<IStatement> Statements { get; set; } = [];
}

public sealed class Identifier : IExpression
{
    public Token Token { get; set; }
    public string Value { get; set; } = "";
    public KongType Type { get; set; }
}

public sealed class IntegerLiteral : IExpression
{
    public Token Token { get; set; }
    public long Value { get; set; }
    public KongType Type => KongType.Int64;
}

public sealed class BooleanLiteral : IExpression
{
    public Token Token { get; set; }
    public bool Value { get; set; }
    public KongType Type => KongType.Boolean;
}

public sealed class CharLiteral : IExpression
{
    public Token Token { get; set; }
    public char Value { get; set; }
    public KongType Type => KongType.Char;
}

public sealed class StringLiteral : IExpression
{
    public Token Token { get; set; }
    public string Value { get; set; } = "";
    public KongType Type => KongType.String;
}

public sealed class PrefixExpression : IExpression
{
    public Token Token { get; set; }
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;
    public KongType Type { get; set; }
}

public sealed class InfixExpression : IExpression
{
    public Token Token { get; set; }
    public IExpression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public IExpression Right { get; set; } = null!;
    public KongType Type { get; set; }
}

public sealed class IfExpression : IExpression
{
    public Token Token { get; set; }
    public IExpression Condition { get; set; } = null!;
    public BlockStatement Consequence { get; set; } = null!;
    public BlockStatement? Alternative { get; set; }
    public KongType Type { get; set; }
}

public sealed class ArrayLiteral : IExpression
{
    public Token Token { get; set; }
    public List<IExpression> Elements { get; set; } = [];
    public KongType Type => KongType.Array;
}

public sealed class HashLiteral : IExpression
{
    public Token Token { get; set; }
    public List<KeyValuePair<IExpression, IExpression>> Pairs { get; set; } = [];
    public KongType Type => KongType.HashMap;
}

public sealed class IndexExpression : IExpression
{
    public Token Token { get; set; }
    public IExpression Left { get; set; } = null!;
    public IExpression Index { get; set; } = null!;
    public KongType Type { get; set; }
}

public sealed class CallExpression : IExpression
{
    public Token Token { get; set; }
    public IExpression Function { get; set; } = null!;
    public List<IExpression> Arguments { get; set; } = [];
    public KongType Type { get; set; }
}

public sealed class IntrinsicCallExpression : IExpression
{
    public Token Token { get; set; }
    public string Name { get; set; } = "";
    public List<IExpression> Arguments { get; set; } = [];
    public KongType Type { get; set; }
}

public sealed class FunctionParameter
{
    public Identifier Name { get; set; } = null!;
}

public sealed class FunctionLiteral : IExpression
{
    public Token Token { get; set; }
    public List<FunctionParameter> Parameters { get; set; } = [];
    public BlockStatement Body { get; set; } = null!;
    public string Name { get; set; } = "";
    public List<string> Captures { get; set; } = [];
    public KongType Type { get; set; }
}
