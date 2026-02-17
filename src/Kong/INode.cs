namespace Kong;

public interface INode
{
    global::Kong.Span Span { get; set; }
    string TokenLiteral();
    string String();
}

public interface IStatement : INode;

public interface IExpression : INode;
