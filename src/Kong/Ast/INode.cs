namespace Kong.Ast;

public interface INode
{
    string TokenLiteral();
    string String();
}

public interface IStatement : INode
{
}

public interface IExpression : INode
{
}
