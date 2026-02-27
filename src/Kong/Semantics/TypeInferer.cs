using Kong.Parsing;

namespace Kong.Semantics;

public class TypeInferer
{
    public TypeInferenceResult InferTypes(Program root)
    {
        var result = new TypeInferenceResult();
        var env = new Dictionary<string, KongType>();
        InferNode(root, result, env);
        return result;
    }

    private KongType InferNode(INode node, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        return node switch
        {
            Program p => InferProgram(p, result, env),
            IStatement s => InferStatement(s, result, env),
            IExpression e => InferExpression(e, result, env),
            _ => InferUnsupported(node, result),
        };
    }

    private KongType InferProgram(Program program, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var lastType = KongType.Unknown;
        foreach (var statement in program.Statements)
        {
            lastType = InferNode(statement, result, env);
        }

        result.AddNodeType(program, lastType);
        return lastType;
    }

    private KongType InferStatement(IStatement statement, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        switch (statement)
        {
            case ExpressionStatement es when es.Expression is not null:
                var expressionType = InferExpression(es.Expression, result, env);
                result.AddNodeType(statement, expressionType);
                return expressionType;

            case LetStatement ls when ls.Value is not null:
                var valueType = InferExpression(ls.Value, result, env);
                if (valueType == KongType.Void)
                {
                    result.AddError("Type error: cannot assign an expression with no value");
                    result.AddNodeType(ls.Name, KongType.Unknown);
                    result.AddNodeType(statement, KongType.Unknown);
                    return KongType.Unknown;
                }
                env[ls.Name.Value] = valueType;
                result.AddNodeType(ls.Name, valueType);
                result.AddNodeType(statement, valueType);
                return valueType;

            case BlockStatement bs:
                var lastType = KongType.Unknown;
                foreach (var stmt in bs.Statements)
                {
                    lastType = InferNode(stmt, result, env);
                }
                result.AddNodeType(statement, lastType);
                return lastType;
            
            default:
                result.AddError($"Unsupported statement type: {statement.GetType().Name}");
                result.AddNodeType(statement, KongType.Unknown);
                return KongType.Unknown;
        }
    }

    private KongType InferExpression(IExpression expression, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        switch (expression)
        {
            case IntegerLiteral:
                result.AddNodeType(expression, KongType.Int64);
                return KongType.Int64;

            case BooleanLiteral:
                result.AddNodeType(expression, KongType.Boolean);
                return KongType.Boolean;

            case StringLiteral:
                result.AddNodeType(expression, KongType.String);
                return KongType.String;

            case InfixExpression infix:
                return InferInfix(infix, result, env);

            case Identifier identifier:
                if (env.TryGetValue(identifier.Value, out var identType))
                {
                    result.AddNodeType(expression, identType);
                    return identType;
                }
                result.AddError($"Undefined variable: {identifier.Value}");
                result.AddNodeType(expression, KongType.Unknown);
                return KongType.Unknown;
            
            case PrefixExpression prefix when prefix.Operator == "-":
                var rightType = InferExpression(prefix.Right, result, env);
                if (rightType != KongType.Int64)
                {
                    result.AddError($"Type error: cannot apply operator '-' to type {rightType}");
                    result.AddNodeType(expression, KongType.Unknown);
                    return KongType.Unknown;
                }
                result.AddNodeType(expression, KongType.Int64);
                return KongType.Int64;

            case PrefixExpression prefix when prefix.Operator == "!":
                var operandType = InferExpression(prefix.Right, result, env);
                if (operandType != KongType.Boolean)
                {
                    result.AddError($"Type error: cannot apply operator '!' to type {operandType}");
                    result.AddNodeType(expression, KongType.Unknown);
                    return KongType.Unknown;
                }
                result.AddNodeType(expression, KongType.Boolean);
                return KongType.Boolean;

            case IfExpression ifExpr:
                var conditionType = InferExpression(ifExpr.Condition, result, env);
                if (conditionType != KongType.Boolean)
                {
                    result.AddError($"Type error: if condition must be of type Boolean, but got {conditionType}");
                    result.AddNodeType(expression, KongType.Unknown);
                    return KongType.Unknown;
                }

                var thenType = InferStatement(ifExpr.Consequence, result, env);

                if (ifExpr.Alternative is null)
                {
                    result.AddNodeType(expression, KongType.Void);
                    return KongType.Void;
                }
                var elseType = InferStatement(ifExpr.Alternative, result, env);

                if (thenType != elseType)
                {
                    result.AddError($"Type error: if branches must have the same type, but got {thenType} and {elseType}");
                    result.AddNodeType(expression, KongType.Unknown);
                    return KongType.Unknown;
                }

                if (thenType == KongType.Void)
                {
                    result.AddError("Type error: if/else used as an expression must produce a value");
                    result.AddNodeType(expression, KongType.Unknown);
                    return KongType.Unknown;
                }

                result.AddNodeType(expression, thenType);
                return thenType;

            default:
                result.AddError($"Unsupported expression: {expression.GetType().Name}");
                result.AddNodeType(expression, KongType.Unknown);
                return KongType.Unknown;
        }
    }

    private KongType InferInfix(InfixExpression infix, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var leftType = InferExpression(infix.Left, result, env);
        var rightType = InferExpression(infix.Right, result, env);

        if (infix.Operator == "+")
        {
            if (leftType == KongType.String && rightType == KongType.String)
            {
                result.AddNodeType(infix, KongType.String);
                return KongType.String;
            }

            if (leftType == KongType.Int64 && rightType == KongType.Int64)
            {
                result.AddNodeType(infix, KongType.Int64);
                return KongType.Int64;
            }

            result.AddError($"Type error: cannot apply operator '+' to types {leftType} and {rightType}");
            result.AddNodeType(infix, KongType.Unknown);
            return KongType.Unknown;
        }
        
        if (infix.Operator is "-" or "*" or "/")
        {
            if (leftType != KongType.Int64 || rightType != KongType.Int64)
            {
                result.AddError($"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}");
                result.AddNodeType(infix, KongType.Unknown);
                return KongType.Unknown;
            }

            result.AddNodeType(infix, KongType.Int64);
            return KongType.Int64;
        }

        if (infix.Operator is "==" or "!=" or "<" or ">")
        {
            if (leftType != rightType)
            {
                result.AddError($"Type error: cannot compare types {leftType} and {rightType} with operator '{infix.Operator}'");
                result.AddNodeType(infix, KongType.Unknown);
                return KongType.Unknown;
            }

            result.AddNodeType(infix, KongType.Boolean);
            return KongType.Boolean;
        }
        
        result.AddError($"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}");
        result.AddNodeType(infix, KongType.Unknown);
        return KongType.Unknown;
    }

    private KongType InferUnsupported(INode node, TypeInferenceResult result)
    {
        result.AddError($"Unsupported node type: {node.GetType().Name}");
        result.AddNodeType(node, KongType.Unknown);
        return KongType.Unknown;
    }
}
