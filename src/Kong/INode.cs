namespace Kong;

public interface INode
{
    Span Span { get; set; }
    string TokenLiteral();
    string String();
}

public interface IStatement : INode;

public interface IExpression : INode;

public interface ITypeNode : INode;
