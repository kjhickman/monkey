namespace Kong.Ast;

public interface INode
{
    Token.Span Span { get; set; }
    string TokenLiteral();
    string String();
}

public interface IStatement : INode;

public interface IExpression : INode;
