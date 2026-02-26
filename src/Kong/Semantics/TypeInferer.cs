using Kong.Parsing;

namespace Kong.Semantics;

public class TypeInferer
{
    public TypeInferenceResult InferTypes(Program root)
    {
        var result = new TypeInferenceResult();
        InferNode(root, result);
        return result;
    }

    private KongType InferNode(INode node, TypeInferenceResult result)
    {
        return node switch
        {
            Program p => InferProgram(p, result),
            IStatement s => InferStatement(s, result),
            IExpression e => InferExpression(e, result),
            _ => InferUnsupported(node, result),
        };
    }

    private KongType InferProgram(Program program, TypeInferenceResult result)
    {
        var lastType = KongType.Unknown;
        foreach (var statement in program.Statements)
        {
            lastType = InferNode(statement, result);
        }

        result.AddNodeType(program, lastType);
        return lastType;
    }

    private KongType InferStatement(IStatement statement, TypeInferenceResult result)
    {
        switch (statement)
        {
            case ExpressionStatement es when es.Expression is not null:
                var expressionType = InferExpression(es.Expression, result);
                result.AddNodeType(statement, expressionType);
                return expressionType;
            
            default:
                result.AddError($"Unsupported statement type: {statement.GetType().Name}");
                result.AddNodeType(statement, KongType.Unknown);
                return KongType.Unknown;
        }
    }

    private KongType InferExpression(IExpression expression, TypeInferenceResult result)
    {
        switch (expression)
        {
            case IntegerLiteral:
                result.AddNodeType(expression, KongType.Int64);
                return KongType.Int64;

            case InfixExpression infix:
                return InferInfix(infix, result);

            default:
                result.AddError($"Unsupported expression: {expression.GetType().Name}");
                result.AddNodeType(expression, KongType.Unknown);
                return KongType.Unknown;
        }
    }

    private KongType InferInfix(InfixExpression infix, TypeInferenceResult result)
    {
        var leftType = InferExpression(infix.Left, result);
        var rightType = InferExpression(infix.Right, result);
        
        var isArithmetic = infix.Operator is "+" or "-" or "*" or "/";
        if (!isArithmetic)
        {
            result.AddError($"Unsupported infix operator: {infix.Operator}");
            result.AddNodeType(infix, KongType.Unknown);
            return KongType.Unknown;
        }

        if (leftType != KongType.Int64 || rightType != KongType.Int64)
        {
            result.AddError($"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}");
            result.AddNodeType(infix, KongType.Unknown);
            return KongType.Unknown;
        }

        result.AddNodeType(infix, KongType.Int64);
        return KongType.Int64;
    }

    private KongType InferUnsupported(INode node, TypeInferenceResult result)
    {
        result.AddError($"Unsupported node type: {node.GetType().Name}");
        result.AddNodeType(node, KongType.Unknown);
        return KongType.Unknown;
    }
}
