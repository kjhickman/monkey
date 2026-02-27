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

        return SetType(program, lastType, result);
    }

    private KongType InferStatement(IStatement statement, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        return statement switch
        {
            ExpressionStatement es when es.Expression is not null => InferExpressionStatement(es, result, env),
            LetStatement ls when ls.Value is not null => InferLetStatement(ls, result, env),
            BlockStatement bs => InferBlockStatement(bs, result, env),
            _ => AddErrorAndSetType(statement, $"Unsupported statement type: {statement.GetType().Name}", KongType.Unknown, result),
        };
    }

    private KongType InferExpression(IExpression expression, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        return expression switch
        {
            IntegerLiteral => SetType(expression, KongType.Int64, result),
            BooleanLiteral => SetType(expression, KongType.Boolean, result),
            StringLiteral => SetType(expression, KongType.String, result),
            ArrayLiteral arrayLit => InferArrayLiteral(arrayLit, result, env),
            HashLiteral hashLit => InferHashLiteral(hashLit, result, env),
            InfixExpression infix => InferInfix(infix, result, env),
            Identifier identifier => InferIdentifier(identifier, result, env),
            PrefixExpression prefix when prefix.Operator == "-" => InferNegationPrefix(prefix, result, env),
            PrefixExpression prefix when prefix.Operator == "!" => InferBangPrefix(prefix, result, env),
            IfExpression ifExpr => InferIfExpression(ifExpr, result, env),
            IndexExpression indexExpr => InferIndexExpression(indexExpr, result, env),
            _ => AddErrorAndSetType(expression, $"Unsupported expression: {expression.GetType().Name}", KongType.Unknown, result),
        };
    }

    private KongType InferInfix(InfixExpression infix, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var leftType = InferExpression(infix.Left, result, env);
        var rightType = InferExpression(infix.Right, result, env);

        if (infix.Operator == "+")
        {
            if (leftType == KongType.String && rightType == KongType.String)
            {
                return SetType(infix, KongType.String, result);
            }

            if (leftType == KongType.Int64 && rightType == KongType.Int64)
            {
                return SetType(infix, KongType.Int64, result);
            }

            return AddErrorAndSetType(infix, $"Type error: cannot apply operator '+' to types {leftType} and {rightType}", KongType.Unknown, result);
        }
        
        if (infix.Operator is "-" or "*" or "/")
        {
            if (leftType != KongType.Int64 || rightType != KongType.Int64)
            {
                return AddErrorAndSetType(infix, $"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}", KongType.Unknown, result);
            }

            return SetType(infix, KongType.Int64, result);
        }

        if (infix.Operator is "==" or "!=" or "<" or ">")
        {
            if (leftType != rightType)
            {
                return AddErrorAndSetType(infix, $"Type error: cannot compare types {leftType} and {rightType} with operator '{infix.Operator}'", KongType.Unknown, result);
            }

            return SetType(infix, KongType.Boolean, result);
        }
        
        return AddErrorAndSetType(infix, $"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}", KongType.Unknown, result);
    }

    private KongType InferExpressionStatement(ExpressionStatement expressionStatement, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var expressionType = InferExpression(expressionStatement.Expression!, result, env);
        return SetType(expressionStatement, expressionType, result);
    }

    private KongType InferLetStatement(LetStatement letStatement, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var valueType = InferExpression(letStatement.Value!, result, env);
        if (valueType == KongType.Void)
        {
            SetType(letStatement.Name, KongType.Unknown, result);
            return AddErrorAndSetType(letStatement, "Type error: cannot assign an expression with no value", KongType.Unknown, result);
        }

        env[letStatement.Name.Value] = valueType;
        SetType(letStatement.Name, valueType, result);
        return SetType(letStatement, valueType, result);
    }

    private KongType InferBlockStatement(BlockStatement block, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var lastType = KongType.Unknown;
        foreach (var statement in block.Statements)
        {
            lastType = InferNode(statement, result, env);
        }

        return SetType(block, lastType, result);
    }

    private KongType InferArrayLiteral(ArrayLiteral arrayLiteral, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        if (arrayLiteral.Elements.Count == 0)
        {
            return SetType(arrayLiteral, KongType.Array, result);
        }

        var elementType = InferExpression(arrayLiteral.Elements[0], result, env);
        foreach (var element in arrayLiteral.Elements.Skip(1))
        {
            var currentType = InferExpression(element, result, env);
            if (currentType != elementType)
            {
                return AddErrorAndSetType(arrayLiteral, $"Type error: array elements must have the same type, but found {elementType} and {currentType}", KongType.Unknown, result);
            }
        }

        return SetType(arrayLiteral, KongType.Array, result);
    }

    private KongType InferHashLiteral(HashLiteral hashLit, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        if (hashLit.Pairs.Count == 0)
        {
            return SetType(hashLit, KongType.HashMap, result);
        }

        var keyType = InferExpression(hashLit.Pairs[0].Key, result, env);
        var valueType = InferExpression(hashLit.Pairs[0].Value, result, env);
        foreach (var pair in hashLit.Pairs.Skip(1))
        {
            var currentKeyType = InferExpression(pair.Key, result, env);
            var currentValueType = InferExpression(pair.Value, result, env);
            if (currentKeyType != keyType || currentValueType != valueType)
            {
                return AddErrorAndSetType(hashLit, $"Type error: hash map keys and values must have the same type, but found ({keyType}, {valueType}) and ({currentKeyType}, {currentValueType})", KongType.Unknown, result);
            }
        }

        return SetType(hashLit, KongType.HashMap, result);
    }

    private KongType InferIdentifier(Identifier identifier, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        if (env.TryGetValue(identifier.Value, out var identifierType))
        {
            return SetType(identifier, identifierType, result);
        }

        return AddErrorAndSetType(identifier, $"Undefined variable: {identifier.Value}", KongType.Unknown, result);
    }

    private KongType InferNegationPrefix(PrefixExpression prefix, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var rightType = InferExpression(prefix.Right, result, env);
        if (rightType != KongType.Int64)
        {
            return AddErrorAndSetType(prefix, $"Type error: cannot apply operator '-' to type {rightType}", KongType.Unknown, result);
        }

        return SetType(prefix, KongType.Int64, result);
    }

    private KongType InferBangPrefix(PrefixExpression prefix, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var operandType = InferExpression(prefix.Right, result, env);
        if (operandType != KongType.Boolean)
        {
            return AddErrorAndSetType(prefix, $"Type error: cannot apply operator '!' to type {operandType}", KongType.Unknown, result);
        }

        return SetType(prefix, KongType.Boolean, result);
    }

    private KongType InferIfExpression(IfExpression ifExpression, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var conditionType = InferExpression(ifExpression.Condition, result, env);
        if (conditionType != KongType.Boolean)
        {
            return AddErrorAndSetType(ifExpression, $"Type error: if condition must be of type Boolean, but got {conditionType}", KongType.Unknown, result);
        }

        var thenType = InferStatement(ifExpression.Consequence, result, env);
        if (ifExpression.Alternative is null)
        {
            return SetType(ifExpression, KongType.Void, result);
        }

        var elseType = InferStatement(ifExpression.Alternative, result, env);
        if (thenType != elseType)
        {
            return AddErrorAndSetType(ifExpression, $"Type error: if branches must have the same type, but got {thenType} and {elseType}", KongType.Unknown, result);
        }

        if (thenType == KongType.Void)
        {
            return AddErrorAndSetType(ifExpression, "Type error: if/else used as an expression must produce a value", KongType.Unknown, result);
        }

        return SetType(ifExpression, thenType, result);
    }

    private KongType InferIndexExpression(IndexExpression indexExpression, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var leftType = InferExpression(indexExpression.Left, result, env);
        if (leftType is not KongType.Array and not KongType.HashMap and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: index operator not supported for type {leftType}", KongType.Unknown, result);
        }

        var indexType = InferExpression(indexExpression.Index, result, env);
        if (leftType == KongType.Array && indexType is not KongType.Int64 and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: array index must be Int64, but got {indexType}", KongType.Unknown, result);
        }

        if (leftType == KongType.HashMap && indexType is not KongType.Int64 and not KongType.Boolean and not KongType.String and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: hash map index must be Int64, Boolean, or String, but got {indexType}", KongType.Unknown, result);
        }

        return SetType(indexExpression, KongType.Unknown, result);
    }

    private static KongType SetType(INode node, KongType type, TypeInferenceResult result)
    {
        result.AddNodeType(node, type);
        return type;
    }

    private static KongType AddErrorAndSetType(INode node, string error, KongType type, TypeInferenceResult result)
    {
        result.AddError(error);
        return SetType(node, type, result);
    }

    private KongType InferUnsupported(INode node, TypeInferenceResult result)
    {
        return AddErrorAndSetType(node, $"Unsupported node type: {node.GetType().Name}", KongType.Unknown, result);
    }
}
