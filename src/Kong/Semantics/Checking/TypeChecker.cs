using Kong.Diagnostics;
using Kong.Parsing;
using Kong.Semantics.Binding;
using Kong.Semantics.Symbols;

namespace Kong.Semantics.Checking;

public sealed class TypeChecker
{
    public TypeCheckResult Check(BoundProgram boundProgram)
    {
        var topLevelFunctions = boundProgram.Statements
            .OfType<BoundLetStatement>()
            .Select(s => s.Value)
            .OfType<BoundFunctionExpression>()
            .Where(f => !string.IsNullOrEmpty(f.Symbol.Name) && f.Symbol.Name != "<anonymous>")
            .ToList();

        Dictionary<string, TypeSymbol>? seededReturns = null;
        var maxIterations = Math.Max(1, topLevelFunctions.Count + 1);
        for (var i = 0; i < maxIterations; i++)
        {
            var passResult = CheckPass(boundProgram, seededReturns);
            var currentReturns = BuildReturnTypeMap(topLevelFunctions);

            if (seededReturns is not null && SameReturns(seededReturns, currentReturns))
            {
                seededReturns = currentReturns;
                break;
            }

            seededReturns = currentReturns;
        }

        var final = CheckPass(boundProgram, seededReturns);
        boundProgram.SetTypeInfo(final.TypeInfo);
        return final;
    }

    private static Dictionary<string, TypeSymbol> BuildReturnTypeMap(IEnumerable<BoundFunctionExpression> functions)
    {
        var result = new Dictionary<string, TypeSymbol>();
        foreach (var function in functions)
        {
            result[function.Symbol.Name] = function.Symbol.ReturnType;
        }

        return result;
    }

    private static bool SameReturns(IReadOnlyDictionary<string, TypeSymbol> left, IReadOnlyDictionary<string, TypeSymbol> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, type) in left)
        {
            if (!right.TryGetValue(name, out var rightType) || rightType != type)
            {
                return false;
            }
        }

        return true;
    }

    private TypeCheckResult CheckPass(BoundProgram boundProgram, IReadOnlyDictionary<string, TypeSymbol>? seededReturns)
    {
        var result = new SemanticModel();

        if (seededReturns is not null)
        {
            foreach (var boundFunction in boundProgram.Functions.Values)
            {
                if (seededReturns.TryGetValue(boundFunction.Symbol.Name, out var returnType))
                {
                    boundFunction.Symbol.SetReturnType(returnType);
                }
                else
                {
                    boundFunction.Symbol.SetReturnType(TypeSymbol.Unknown);
                }
            }
        }

        foreach (var statement in boundProgram.Statements)
        {
            InferStatement(statement, result);
        }

        foreach (var topLevelFunction in boundProgram.Statements
                     .OfType<BoundLetStatement>()
                     .Select(s => s.Value)
                     .OfType<BoundFunctionExpression>())
        {
            if (!string.IsNullOrEmpty(topLevelFunction.Symbol.Name) && topLevelFunction.Symbol.Name != "<anonymous>")
            {
                result.AddFunctionSignature(
                    topLevelFunction.Symbol.Name,
                    topLevelFunction.Symbol.Parameters.Select(p => p.Type).ToList(),
                    topLevelFunction.Symbol.ReturnType);
            }
        }

        return new TypeCheckResult(result, result.GetErrors().ToList());
    }

    private TypeSymbol InferStatement(BoundStatement statement, SemanticModel result)
    {
        return statement switch
        {
            BoundExpressionStatement expressionStatement => SetType(statement.Syntax, InferExpression(expressionStatement.Expression, result), result),
            BoundLetStatement letStatement => InferLetStatement(letStatement, result),
            BoundReturnStatement returnStatement => SetType(statement.Syntax, InferExpression(returnStatement.Value, result), result),
            BoundBlockStatement blockStatement => InferBlockStatement(blockStatement, result),
            _ => AddErrorAndSetType(statement.Syntax, $"Unsupported statement type: {statement.GetType().Name}", TypeSymbol.Unknown, result),
        };
    }

    private TypeSymbol InferLetStatement(BoundLetStatement letStatement, SemanticModel result)
    {
        var valueType = InferExpression(letStatement.Value, result);
        if (letStatement.Variable is not null)
        {
            if (valueType == TypeSymbol.Void)
            {
                return AddErrorAndSetType(letStatement.Syntax, "Type error: cannot assign an expression with no value", TypeSymbol.Unknown, result);
            }

            letStatement.Variable.SetType(valueType);
        }

        return SetType(letStatement.Syntax, valueType, result);
    }

    private TypeSymbol InferBlockStatement(BoundBlockStatement blockStatement, SemanticModel result)
    {
        var last = TypeSymbol.Unknown;
        foreach (var statement in blockStatement.Statements)
        {
            last = InferStatement(statement, result);
        }

        return SetType(blockStatement.Syntax, last, result);
    }

    private TypeSymbol InferExpression(BoundExpression expression, SemanticModel result)
    {
        return expression switch
        {
            BoundIntegerLiteralExpression => SetType(expression.Syntax, TypeSymbol.Int, result),
            BoundBooleanLiteralExpression => SetType(expression.Syntax, TypeSymbol.Bool, result),
            BoundCharLiteralExpression => SetType(expression.Syntax, TypeSymbol.Char, result),
            BoundDoubleLiteralExpression => SetType(expression.Syntax, TypeSymbol.Double, result),
            BoundStringLiteralExpression => SetType(expression.Syntax, TypeSymbol.String, result),
            BoundIdentifierExpression identifier => InferIdentifier(identifier, result),
            BoundArrayLiteralExpression arrayLiteral => InferArrayLiteral(arrayLiteral, result),
            BoundHashLiteralExpression hashLiteral => InferHashLiteral(hashLiteral, result),
            BoundPrefixExpression prefix => InferPrefix(prefix, result),
            BoundInfixExpression infix => InferInfix(infix, result),
            BoundIfExpression ifExpression => InferIf(ifExpression, result),
            BoundIndexExpression indexExpression => InferIndex(indexExpression, result),
            BoundFunctionExpression functionExpression => InferFunction(functionExpression, result),
            BoundCallExpression callExpression => InferCall(callExpression, result),
            _ => AddErrorAndSetType(expression.Syntax, $"Unsupported expression type: {expression.GetType().Name}", TypeSymbol.Unknown, result),
        };
    }

    private TypeSymbol InferIdentifier(BoundIdentifierExpression identifierExpression, SemanticModel result)
    {
        if (identifierExpression.Symbol is VariableSymbol variable)
        {
            return SetType(identifierExpression.Syntax, variable.Type, result);
        }

        if (identifierExpression.Symbol is FunctionSymbol function)
        {
            return SetType(identifierExpression.Syntax, function.Type, result);
        }

        return AddErrorAndSetType(identifierExpression.Syntax, $"Undefined variable: {((Identifier)identifierExpression.Syntax).Value}", TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferArrayLiteral(BoundArrayLiteralExpression arrayLiteral, SemanticModel result)
    {
        if (arrayLiteral.Elements.Count == 0)
        {
            return SetType(arrayLiteral.Syntax, TypeSymbol.Array, result);
        }

        var firstType = InferExpression(arrayLiteral.Elements[0], result);
        foreach (var element in arrayLiteral.Elements.Skip(1))
        {
            var currentType = InferExpression(element, result);
            if (!firstType.IsCompatibleWith(currentType) || !currentType.IsCompatibleWith(firstType))
            {
                return AddErrorAndSetType(arrayLiteral.Syntax, $"Type error: array elements must have the same type, but found {firstType} and {currentType}", TypeSymbol.Unknown, result);
            }
        }

        return SetType(arrayLiteral.Syntax, TypeSymbol.ArrayOf(firstType), result);
    }

    private TypeSymbol InferHashLiteral(BoundHashLiteralExpression hashLiteral, SemanticModel result)
    {
        if (hashLiteral.Pairs.Count == 0)
        {
            return SetType(hashLiteral.Syntax, TypeSymbol.HashMap, result);
        }

        var keyType = InferExpression(hashLiteral.Pairs[0].Key, result);
        var valueType = InferExpression(hashLiteral.Pairs[0].Value, result);
        foreach (var (key, value) in hashLiteral.Pairs.Skip(1))
        {
            var currentKeyType = InferExpression(key, result);
            var currentValueType = InferExpression(value, result);
            if (!keyType.IsCompatibleWith(currentKeyType)
                || !currentKeyType.IsCompatibleWith(keyType)
                || !valueType.IsCompatibleWith(currentValueType)
                || !currentValueType.IsCompatibleWith(valueType))
            {
                return AddErrorAndSetType(hashLiteral.Syntax, $"Type error: hash map keys and values must have the same type, but found ({keyType}, {valueType}) and ({currentKeyType}, {currentValueType})", TypeSymbol.Unknown, result);
            }
        }

        return SetType(hashLiteral.Syntax, TypeSymbol.MapOf(keyType, valueType), result);
    }

    private TypeSymbol InferPrefix(BoundPrefixExpression prefixExpression, SemanticModel result)
    {
        var operandType = InferExpression(prefixExpression.Right, result);
        var prefixSyntax = (PrefixExpression)prefixExpression.Syntax;
        if (prefixSyntax.Operator == "-")
        {
            if (operandType != TypeSymbol.Int)
            {
                return AddErrorAndSetType(prefixExpression.Syntax, $"Type error: cannot apply operator '-' to type {operandType}", TypeSymbol.Unknown, result);
            }

            return SetType(prefixExpression.Syntax, TypeSymbol.Int, result);
        }

        if (prefixSyntax.Operator == "!")
        {
            if (operandType != TypeSymbol.Bool)
            {
                return AddErrorAndSetType(prefixExpression.Syntax, $"Type error: cannot apply operator '!' to type {operandType}", TypeSymbol.Unknown, result);
            }

            return SetType(prefixExpression.Syntax, TypeSymbol.Bool, result);
        }

        return AddErrorAndSetType(prefixExpression.Syntax, $"Unsupported prefix operator: {prefixSyntax.Operator}", TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferInfix(BoundInfixExpression infixExpression, SemanticModel result)
    {
        var leftType = InferExpression(infixExpression.Left, result);
        var rightType = InferExpression(infixExpression.Right, result);
        var op = ((InfixExpression)infixExpression.Syntax).Operator;

        if (op == "+")
        {
            if (leftType == TypeSymbol.String && rightType == TypeSymbol.String)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.String, result);
            }

            if (leftType == TypeSymbol.Int && rightType == TypeSymbol.Int)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Int, result);
            }

            if (leftType == TypeSymbol.Double && rightType == TypeSymbol.Double)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Double, result);
            }

            if ((leftType == TypeSymbol.Int && rightType == TypeSymbol.Unknown)
                || (leftType == TypeSymbol.Unknown && rightType == TypeSymbol.Int))
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Int, result);
            }

            if ((leftType == TypeSymbol.Double && rightType == TypeSymbol.Unknown)
                || (leftType == TypeSymbol.Unknown && rightType == TypeSymbol.Double))
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Double, result);
            }

            if ((leftType == TypeSymbol.String && rightType == TypeSymbol.Unknown)
                || (leftType == TypeSymbol.Unknown && rightType == TypeSymbol.String))
            {
                return SetType(infixExpression.Syntax, TypeSymbol.String, result);
            }

            return AddErrorAndSetType(infixExpression.Syntax, $"Type error: cannot apply operator '+' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
        }

        if (op is "-" or "*" or "/" or "%")
        {
            if (leftType == TypeSymbol.Unknown || rightType == TypeSymbol.Unknown)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Int, result);
            }

            if (leftType == TypeSymbol.Double && rightType == TypeSymbol.Double)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Double, result);
            }

            if (leftType != TypeSymbol.Int || rightType != TypeSymbol.Int)
            {
                return AddErrorAndSetType(infixExpression.Syntax, $"Type error: cannot apply operator '{op}' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
            }

            return SetType(infixExpression.Syntax, TypeSymbol.Int, result);
        }

        if (op is "==" or "!=" or "<" or ">")
        {
            if (leftType == TypeSymbol.Unknown || rightType == TypeSymbol.Unknown)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Bool, result);
            }

            if (!leftType.IsCompatibleWith(rightType) || !rightType.IsCompatibleWith(leftType))
            {
                return AddErrorAndSetType(infixExpression.Syntax, $"Type error: cannot compare types {leftType} and {rightType} with operator '{op}'", TypeSymbol.Unknown, result);
            }

            return SetType(infixExpression.Syntax, TypeSymbol.Bool, result);
        }

        if (op is "&&" or "||")
        {
            if (leftType == TypeSymbol.Unknown || rightType == TypeSymbol.Unknown)
            {
                return SetType(infixExpression.Syntax, TypeSymbol.Bool, result);
            }

            if (leftType != TypeSymbol.Bool)
            {
                return AddErrorAndSetType(infixExpression.Syntax, $"Type error: left operand of '{op}' must be Boolean, but got {leftType}", TypeSymbol.Unknown, result);
            }

            if (rightType != TypeSymbol.Bool)
            {
                return AddErrorAndSetType(infixExpression.Syntax, $"Type error: right operand of '{op}' must be Boolean, but got {rightType}", TypeSymbol.Unknown, result);
            }

            return SetType(infixExpression.Syntax, TypeSymbol.Bool, result);
        }

        return AddErrorAndSetType(infixExpression.Syntax, $"Type error: cannot apply operator '{op}' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferIf(BoundIfExpression ifExpression, SemanticModel result)
    {
        var conditionType = InferExpression(ifExpression.Condition, result);
        if (conditionType != TypeSymbol.Bool)
        {
            return AddErrorAndSetType(ifExpression.Syntax, $"Type error: if condition must be of type Boolean, but got {conditionType}", TypeSymbol.Unknown, result);
        }

        var thenType = InferStatement(ifExpression.Consequence, result);
        if (ifExpression.Alternative is null)
        {
            return SetType(ifExpression.Syntax, TypeSymbol.Void, result);
        }

        var elseType = InferStatement(ifExpression.Alternative, result);
        if (!thenType.IsCompatibleWith(elseType) || !elseType.IsCompatibleWith(thenType))
        {
            return AddErrorAndSetType(ifExpression.Syntax, $"Type error: if branches must have the same type, but got {thenType} and {elseType}", TypeSymbol.Unknown, result);
        }

        if (thenType == TypeSymbol.Void)
        {
            return AddErrorAndSetType(ifExpression.Syntax, "Type error: if/else used as an expression must produce a value", TypeSymbol.Unknown, result);
        }

        return SetType(ifExpression.Syntax, thenType, result);
    }

    private TypeSymbol InferIndex(BoundIndexExpression indexExpression, SemanticModel result)
    {
        var leftType = InferExpression(indexExpression.Left, result);
        var leftKind = leftType.ToKongType();
        if (leftKind is not KongType.Array and not KongType.HashMap and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression.Syntax, $"Type error: index operator not supported for type {leftType}", TypeSymbol.Unknown, result);
        }

        var indexType = InferExpression(indexExpression.Index, result);
        var indexKind = indexType.ToKongType();

        if (leftType is MapTypeSymbol mapType)
        {
            var keyType = mapType.KeyType;
            if (keyType != TypeSymbol.Unknown
                && indexType != TypeSymbol.Unknown
                && (!keyType.IsCompatibleWith(indexType) || !indexType.IsCompatibleWith(keyType)))
            {
                return AddErrorAndSetType(indexExpression.Syntax, $"Type error: hash map index must be {keyType}, but got {indexType}", TypeSymbol.Unknown, result);
            }
        }

        if (leftKind == KongType.Array && indexKind is not KongType.Int64 and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression.Syntax, $"Type error: array index must be Int64, but got {indexType}", TypeSymbol.Unknown, result);
        }

        if (leftKind == KongType.HashMap && indexKind is not KongType.Int64 and not KongType.Boolean and not KongType.String and not KongType.Char and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression.Syntax, $"Type error: hash map index must be Int64, Boolean, or String, but got {indexType}", TypeSymbol.Unknown, result);
        }

        if (leftType is ArrayTypeSymbol arrayType)
        {
            return SetType(indexExpression.Syntax, arrayType.ElementType, result);
        }

        return SetType(indexExpression.Syntax, TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferFunction(BoundFunctionExpression functionExpression, SemanticModel result)
    {
        for (var i = 0; i < functionExpression.Symbol.Parameters.Count; i++)
        {
            var parameterType = functionExpression.Symbol.Parameters[i].Type;
            var syntax = ((FunctionLiteral)functionExpression.Syntax).Parameters[i].Name;
            SetType(syntax, parameterType, result);
        }

        var returnType = InferStatement(functionExpression.Body, result);
        functionExpression.Symbol.SetReturnType(returnType);

        var functionType = functionExpression.Symbol.Type;
        return SetType(functionExpression.Syntax, functionType, result);
    }

    private TypeSymbol InferCall(BoundCallExpression callExpression, SemanticModel result)
    {
        if (callExpression.Function.Syntax is Identifier { Value: "puts" })
        {
            foreach (var argument in callExpression.Arguments)
            {
                InferExpression(argument, result);
            }

            return SetType(callExpression.Syntax, TypeSymbol.Void, result);
        }

        if (callExpression.Function.Syntax is Identifier { Value: "len" })
        {
            if (callExpression.Arguments.Count != 1)
            {
                return AddErrorAndSetType(callExpression.Syntax, $"wrong number of arguments. got={callExpression.Arguments.Count}, want=1", TypeSymbol.Unknown, result);
            }

            var argumentType = InferExpression(callExpression.Arguments[0], result);
            var kind = argumentType.ToKongType();
            if (kind is KongType.String or KongType.Array or KongType.Unknown)
            {
                return SetType(callExpression.Syntax, TypeSymbol.Int, result);
            }

            return AddErrorAndSetType(callExpression.Syntax, $"argument to `len` not supported, got {argumentType}", TypeSymbol.Unknown, result);
        }

        if (callExpression.Function.Syntax is Identifier { Value: "push" })
        {
            if (callExpression.Arguments.Count != 2)
            {
                return AddErrorAndSetType(callExpression.Syntax, $"wrong number of arguments. got={callExpression.Arguments.Count}, want=2", TypeSymbol.Unknown, result);
            }

            var arrayType = InferExpression(callExpression.Arguments[0], result);
            InferExpression(callExpression.Arguments[1], result);
            var kind = arrayType.ToKongType();
            if (kind is KongType.Array or KongType.Unknown)
            {
                return SetType(callExpression.Syntax, kind == KongType.Array ? arrayType : TypeSymbol.Array, result);
            }

            return AddErrorAndSetType(callExpression.Syntax, $"argument to `push` must be ARRAY, got {arrayType}", TypeSymbol.Unknown, result);
        }

        var functionType = InferExpression(callExpression.Function, result);
        foreach (var argument in callExpression.Arguments)
        {
            InferExpression(argument, result);
        }

        if (functionType is FunctionTypeSymbol typedFunction)
        {
            return InferTypedFunctionCall(callExpression, callExpression.Function.Syntax.String(), typedFunction.ParameterTypes, typedFunction.ReturnType, result);
        }

        return SetType(callExpression.Syntax, TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferTypedFunctionCall(
        BoundCallExpression callExpression,
        string functionName,
        IReadOnlyList<TypeSymbol> parameterTypes,
        TypeSymbol returnType,
        SemanticModel result)
    {
        if (callExpression.Arguments.Count != parameterTypes.Count)
        {
            return AddErrorAndSetType(callExpression.Syntax, $"wrong number of arguments for {functionName}: want={parameterTypes.Count}, got={callExpression.Arguments.Count}", TypeSymbol.Unknown, result);
        }

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            var argumentType = result.GetNodeTypeSymbol(callExpression.Arguments[i].Syntax);
            var parameterType = parameterTypes[i];
            if (argumentType != TypeSymbol.Unknown
                && parameterType != TypeSymbol.Unknown
                && (!parameterType.IsCompatibleWith(argumentType) || !argumentType.IsCompatibleWith(parameterType)))
            {
                return AddErrorAndSetType(callExpression.Syntax, $"Type error: argument {i + 1} for {functionName} expects {parameterType}, got {argumentType}", TypeSymbol.Unknown, result);
            }
        }

        return SetType(callExpression.Syntax, returnType, result);
    }

    private static TypeSymbol SetType(INode node, TypeSymbol type, SemanticModel result)
    {
        result.AddNodeType(node, type);
        return type;
    }

    private static TypeSymbol AddErrorAndSetType(INode node, string error, TypeSymbol type, SemanticModel result)
    {
        result.AddError(error, node.Token.Line, node.Token.Column);
        return SetType(node, type, result);
    }
}

public sealed record TypeCheckResult(SemanticModel TypeInfo, IReadOnlyList<Diagnostic> Errors);
