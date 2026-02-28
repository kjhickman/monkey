using Kong.Parsing;

namespace Kong.Semantics;

public class TypeInferer
{
    private sealed class BindingInfo
    {
        public StaticType ValueType { get; init; } = StaticType.Unknown;
        public IReadOnlyList<StaticType>? FunctionParameterTypes { get; init; }
    }

    private sealed class StaticType
    {
        private enum Kind
        {
            Unknown,
            Int,
            Bool,
            String,
            Array,
            Map,
        }

        private StaticType(Kind typeKind, StaticType? elementType = null, StaticType? keyType = null, StaticType? valueType = null)
        {
            TypeKind = typeKind;
            ElementType = elementType;
            KeyType = keyType;
            ValueType = valueType;
        }

        private Kind TypeKind { get; }
        private StaticType? ElementType { get; }
        private StaticType? KeyType { get; }
        private StaticType? ValueType { get; }

        public static StaticType Unknown { get; } = new(Kind.Unknown);
        public static StaticType Int { get; } = new(Kind.Int);
        public static StaticType Bool { get; } = new(Kind.Bool);
        public static StaticType String { get; } = new(Kind.String);
        public static StaticType ArrayOf(StaticType elementType) => new(Kind.Array, elementType: elementType);
        public static StaticType MapOf(StaticType keyType, StaticType valueType) => new(Kind.Map, keyType: keyType, valueType: valueType);

        public bool IsUnknown => TypeKind == Kind.Unknown;

        public bool IsCompatibleWith(StaticType actual)
        {
            if (IsUnknown || actual.IsUnknown)
            {
                return true;
            }

            if (TypeKind != actual.TypeKind)
            {
                return false;
            }

            return TypeKind switch
            {
                Kind.Array => ElementType!.IsCompatibleWith(actual.ElementType!),
                Kind.Map => KeyType!.IsCompatibleWith(actual.KeyType!) && ValueType!.IsCompatibleWith(actual.ValueType!),
                _ => true,
            };
        }

        public override string ToString()
        {
            return TypeKind switch
            {
                Kind.Int => "int",
                Kind.Bool => "bool",
                Kind.String => "string",
                Kind.Array => $"{ElementType}[]",
                Kind.Map => $"map[{KeyType}]{ValueType}",
                _ => "unknown",
            };
        }
    }

    public List<string> ValidateFunctionTypeAnnotations(Program root)
    {
        var env = new Dictionary<string, BindingInfo>();
        var error = ValidateProgramFunctionTypes(root, env);
        return error is null ? [] : [error];
    }

    public List<string> ValidateClrFunctionDeclarations(Program root)
    {
        var errors = new List<string>();
        var topLevelFunctionNames = root.Statements
            .OfType<LetStatement>()
            .Where(ls => ls.Value is FunctionLiteral)
            .Select(ls => ls.Name.Value)
            .ToHashSet();

        foreach (var statement in root.Statements)
        {
            if (statement is LetStatement { Value: FunctionLiteral functionLiteral })
            {
                ValidateClrFunctionLiteral(functionLiteral, topLevelFunctionNames, errors);
                continue;
            }

            ValidateNoNestedFunctionsInStatement(statement, errors);
        }

        return errors;
    }

    public TypeInferenceResult InferTypes(Program root)
    {
        var bootstrap = new TypeInferenceResult();
        var bootstrapEnv = new Dictionary<string, KongType>();

        foreach (var statement in root.Statements)
        {
            if (statement is LetStatement { Value: FunctionLiteral fl })
            {
                var parameterTypes = fl.Parameters
                    .Select(p => p.TypeAnnotation is null ? KongType.Unknown : ConvertTypeAnnotationToKongType(p.TypeAnnotation))
                    .ToList();
                bootstrap.AddFunctionSignature(fl.Name, parameterTypes, KongType.Unknown);
                bootstrapEnv[fl.Name] = KongType.Unknown;
            }
        }

        InferNode(root, bootstrap, bootstrapEnv);

        var result = new TypeInferenceResult();
        var env = new Dictionary<string, KongType>();
        foreach (var signature in bootstrap.GetFunctionSignatures())
        {
            result.AddFunctionSignature(signature.Name, signature.ParameterTypes, signature.ReturnType);
            env[signature.Name] = signature.ReturnType;
        }

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
            ReturnStatement rs when rs.ReturnValue is not null => InferReturnStatement(rs, result, env),
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
            FunctionLiteral functionLiteral => InferFunctionLiteral(functionLiteral, result, env),
            CallExpression callExpression => InferCallExpression(callExpression, result, env),
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

            if ((leftType == KongType.Int64 && rightType == KongType.Unknown)
                || (leftType == KongType.Unknown && rightType == KongType.Int64))
            {
                return SetType(infix, KongType.Int64, result);
            }

            if ((leftType == KongType.String && rightType == KongType.Unknown)
                || (leftType == KongType.Unknown && rightType == KongType.String))
            {
                return SetType(infix, KongType.String, result);
            }

            return AddErrorAndSetType(infix, $"Type error: cannot apply operator '+' to types {leftType} and {rightType}", KongType.Unknown, result);
        }
        
        if (infix.Operator is "-" or "*" or "/")
        {
            if (leftType == KongType.Unknown || rightType == KongType.Unknown)
            {
                return SetType(infix, KongType.Int64, result);
            }

            if (leftType != KongType.Int64 || rightType != KongType.Int64)
            {
                return AddErrorAndSetType(infix, $"Type error: cannot apply operator '{infix.Operator}' to types {leftType} and {rightType}", KongType.Unknown, result);
            }

            return SetType(infix, KongType.Int64, result);
        }

        if (infix.Operator is "==" or "!=" or "<" or ">")
        {
            if (leftType == KongType.Unknown || rightType == KongType.Unknown)
            {
                return SetType(infix, KongType.Boolean, result);
            }

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
        if (letStatement.Value is FunctionLiteral functionLiteral)
        {
            var functionType = InferFunctionLiteral(functionLiteral, result, env);
            env[letStatement.Name.Value] = functionType;
            SetType(letStatement.Name, functionType, result);
            return SetType(letStatement, functionType, result);
        }

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

    private KongType InferReturnStatement(ReturnStatement returnStatement, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var returnType = InferExpression(returnStatement.ReturnValue!, result, env);
        return SetType(returnStatement, returnType, result);
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

    private KongType InferFunctionLiteral(FunctionLiteral functionLiteral, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var localEnv = new Dictionary<string, KongType>(env);
        var parameterTypes = new List<KongType>(functionLiteral.Parameters.Count);

        foreach (var parameter in functionLiteral.Parameters)
        {
            if (parameter.TypeAnnotation is null)
            {
                return AddErrorAndSetType(functionLiteral, $"missing type annotation for parameter '{parameter.Name.Value}'", KongType.Unknown, result);
            }

            var parameterType = ConvertTypeAnnotationToKongType(parameter.TypeAnnotation);
            parameterTypes.Add(parameterType);
            localEnv[parameter.Name.Value] = parameterType;
            SetType(parameter.Name, parameterType, result);
        }

        var bodyType = InferBlockStatement(functionLiteral.Body, result, localEnv);
        if (!string.IsNullOrEmpty(functionLiteral.Name))
        {
            result.AddFunctionSignature(functionLiteral.Name, parameterTypes, bodyType);
        }

        return SetType(functionLiteral, KongType.Unknown, result);
    }

    private KongType InferCallExpression(CallExpression callExpression, TypeInferenceResult result, Dictionary<string, KongType> env)
    {
        var _ = InferExpression(callExpression.Function, result, env);

        foreach (var argument in callExpression.Arguments)
        {
            InferExpression(argument, result, env);
        }

        if (callExpression.Function is not Identifier identifier)
        {
            return SetType(callExpression, KongType.Unknown, result);
        }

        if (!result.TryGetFunctionSignature(identifier.Value, out var signature))
        {
            return SetType(callExpression, KongType.Unknown, result);
        }

        if (callExpression.Arguments.Count != signature.ParameterTypes.Count)
        {
            return AddErrorAndSetType(callExpression, $"wrong number of arguments for {identifier.Value}: want={signature.ParameterTypes.Count}, got={callExpression.Arguments.Count}", KongType.Unknown, result);
        }

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            var argumentType = result.GetNodeType(callExpression.Arguments[i]);
            var parameterType = signature.ParameterTypes[i];
            if (argumentType != KongType.Unknown && parameterType != KongType.Unknown && argumentType != parameterType)
            {
                return AddErrorAndSetType(callExpression, $"Type error: argument {i + 1} for {identifier.Value} expects {parameterType}, got {argumentType}", KongType.Unknown, result);
            }
        }

        return SetType(callExpression, signature.ReturnType, result);
    }

    private static KongType ConvertTypeAnnotationToKongType(ITypeExpression annotation)
    {
        return annotation switch
        {
            NamedTypeExpression { Name: "int" } => KongType.Int64,
            NamedTypeExpression { Name: "bool" } => KongType.Boolean,
            NamedTypeExpression { Name: "string" } => KongType.String,
            ArrayTypeExpression => KongType.Array,
            MapTypeExpression => KongType.HashMap,
            _ => KongType.Unknown,
        };
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

    private static string? ValidateProgramFunctionTypes(Program program, Dictionary<string, BindingInfo> env)
    {
        foreach (var statement in program.Statements)
        {
            var err = ValidateFunctionTypesInStatement(statement, env);
            if (err is not null)
            {
                return err;
            }
        }

        return null;
    }

    private static string? ValidateFunctionTypesInStatement(IStatement statement, Dictionary<string, BindingInfo> env)
    {
        switch (statement)
        {
            case LetStatement { Value: not null } letStatement:
            {
                if (letStatement.Value is FunctionLiteral functionLiteral)
                {
                    var fnParamTypes = ParseFunctionParameterTypes(functionLiteral, out var parseErr);
                    if (parseErr is not null)
                    {
                        return parseErr;
                    }

                    env[letStatement.Name.Value] = new BindingInfo { FunctionParameterTypes = fnParamTypes };

                    var fnErr = ValidateFunctionLiteral(functionLiteral, env);
                    if (fnErr is not null)
                    {
                        return fnErr;
                    }
                }
                else
                {
                    var valueErr = ValidateFunctionTypesInExpression(letStatement.Value, env);
                    if (valueErr is not null)
                    {
                        return valueErr;
                    }

                    env[letStatement.Name.Value] = new BindingInfo
                    {
                        ValueType = InferStaticExpressionType(letStatement.Value, env),
                    };
                }

                return null;
            }

            case ReturnStatement { ReturnValue: not null } returnStatement:
                return ValidateFunctionTypesInExpression(returnStatement.ReturnValue, env);

            case ExpressionStatement { Expression: not null } expressionStatement:
                return ValidateFunctionTypesInExpression(expressionStatement.Expression, env);

            case BlockStatement blockStatement:
            {
                foreach (var inner in blockStatement.Statements)
                {
                    var err = ValidateFunctionTypesInStatement(inner, env);
                    if (err is not null)
                    {
                        return err;
                    }
                }

                return null;
            }

            default:
                return null;
        }
    }

    private static string? ValidateFunctionTypesInExpression(IExpression expression, Dictionary<string, BindingInfo> env)
    {
        switch (expression)
        {
            case FunctionLiteral functionLiteral:
                return ValidateFunctionLiteral(functionLiteral, env);

            case CallExpression callExpression:
            {
                var functionErr = ValidateFunctionTypesInExpression(callExpression.Function, env);
                if (functionErr is not null)
                {
                    return functionErr;
                }

                foreach (var argument in callExpression.Arguments)
                {
                    var argumentErr = ValidateFunctionTypesInExpression(argument, env);
                    if (argumentErr is not null)
                    {
                        return argumentErr;
                    }
                }

                var fnParamTypes = ResolveFunctionParameterTypes(callExpression.Function, env, out var resolveErr);
                if (resolveErr is not null)
                {
                    return resolveErr;
                }

                if (fnParamTypes is null)
                {
                    return null;
                }

                var count = Math.Min(fnParamTypes.Count, callExpression.Arguments.Count);
                for (var i = 0; i < count; i++)
                {
                    var expected = fnParamTypes[i];
                    var actual = InferStaticExpressionType(callExpression.Arguments[i], env);
                    if (!expected.IsCompatibleWith(actual))
                    {
                        return $"type mismatch for argument {i + 1} in call to {callExpression.Function.String()}: expected {expected}, got {actual}";
                    }
                }

                return null;
            }

            case IfExpression ifExpression:
            {
                var conditionErr = ValidateFunctionTypesInExpression(ifExpression.Condition, env);
                if (conditionErr is not null)
                {
                    return conditionErr;
                }

                var thenEnv = new Dictionary<string, BindingInfo>(env);
                var thenErr = ValidateFunctionTypesInStatement(ifExpression.Consequence, thenEnv);
                if (thenErr is not null)
                {
                    return thenErr;
                }

                if (ifExpression.Alternative is not null)
                {
                    var elseEnv = new Dictionary<string, BindingInfo>(env);
                    var elseErr = ValidateFunctionTypesInStatement(ifExpression.Alternative, elseEnv);
                    if (elseErr is not null)
                    {
                        return elseErr;
                    }
                }

                return null;
            }

            case PrefixExpression prefixExpression:
                return ValidateFunctionTypesInExpression(prefixExpression.Right, env);

            case InfixExpression infixExpression:
            {
                var leftErr = ValidateFunctionTypesInExpression(infixExpression.Left, env);
                if (leftErr is not null)
                {
                    return leftErr;
                }

                return ValidateFunctionTypesInExpression(infixExpression.Right, env);
            }

            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    var err = ValidateFunctionTypesInExpression(element, env);
                    if (err is not null)
                    {
                        return err;
                    }
                }
                return null;

            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    var keyErr = ValidateFunctionTypesInExpression(pair.Key, env);
                    if (keyErr is not null)
                    {
                        return keyErr;
                    }

                    var valueErr = ValidateFunctionTypesInExpression(pair.Value, env);
                    if (valueErr is not null)
                    {
                        return valueErr;
                    }
                }
                return null;

            case IndexExpression indexExpression:
            {
                var leftErr = ValidateFunctionTypesInExpression(indexExpression.Left, env);
                if (leftErr is not null)
                {
                    return leftErr;
                }

                return ValidateFunctionTypesInExpression(indexExpression.Index, env);
            }

            default:
                return null;
        }
    }

    private static string? ValidateFunctionLiteral(FunctionLiteral functionLiteral, Dictionary<string, BindingInfo> outerEnv)
    {
        var paramTypes = ParseFunctionParameterTypes(functionLiteral, out var parseErr);
        if (parseErr is not null)
        {
            return parseErr;
        }

        var localEnv = new Dictionary<string, BindingInfo>(outerEnv);

        if (!string.IsNullOrEmpty(functionLiteral.Name))
        {
            localEnv[functionLiteral.Name] = new BindingInfo { FunctionParameterTypes = paramTypes };
        }

        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            var parameter = functionLiteral.Parameters[i];
            localEnv[parameter.Name.Value] = new BindingInfo
            {
                ValueType = paramTypes[i],
            };
        }

        return ValidateFunctionTypesInStatement(functionLiteral.Body, localEnv);
    }

    private static IReadOnlyList<StaticType>? ResolveFunctionParameterTypes(IExpression functionExpression, Dictionary<string, BindingInfo> env, out string? err)
    {
        err = null;

        return functionExpression switch
        {
            Identifier identifier when env.TryGetValue(identifier.Value, out var binding) => binding.FunctionParameterTypes,
            FunctionLiteral literal => ParseFunctionParameterTypes(literal, out err),
            _ => null,
        };
    }

    private static List<StaticType> ParseFunctionParameterTypes(FunctionLiteral functionLiteral, out string? err)
    {
        var parameterTypes = new List<StaticType>(functionLiteral.Parameters.Count);
        foreach (var parameter in functionLiteral.Parameters)
        {
            if (parameter.TypeAnnotation is null)
            {
                err = $"missing type annotation for parameter '{parameter.Name.Value}'";
                return [];
            }

            var type = ParseTypeAnnotation(parameter.TypeAnnotation, out err);
            if (err is not null)
            {
                err = $"invalid type annotation for parameter '{parameter.Name.Value}': {err}";
                return [];
            }

            parameterTypes.Add(type);
        }

        err = null;
        return parameterTypes;
    }

    private static StaticType ParseTypeAnnotation(ITypeExpression annotation, out string? err)
    {
        switch (annotation)
        {
            case NamedTypeExpression namedType:
                return namedType.Name switch
                {
                    "int" => SetNoError(StaticType.Int, out err),
                    "bool" => SetNoError(StaticType.Bool, out err),
                    "string" => SetNoError(StaticType.String, out err),
                    _ => SetErrorAndReturnUnknown($"unknown type '{namedType.Name}'", out err),
                };

            case ArrayTypeExpression arrayType:
            {
                var elementType = ParseTypeAnnotation(arrayType.ElementType, out err);
                if (err is not null)
                {
                    return StaticType.Unknown;
                }

                return SetNoError(StaticType.ArrayOf(elementType), out err);
            }

            case MapTypeExpression mapType:
            {
                var keyType = ParseTypeAnnotation(mapType.KeyType, out err);
                if (err is not null)
                {
                    return StaticType.Unknown;
                }

                var valueType = ParseTypeAnnotation(mapType.ValueType, out err);
                if (err is not null)
                {
                    return StaticType.Unknown;
                }

                return SetNoError(StaticType.MapOf(keyType, valueType), out err);
            }

            default:
                return SetErrorAndReturnUnknown($"unsupported type annotation '{annotation.GetType().Name}'", out err);
        }
    }

    private static StaticType SetNoError(StaticType type, out string? err)
    {
        err = null;
        return type;
    }

    private static StaticType SetErrorAndReturnUnknown(string error, out string? err)
    {
        err = error;
        return StaticType.Unknown;
    }

    private static StaticType InferStaticExpressionType(IExpression expression, Dictionary<string, BindingInfo> env)
    {
        switch (expression)
        {
            case IntegerLiteral:
                return StaticType.Int;
            case BooleanLiteral:
                return StaticType.Bool;
            case StringLiteral:
                return StaticType.String;
            case Identifier identifier when env.TryGetValue(identifier.Value, out var binding):
                return binding.ValueType;
            case ArrayLiteral arrayLiteral:
            {
                if (arrayLiteral.Elements.Count == 0)
                {
                    return StaticType.ArrayOf(StaticType.Unknown);
                }

                var elementType = InferStaticExpressionType(arrayLiteral.Elements[0], env);
                for (var i = 1; i < arrayLiteral.Elements.Count; i++)
                {
                    var currentType = InferStaticExpressionType(arrayLiteral.Elements[i], env);
                    if (!elementType.IsCompatibleWith(currentType) || !currentType.IsCompatibleWith(elementType))
                    {
                        return StaticType.ArrayOf(StaticType.Unknown);
                    }
                }

                return StaticType.ArrayOf(elementType);
            }
            case HashLiteral hashLiteral:
            {
                if (hashLiteral.Pairs.Count == 0)
                {
                    return StaticType.MapOf(StaticType.Unknown, StaticType.Unknown);
                }

                var first = hashLiteral.Pairs[0];
                var keyType = InferStaticExpressionType(first.Key, env);
                var valueType = InferStaticExpressionType(first.Value, env);

                for (var i = 1; i < hashLiteral.Pairs.Count; i++)
                {
                    var currentKeyType = InferStaticExpressionType(hashLiteral.Pairs[i].Key, env);
                    var currentValueType = InferStaticExpressionType(hashLiteral.Pairs[i].Value, env);
                    if (!keyType.IsCompatibleWith(currentKeyType) || !currentKeyType.IsCompatibleWith(keyType) || !valueType.IsCompatibleWith(currentValueType) || !currentValueType.IsCompatibleWith(valueType))
                    {
                        return StaticType.MapOf(StaticType.Unknown, StaticType.Unknown);
                    }
                }

                return StaticType.MapOf(keyType, valueType);
            }
            case PrefixExpression prefixExpression when prefixExpression.Operator == "-":
                return StaticType.Int;
            case PrefixExpression prefixExpression when prefixExpression.Operator == "!":
                return StaticType.Bool;
            case InfixExpression infixExpression when infixExpression.Operator == "+":
            {
                var left = InferStaticExpressionType(infixExpression.Left, env);
                var right = InferStaticExpressionType(infixExpression.Right, env);
                if (left.IsCompatibleWith(StaticType.String) && right.IsCompatibleWith(StaticType.String))
                {
                    return StaticType.String;
                }

                if (left.IsCompatibleWith(StaticType.Int) && right.IsCompatibleWith(StaticType.Int))
                {
                    return StaticType.Int;
                }

                return StaticType.Unknown;
            }
            case InfixExpression infixExpression when infixExpression.Operator is "-" or "*" or "/":
                return StaticType.Int;
            case InfixExpression infixExpression when infixExpression.Operator is "==" or "!=" or "<" or ">":
                return StaticType.Bool;
            case IfExpression ifExpression:
            {
                var thenType = InferStaticBlockType(ifExpression.Consequence, env);
                if (ifExpression.Alternative is null)
                {
                    return StaticType.Unknown;
                }

                var elseType = InferStaticBlockType(ifExpression.Alternative, env);
                return thenType.IsCompatibleWith(elseType) && elseType.IsCompatibleWith(thenType)
                    ? thenType
                    : StaticType.Unknown;
            }
            case IndexExpression:
            case CallExpression:
            case FunctionLiteral:
            default:
                return StaticType.Unknown;
        }
    }

    private static StaticType InferStaticBlockType(BlockStatement blockStatement, Dictionary<string, BindingInfo> env)
    {
        if (blockStatement.Statements.Count == 0)
        {
            return StaticType.Unknown;
        }

        var localEnv = new Dictionary<string, BindingInfo>(env);
        var lastType = StaticType.Unknown;
        foreach (var statement in blockStatement.Statements)
        {
            switch (statement)
            {
                case LetStatement { Value: not null } letStatement:
                    localEnv[letStatement.Name.Value] = new BindingInfo
                    {
                        ValueType = InferStaticExpressionType(letStatement.Value, localEnv),
                    };
                    lastType = StaticType.Unknown;
                    break;
                case ExpressionStatement { Expression: not null } expressionStatement:
                    lastType = InferStaticExpressionType(expressionStatement.Expression, localEnv);
                    break;
                case ReturnStatement { ReturnValue: not null } returnStatement:
                    lastType = InferStaticExpressionType(returnStatement.ReturnValue, localEnv);
                    break;
                default:
                    lastType = StaticType.Unknown;
                    break;
            }
        }

        return lastType;
    }

    private static void ValidateClrFunctionLiteral(FunctionLiteral functionLiteral, HashSet<string> topLevelFunctionNames, List<string> errors)
    {
        var functionScope = new HashSet<string>(topLevelFunctionNames);
        foreach (var parameter in functionLiteral.Parameters)
        {
            functionScope.Add(parameter.Name.Value);
        }

        ValidateClrStatementScope(functionLiteral.Body, functionScope, topLevelFunctionNames, errors, functionLiteral.Name);
    }

    private static void ValidateClrStatementScope(IStatement statement, HashSet<string> scope, HashSet<string> topLevelFunctionNames, List<string> errors, string currentFunctionName)
    {
        switch (statement)
        {
            case LetStatement { Value: FunctionLiteral nestedFunction }:
                errors.Add($"nested function declarations are not supported in CLR backend: {nestedFunction.Name}");
                return;

            case LetStatement { Value: not null } letStatement:
                ValidateClrExpressionScope(letStatement.Value, scope, topLevelFunctionNames, errors, currentFunctionName);
                scope.Add(letStatement.Name.Value);
                return;

            case ReturnStatement { ReturnValue: not null } returnStatement:
                ValidateClrExpressionScope(returnStatement.ReturnValue, scope, topLevelFunctionNames, errors, currentFunctionName);
                return;

            case ExpressionStatement { Expression: not null } expressionStatement:
                ValidateClrExpressionScope(expressionStatement.Expression, scope, topLevelFunctionNames, errors, currentFunctionName);
                return;

            case BlockStatement blockStatement:
                foreach (var innerStatement in blockStatement.Statements)
                {
                    ValidateClrStatementScope(innerStatement, scope, topLevelFunctionNames, errors, currentFunctionName);
                }
                return;
        }
    }

    private static void ValidateClrExpressionScope(IExpression expression, HashSet<string> scope, HashSet<string> topLevelFunctionNames, List<string> errors, string currentFunctionName)
    {
        switch (expression)
        {
            case Identifier identifier when !scope.Contains(identifier.Value):
                errors.Add($"captured variables are not supported in CLR backend function '{currentFunctionName}': {identifier.Value}");
                return;

            case FunctionLiteral nestedFunction:
                errors.Add($"nested function declarations are not supported in CLR backend: {nestedFunction.Name}");
                return;

            case CallExpression callExpression:
                if (callExpression.Function is not Identifier callIdentifier)
                {
                    errors.Add("function values and higher-order function calls are not supported in CLR backend");
                    return;
                }

                if (!topLevelFunctionNames.Contains(callIdentifier.Value) && !scope.Contains(callIdentifier.Value))
                {
                    errors.Add($"undefined function in CLR backend: {callIdentifier.Value}");
                    return;
                }

                foreach (var argument in callExpression.Arguments)
                {
                    ValidateClrExpressionScope(argument, scope, topLevelFunctionNames, errors, currentFunctionName);
                }
                return;

            case InfixExpression infixExpression:
                ValidateClrExpressionScope(infixExpression.Left, scope, topLevelFunctionNames, errors, currentFunctionName);
                ValidateClrExpressionScope(infixExpression.Right, scope, topLevelFunctionNames, errors, currentFunctionName);
                return;

            case PrefixExpression prefixExpression:
                ValidateClrExpressionScope(prefixExpression.Right, scope, topLevelFunctionNames, errors, currentFunctionName);
                return;

            case IfExpression ifExpression:
                ValidateClrExpressionScope(ifExpression.Condition, scope, topLevelFunctionNames, errors, currentFunctionName);
                ValidateClrStatementScope(ifExpression.Consequence, scope, topLevelFunctionNames, errors, currentFunctionName);
                if (ifExpression.Alternative is not null)
                {
                    ValidateClrStatementScope(ifExpression.Alternative, scope, topLevelFunctionNames, errors, currentFunctionName);
                }
                return;

            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ValidateClrExpressionScope(element, scope, topLevelFunctionNames, errors, currentFunctionName);
                }
                return;

            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    ValidateClrExpressionScope(pair.Key, scope, topLevelFunctionNames, errors, currentFunctionName);
                    ValidateClrExpressionScope(pair.Value, scope, topLevelFunctionNames, errors, currentFunctionName);
                }
                return;

            case IndexExpression indexExpression:
                ValidateClrExpressionScope(indexExpression.Left, scope, topLevelFunctionNames, errors, currentFunctionName);
                ValidateClrExpressionScope(indexExpression.Index, scope, topLevelFunctionNames, errors, currentFunctionName);
                return;
        }
    }

    private static void ValidateNoNestedFunctionsInStatement(IStatement statement, List<string> errors)
    {
        switch (statement)
        {
            case LetStatement { Value: not null } letStatement:
                ValidateNoNestedFunctionsInExpression(letStatement.Value, errors);
                break;
            case ExpressionStatement { Expression: not null } expressionStatement:
                ValidateNoNestedFunctionsInExpression(expressionStatement.Expression, errors);
                break;
            case ReturnStatement { ReturnValue: not null } returnStatement:
                ValidateNoNestedFunctionsInExpression(returnStatement.ReturnValue, errors);
                break;
            case BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    ValidateNoNestedFunctionsInStatement(inner, errors);
                }
                break;
        }
    }

    private static void ValidateNoNestedFunctionsInExpression(IExpression expression, List<string> errors)
    {
        switch (expression)
        {
            case FunctionLiteral nestedFunction:
                errors.Add($"nested function declarations are not supported in CLR backend: {nestedFunction.Name}");
                return;
            case PrefixExpression prefixExpression:
                ValidateNoNestedFunctionsInExpression(prefixExpression.Right, errors);
                return;
            case InfixExpression infixExpression:
                ValidateNoNestedFunctionsInExpression(infixExpression.Left, errors);
                ValidateNoNestedFunctionsInExpression(infixExpression.Right, errors);
                return;
            case IfExpression ifExpression:
                ValidateNoNestedFunctionsInExpression(ifExpression.Condition, errors);
                ValidateNoNestedFunctionsInStatement(ifExpression.Consequence, errors);
                if (ifExpression.Alternative is not null)
                {
                    ValidateNoNestedFunctionsInStatement(ifExpression.Alternative, errors);
                }
                return;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ValidateNoNestedFunctionsInExpression(element, errors);
                }
                return;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    ValidateNoNestedFunctionsInExpression(pair.Key, errors);
                    ValidateNoNestedFunctionsInExpression(pair.Value, errors);
                }
                return;
            case IndexExpression indexExpression:
                ValidateNoNestedFunctionsInExpression(indexExpression.Left, errors);
                ValidateNoNestedFunctionsInExpression(indexExpression.Index, errors);
                return;
            case CallExpression callExpression:
                ValidateNoNestedFunctionsInExpression(callExpression.Function, errors);
                foreach (var argument in callExpression.Arguments)
                {
                    ValidateNoNestedFunctionsInExpression(argument, errors);
                }
                return;
        }
    }
}
