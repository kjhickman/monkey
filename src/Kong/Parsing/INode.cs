using Kong.Lexing;

namespace Kong.Parsing;

public interface INode
{
    Token Token { get; set; }
    string TokenLiteral();
    string String();
}

public interface IStatement : INode;

public interface IExpression : INode;
