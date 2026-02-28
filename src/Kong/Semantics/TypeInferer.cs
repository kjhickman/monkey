using Kong.Parsing;
using Kong.Semantics.Symbols;

namespace Kong.Semantics;

public class TypeInferer
{
    public List<string> ValidateFunctionTypeAnnotations(Program root)
    {
        var errors = new List<string>();
        foreach (var statement in root.Statements)
        {
            ValidateTypeAnnotationsInStatement(statement, errors);
        }

        return errors;
    }

    public TypeInferenceResult InferTypes(Program root)
    {
        var topLevelFunctionCount = root.Statements.Count(s => s is LetStatement { Value: FunctionLiteral });
        var maxIterations = Math.Max(1, topLevelFunctionCount + 1);

        Dictionary<string, TypeSymbol>? seededReturnTypes = null;
        for (var i = 0; i < maxIterations; i++)
        {
            var passResult = InferTypesPass(root, reportTopLevelAnnotationErrors: false, seededReturnTypes);
            var inferredReturnTypes = BuildTopLevelReturnTypeMap(passResult);

            if (seededReturnTypes is not null && HaveSameTopLevelReturnTypes(seededReturnTypes, inferredReturnTypes))
            {
                seededReturnTypes = inferredReturnTypes;
                break;
            }

            seededReturnTypes = inferredReturnTypes;
        }

        return InferTypesPass(root, reportTopLevelAnnotationErrors: true, seededReturnTypes);
    }

    private TypeInferenceResult InferTypesPass(
        Program root,
        bool reportTopLevelAnnotationErrors,
        IReadOnlyDictionary<string, TypeSymbol>? seededReturnTypes)
    {
        var result = new TypeInferenceResult();
        var globalScope = new SymbolScope();

        BootstrapTopLevelFunctions(root, result, globalScope, reportTopLevelAnnotationErrors, seededReturnTypes);
        InferNode(root, result, globalScope);

        return result;
    }

    private static Dictionary<string, TypeSymbol> BuildTopLevelReturnTypeMap(TypeInferenceResult result)
    {
        return result
            .GetFunctionSignatures()
            .ToDictionary(signature => signature.Name, signature => signature.ReturnType);
    }

    private static bool HaveSameTopLevelReturnTypes(
        IReadOnlyDictionary<string, TypeSymbol> previous,
        IReadOnlyDictionary<string, TypeSymbol> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        foreach (var (name, previousType) in previous)
        {
            if (!current.TryGetValue(name, out var currentType) || previousType != currentType)
            {
                return false;
            }
        }

        return true;
    }

    private static void BootstrapTopLevelFunctions(
        Program root,
        TypeInferenceResult result,
        SymbolScope globalScope,
        bool reportAnnotationErrors,
        IReadOnlyDictionary<string, TypeSymbol>? seededReturnTypes)
    {
        foreach (var statement in root.Statements)
        {
            if (statement is not LetStatement { Value: FunctionLiteral functionLiteral } letStatement)
            {
                continue;
            }

            var parameterTypes = ParseFunctionParameterTypes(functionLiteral, result, reportErrors: reportAnnotationErrors);
            var parameterSymbols = CreateParameterSymbols(functionLiteral, parameterTypes);
            var returnType = seededReturnTypes is not null && seededReturnTypes.TryGetValue(letStatement.Name.Value, out var seededReturnType)
                ? seededReturnType
                : TypeSymbol.Unknown;

            var functionSymbol = new FunctionSymbol(letStatement.Name.Value, parameterSymbols, returnType);
            globalScope.Define(functionSymbol);
            result.AddFunctionSignature(letStatement.Name.Value, parameterTypes, returnType);
        }
    }

    private static List<VariableSymbol> CreateParameterSymbols(FunctionLiteral functionLiteral, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        var symbols = new List<VariableSymbol>(functionLiteral.Parameters.Count);
        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            symbols.Add(new VariableSymbol(functionLiteral.Parameters[i].Name.Value, parameterTypes[i]));
        }

        return symbols;
    }

    private static List<TypeSymbol> ParseFunctionParameterTypes(FunctionLiteral functionLiteral, TypeInferenceResult result, bool reportErrors)
    {
        var parameterTypes = new List<TypeSymbol>(functionLiteral.Parameters.Count);
        foreach (var parameter in functionLiteral.Parameters)
        {
            if (parameter.TypeAnnotation is null)
            {
                if (reportErrors)
                {
                    result.AddError($"missing type annotation for parameter '{parameter.Name.Value}'");
                }

                parameterTypes.Add(TypeSymbol.Unknown);
                continue;
            }

            var parameterType = ParseTypeAnnotation(parameter.TypeAnnotation, out var parseErr);
            if (parseErr is not null && reportErrors)
            {
                result.AddError($"invalid type annotation for parameter '{parameter.Name.Value}': {parseErr}");
            }

            parameterTypes.Add(parameterType);
        }

        return parameterTypes;
    }

    private static TypeSymbol ParseTypeAnnotation(ITypeExpression annotation, out string? err)
    {
        switch (annotation)
        {
            case NamedTypeExpression namedType:
                return namedType.Name switch
                {
                    "int" => SetNoError(TypeSymbol.Int, out err),
                    "bool" => SetNoError(TypeSymbol.Bool, out err),
                    "string" => SetNoError(TypeSymbol.String, out err),
                    _ => SetErrorAndReturnUnknown($"unknown type '{namedType.Name}'", out err),
                };

            case ArrayTypeExpression arrayType:
            {
                var elementType = ParseTypeAnnotation(arrayType.ElementType, out err);
                if (err is not null)
                {
                    return TypeSymbol.Unknown;
                }

                return SetNoError(TypeSymbol.ArrayOf(elementType), out err);
            }

            case MapTypeExpression mapType:
            {
                var keyType = ParseTypeAnnotation(mapType.KeyType, out err);
                if (err is not null)
                {
                    return TypeSymbol.Unknown;
                }

                var valueType = ParseTypeAnnotation(mapType.ValueType, out err);
                if (err is not null)
                {
                    return TypeSymbol.Unknown;
                }

                return SetNoError(TypeSymbol.MapOf(keyType, valueType), out err);
            }

            default:
                return SetErrorAndReturnUnknown($"unsupported type annotation '{annotation.GetType().Name}'", out err);
        }
    }

    private static TypeSymbol SetNoError(TypeSymbol type, out string? err)
    {
        err = null;
        return type;
    }

    private static TypeSymbol SetErrorAndReturnUnknown(string error, out string? err)
    {
        err = error;
        return TypeSymbol.Unknown;
    }

    private static void ValidateTypeAnnotationsInStatement(IStatement statement, List<string> errors)
    {
        switch (statement)
        {
            case LetStatement { Value: not null } letStatement:
                ValidateTypeAnnotationsInExpression(letStatement.Value, errors);
                return;
            case ReturnStatement { ReturnValue: not null } returnStatement:
                ValidateTypeAnnotationsInExpression(returnStatement.ReturnValue, errors);
                return;
            case ExpressionStatement { Expression: not null } expressionStatement:
                ValidateTypeAnnotationsInExpression(expressionStatement.Expression, errors);
                return;
            case BlockStatement blockStatement:
                foreach (var inner in blockStatement.Statements)
                {
                    ValidateTypeAnnotationsInStatement(inner, errors);
                }

                return;
            default:
                return;
        }
    }

    private static void ValidateTypeAnnotationsInExpression(IExpression expression, List<string> errors)
    {
        switch (expression)
        {
            case FunctionLiteral functionLiteral:
                ValidateFunctionLiteralAnnotations(functionLiteral, errors);
                return;
            case CallExpression callExpression:
                ValidateTypeAnnotationsInExpression(callExpression.Function, errors);
                foreach (var argument in callExpression.Arguments)
                {
                    ValidateTypeAnnotationsInExpression(argument, errors);
                }

                return;
            case IfExpression ifExpression:
                ValidateTypeAnnotationsInExpression(ifExpression.Condition, errors);
                ValidateTypeAnnotationsInStatement(ifExpression.Consequence, errors);
                if (ifExpression.Alternative is not null)
                {
                    ValidateTypeAnnotationsInStatement(ifExpression.Alternative, errors);
                }

                return;
            case PrefixExpression prefixExpression:
                ValidateTypeAnnotationsInExpression(prefixExpression.Right, errors);
                return;
            case InfixExpression infixExpression:
                ValidateTypeAnnotationsInExpression(infixExpression.Left, errors);
                ValidateTypeAnnotationsInExpression(infixExpression.Right, errors);
                return;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ValidateTypeAnnotationsInExpression(element, errors);
                }

                return;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    ValidateTypeAnnotationsInExpression(pair.Key, errors);
                    ValidateTypeAnnotationsInExpression(pair.Value, errors);
                }

                return;
            case IndexExpression indexExpression:
                ValidateTypeAnnotationsInExpression(indexExpression.Left, errors);
                ValidateTypeAnnotationsInExpression(indexExpression.Index, errors);
                return;
            default:
                return;
        }
    }

    private static void ValidateFunctionLiteralAnnotations(FunctionLiteral functionLiteral, List<string> errors)
    {
        foreach (var parameter in functionLiteral.Parameters)
        {
            if (parameter.TypeAnnotation is null)
            {
                errors.Add($"missing type annotation for parameter '{parameter.Name.Value}'");
                continue;
            }

            _ = ParseTypeAnnotation(parameter.TypeAnnotation, out var parseErr);
            if (parseErr is not null)
            {
                errors.Add($"invalid type annotation for parameter '{parameter.Name.Value}': {parseErr}");
            }
        }

        ValidateTypeAnnotationsInStatement(functionLiteral.Body, errors);
    }

    private TypeSymbol InferNode(INode node, TypeInferenceResult result, SymbolScope scope)
    {
        return node switch
        {
            Program program => InferProgram(program, result, scope),
            IStatement statement => InferStatement(statement, result, scope),
            IExpression expression => InferExpression(expression, result, scope),
            _ => InferUnsupported(node, result),
        };
    }

    private TypeSymbol InferProgram(Program program, TypeInferenceResult result, SymbolScope scope)
    {
        var lastType = TypeSymbol.Unknown;
        foreach (var statement in program.Statements)
        {
            lastType = InferNode(statement, result, scope);
        }

        return SetType(program, lastType, result);
    }

    private TypeSymbol InferStatement(IStatement statement, TypeInferenceResult result, SymbolScope scope)
    {
        return statement switch
        {
            ExpressionStatement expressionStatement when expressionStatement.Expression is not null => InferExpressionStatement(expressionStatement, result, scope),
            LetStatement letStatement when letStatement.Value is not null => InferLetStatement(letStatement, result, scope),
            ReturnStatement returnStatement when returnStatement.ReturnValue is not null => InferReturnStatement(returnStatement, result, scope),
            BlockStatement blockStatement => InferBlockStatement(blockStatement, result, scope),
            _ => AddErrorAndSetType(statement, $"Unsupported statement type: {statement.GetType().Name}", TypeSymbol.Unknown, result),
        };
    }

    private TypeSymbol InferExpression(IExpression expression, TypeInferenceResult result, SymbolScope scope)
    {
        return expression switch
        {
            IntegerLiteral => SetType(expression, TypeSymbol.Int, result),
            BooleanLiteral => SetType(expression, TypeSymbol.Bool, result),
            StringLiteral => SetType(expression, TypeSymbol.String, result),
            ArrayLiteral arrayLiteral => InferArrayLiteral(arrayLiteral, result, scope),
            HashLiteral hashLiteral => InferHashLiteral(hashLiteral, result, scope),
            InfixExpression infixExpression => InferInfixExpression(infixExpression, result, scope),
            Identifier identifier => InferIdentifier(identifier, result, scope),
            PrefixExpression prefixExpression when prefixExpression.Operator == "-" => InferNegationPrefix(prefixExpression, result, scope),
            PrefixExpression prefixExpression when prefixExpression.Operator == "!" => InferBangPrefix(prefixExpression, result, scope),
            IfExpression ifExpression => InferIfExpression(ifExpression, result, scope),
            IndexExpression indexExpression => InferIndexExpression(indexExpression, result, scope),
            FunctionLiteral functionLiteral => InferFunctionLiteral(functionLiteral, null, result, scope, recordTopLevelSignature: false),
            CallExpression callExpression => InferCallExpression(callExpression, result, scope),
            _ => AddErrorAndSetType(expression, $"Unsupported expression: {expression.GetType().Name}", TypeSymbol.Unknown, result),
        };
    }

    private TypeSymbol InferExpressionStatement(ExpressionStatement expressionStatement, TypeInferenceResult result, SymbolScope scope)
    {
        var expressionType = InferExpression(expressionStatement.Expression!, result, scope);
        return SetType(expressionStatement, expressionType, result);
    }

    private TypeSymbol InferLetStatement(LetStatement letStatement, TypeInferenceResult result, SymbolScope scope)
    {
        if (letStatement.Value is FunctionLiteral functionLiteral)
        {
            var functionSymbol = ResolveDeclaredFunctionSymbol(letStatement, functionLiteral, result, scope);
            scope.Define(functionSymbol);

            var functionType = InferFunctionLiteral(
                functionLiteral,
                functionSymbol,
                result,
                scope,
                recordTopLevelSignature: scope.Parent is null);

            SetType(letStatement.Name, functionType, result);
            return SetType(letStatement, functionType, result);
        }

        var valueType = InferExpression(letStatement.Value!, result, scope);
        if (valueType == TypeSymbol.Void)
        {
            SetType(letStatement.Name, TypeSymbol.Unknown, result);
            return AddErrorAndSetType(letStatement, "Type error: cannot assign an expression with no value", TypeSymbol.Unknown, result);
        }

        scope.Define(new VariableSymbol(letStatement.Name.Value, valueType));
        SetType(letStatement.Name, valueType, result);
        return SetType(letStatement, valueType, result);
    }

    private static FunctionSymbol ResolveDeclaredFunctionSymbol(LetStatement letStatement, FunctionLiteral functionLiteral, TypeInferenceResult result, SymbolScope scope)
    {
        if (scope.Parent is null && scope.TryLookupFunction(letStatement.Name.Value, out var existingGlobalFunction) && existingGlobalFunction is not null)
        {
            return existingGlobalFunction;
        }

        var parameterTypes = ParseFunctionParameterTypes(functionLiteral, result, reportErrors: true);
        var parameterSymbols = CreateParameterSymbols(functionLiteral, parameterTypes);
        return new FunctionSymbol(letStatement.Name.Value, parameterSymbols, TypeSymbol.Unknown);
    }

    private TypeSymbol InferReturnStatement(ReturnStatement returnStatement, TypeInferenceResult result, SymbolScope scope)
    {
        var returnType = InferExpression(returnStatement.ReturnValue!, result, scope);
        return SetType(returnStatement, returnType, result);
    }

    private TypeSymbol InferBlockStatement(BlockStatement blockStatement, TypeInferenceResult result, SymbolScope scope)
    {
        var lastType = TypeSymbol.Unknown;
        foreach (var statement in blockStatement.Statements)
        {
            lastType = InferNode(statement, result, scope);
        }

        return SetType(blockStatement, lastType, result);
    }

    private TypeSymbol InferArrayLiteral(ArrayLiteral arrayLiteral, TypeInferenceResult result, SymbolScope scope)
    {
        if (arrayLiteral.Elements.Count == 0)
        {
            return SetType(arrayLiteral, TypeSymbol.Array, result);
        }

        var elementType = InferExpression(arrayLiteral.Elements[0], result, scope);
        foreach (var element in arrayLiteral.Elements.Skip(1))
        {
            var currentType = InferExpression(element, result, scope);
            if (!elementType.IsCompatibleWith(currentType) || !currentType.IsCompatibleWith(elementType))
            {
                return AddErrorAndSetType(
                    arrayLiteral,
                    $"Type error: array elements must have the same type, but found {elementType} and {currentType}",
                    TypeSymbol.Unknown,
                    result);
            }
        }

        return SetType(arrayLiteral, TypeSymbol.ArrayOf(elementType), result);
    }

    private TypeSymbol InferHashLiteral(HashLiteral hashLiteral, TypeInferenceResult result, SymbolScope scope)
    {
        if (hashLiteral.Pairs.Count == 0)
        {
            return SetType(hashLiteral, TypeSymbol.HashMap, result);
        }

        var keyType = InferExpression(hashLiteral.Pairs[0].Key, result, scope);
        var valueType = InferExpression(hashLiteral.Pairs[0].Value, result, scope);
        foreach (var pair in hashLiteral.Pairs.Skip(1))
        {
            var currentKeyType = InferExpression(pair.Key, result, scope);
            var currentValueType = InferExpression(pair.Value, result, scope);
            if (!keyType.IsCompatibleWith(currentKeyType)
                || !currentKeyType.IsCompatibleWith(keyType)
                || !valueType.IsCompatibleWith(currentValueType)
                || !currentValueType.IsCompatibleWith(valueType))
            {
                return AddErrorAndSetType(
                    hashLiteral,
                    $"Type error: hash map keys and values must have the same type, but found ({keyType}, {valueType}) and ({currentKeyType}, {currentValueType})",
                    TypeSymbol.Unknown,
                    result);
            }
        }

        return SetType(hashLiteral, TypeSymbol.MapOf(keyType, valueType), result);
    }

    private TypeSymbol InferIdentifier(Identifier identifier, TypeInferenceResult result, SymbolScope scope)
    {
        if (scope.TryLookupVariable(identifier.Value, out var variableSymbol) && variableSymbol is not null)
        {
            return SetType(identifier, variableSymbol.Type, result);
        }

        if (scope.TryLookupFunction(identifier.Value, out var functionSymbol) && functionSymbol is not null)
        {
            return SetType(identifier, functionSymbol.Type, result);
        }

        return AddErrorAndSetType(identifier, $"Undefined variable: {identifier.Value}", TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferInfixExpression(InfixExpression infixExpression, TypeInferenceResult result, SymbolScope scope)
    {
        var leftType = InferExpression(infixExpression.Left, result, scope);
        var rightType = InferExpression(infixExpression.Right, result, scope);

        if (infixExpression.Operator == "+")
        {
            if (leftType == TypeSymbol.String && rightType == TypeSymbol.String)
            {
                return SetType(infixExpression, TypeSymbol.String, result);
            }

            if (leftType == TypeSymbol.Int && rightType == TypeSymbol.Int)
            {
                return SetType(infixExpression, TypeSymbol.Int, result);
            }

            if ((leftType == TypeSymbol.Int && rightType == TypeSymbol.Unknown)
                || (leftType == TypeSymbol.Unknown && rightType == TypeSymbol.Int))
            {
                return SetType(infixExpression, TypeSymbol.Int, result);
            }

            if ((leftType == TypeSymbol.String && rightType == TypeSymbol.Unknown)
                || (leftType == TypeSymbol.Unknown && rightType == TypeSymbol.String))
            {
                return SetType(infixExpression, TypeSymbol.String, result);
            }

            return AddErrorAndSetType(infixExpression, $"Type error: cannot apply operator '+' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
        }

        if (infixExpression.Operator is "-" or "*" or "/")
        {
            if (leftType == TypeSymbol.Unknown || rightType == TypeSymbol.Unknown)
            {
                return SetType(infixExpression, TypeSymbol.Int, result);
            }

            if (leftType != TypeSymbol.Int || rightType != TypeSymbol.Int)
            {
                return AddErrorAndSetType(infixExpression, $"Type error: cannot apply operator '{infixExpression.Operator}' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
            }

            return SetType(infixExpression, TypeSymbol.Int, result);
        }

        if (infixExpression.Operator is "==" or "!=" or "<" or ">")
        {
            if (leftType == TypeSymbol.Unknown || rightType == TypeSymbol.Unknown)
            {
                return SetType(infixExpression, TypeSymbol.Bool, result);
            }

            if (!leftType.IsCompatibleWith(rightType) || !rightType.IsCompatibleWith(leftType))
            {
                return AddErrorAndSetType(infixExpression, $"Type error: cannot compare types {leftType} and {rightType} with operator '{infixExpression.Operator}'", TypeSymbol.Unknown, result);
            }

            return SetType(infixExpression, TypeSymbol.Bool, result);
        }

        return AddErrorAndSetType(infixExpression, $"Type error: cannot apply operator '{infixExpression.Operator}' to types {leftType} and {rightType}", TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferNegationPrefix(PrefixExpression prefixExpression, TypeInferenceResult result, SymbolScope scope)
    {
        var rightType = InferExpression(prefixExpression.Right, result, scope);
        if (rightType != TypeSymbol.Int)
        {
            return AddErrorAndSetType(prefixExpression, $"Type error: cannot apply operator '-' to type {rightType}", TypeSymbol.Unknown, result);
        }

        return SetType(prefixExpression, TypeSymbol.Int, result);
    }

    private TypeSymbol InferBangPrefix(PrefixExpression prefixExpression, TypeInferenceResult result, SymbolScope scope)
    {
        var operandType = InferExpression(prefixExpression.Right, result, scope);
        if (operandType != TypeSymbol.Bool)
        {
            return AddErrorAndSetType(prefixExpression, $"Type error: cannot apply operator '!' to type {operandType}", TypeSymbol.Unknown, result);
        }

        return SetType(prefixExpression, TypeSymbol.Bool, result);
    }

    private TypeSymbol InferIfExpression(IfExpression ifExpression, TypeInferenceResult result, SymbolScope scope)
    {
        var conditionType = InferExpression(ifExpression.Condition, result, scope);
        if (conditionType != TypeSymbol.Bool)
        {
            return AddErrorAndSetType(ifExpression, $"Type error: if condition must be of type Boolean, but got {conditionType}", TypeSymbol.Unknown, result);
        }

        var thenScope = new SymbolScope(scope);
        var thenType = InferStatement(ifExpression.Consequence, result, thenScope);
        if (ifExpression.Alternative is null)
        {
            return SetType(ifExpression, TypeSymbol.Void, result);
        }

        var elseScope = new SymbolScope(scope);
        var elseType = InferStatement(ifExpression.Alternative, result, elseScope);
        if (!thenType.IsCompatibleWith(elseType) || !elseType.IsCompatibleWith(thenType))
        {
            return AddErrorAndSetType(ifExpression, $"Type error: if branches must have the same type, but got {thenType} and {elseType}", TypeSymbol.Unknown, result);
        }

        if (thenType == TypeSymbol.Void)
        {
            return AddErrorAndSetType(ifExpression, "Type error: if/else used as an expression must produce a value", TypeSymbol.Unknown, result);
        }

        return SetType(ifExpression, thenType, result);
    }

    private TypeSymbol InferIndexExpression(IndexExpression indexExpression, TypeInferenceResult result, SymbolScope scope)
    {
        var leftType = InferExpression(indexExpression.Left, result, scope);
        var leftKind = leftType.ToKongType();
        if (leftKind is not KongType.Array and not KongType.HashMap and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: index operator not supported for type {leftType}", TypeSymbol.Unknown, result);
        }

        var indexType = InferExpression(indexExpression.Index, result, scope);
        var indexKind = indexType.ToKongType();

        if (leftKind == KongType.Array && indexKind is not KongType.Int64 and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: array index must be Int64, but got {indexType}", TypeSymbol.Unknown, result);
        }

        if (leftKind == KongType.HashMap && indexKind is not KongType.Int64 and not KongType.Boolean and not KongType.String and not KongType.Unknown)
        {
            return AddErrorAndSetType(indexExpression, $"Type error: hash map index must be Int64, Boolean, or String, but got {indexType}", TypeSymbol.Unknown, result);
        }

        return SetType(indexExpression, TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferFunctionLiteral(
        FunctionLiteral functionLiteral,
        FunctionSymbol? functionSymbol,
        TypeInferenceResult result,
        SymbolScope scope,
        bool recordTopLevelSignature)
    {
        var resolvedFunctionSymbol = functionSymbol ?? CreateAnonymousFunctionSymbol(functionLiteral, result);
        var localScope = new SymbolScope(scope);

        if (!string.IsNullOrEmpty(functionLiteral.Name))
        {
            localScope.Define(resolvedFunctionSymbol);
        }

        foreach (var parameter in resolvedFunctionSymbol.Parameters)
        {
            localScope.Define(parameter);
        }

        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            SetType(functionLiteral.Parameters[i].Name, resolvedFunctionSymbol.Parameters[i].Type, result);
        }

        var bodyType = InferBlockStatement(functionLiteral.Body, result, localScope);
        resolvedFunctionSymbol.SetReturnType(bodyType);

        if (recordTopLevelSignature && !string.IsNullOrEmpty(functionLiteral.Name))
        {
            result.AddFunctionSignature(
                functionLiteral.Name,
                resolvedFunctionSymbol.Parameters.Select(p => p.Type).ToList(),
                bodyType);
        }

        return SetType(functionLiteral, resolvedFunctionSymbol.Type, result);
    }

    private static FunctionSymbol CreateAnonymousFunctionSymbol(FunctionLiteral functionLiteral, TypeInferenceResult result)
    {
        var parameterTypes = ParseFunctionParameterTypes(functionLiteral, result, reportErrors: true);
        var parameterSymbols = CreateParameterSymbols(functionLiteral, parameterTypes);
        var name = string.IsNullOrEmpty(functionLiteral.Name) ? "<anonymous>" : functionLiteral.Name;
        return new FunctionSymbol(name, parameterSymbols, TypeSymbol.Unknown);
    }

    private TypeSymbol InferCallExpression(CallExpression callExpression, TypeInferenceResult result, SymbolScope scope)
    {
        if (callExpression.Function is Identifier { Value: "puts" })
        {
            foreach (var argument in callExpression.Arguments)
            {
                InferExpression(argument, result, scope);
            }

            return SetType(callExpression, TypeSymbol.Void, result);
        }

        if (callExpression.Function is Identifier { Value: "len" })
        {
            if (callExpression.Arguments.Count != 1)
            {
                return AddErrorAndSetType(callExpression, $"wrong number of arguments. got={callExpression.Arguments.Count}, want=1", TypeSymbol.Unknown, result);
            }

            var argumentType = InferExpression(callExpression.Arguments[0], result, scope);
            var argumentKind = argumentType.ToKongType();
            if (argumentKind is KongType.String or KongType.Array or KongType.Unknown)
            {
                return SetType(callExpression, TypeSymbol.Int, result);
            }

            return AddErrorAndSetType(callExpression, $"argument to `len` not supported, got {argumentType}", TypeSymbol.Unknown, result);
        }

        if (callExpression.Function is Identifier { Value: "push" })
        {
            if (callExpression.Arguments.Count != 2)
            {
                return AddErrorAndSetType(callExpression, $"wrong number of arguments. got={callExpression.Arguments.Count}, want=2", TypeSymbol.Unknown, result);
            }

            var arrayType = InferExpression(callExpression.Arguments[0], result, scope);
            InferExpression(callExpression.Arguments[1], result, scope);

            var arrayKind = arrayType.ToKongType();
            if (arrayKind is KongType.Array or KongType.Unknown)
            {
                return SetType(callExpression, arrayKind == KongType.Array ? arrayType : TypeSymbol.Array, result);
            }

            return AddErrorAndSetType(callExpression, $"argument to `push` must be ARRAY, got {arrayType}", TypeSymbol.Unknown, result);
        }

        var functionType = InferExpression(callExpression.Function, result, scope);
        foreach (var argument in callExpression.Arguments)
        {
            InferExpression(argument, result, scope);
        }

        if (callExpression.Function is Identifier identifier
            && scope.TryLookupFunction(identifier.Value, out var functionSymbol)
            && functionSymbol is not null)
        {
            return InferTypedFunctionCall(
                callExpression,
                functionSymbol.Name,
                functionSymbol.Parameters.Select(p => p.Type).ToList(),
                functionSymbol.ReturnType,
                result);
        }

        if (functionType is FunctionTypeSymbol functionTypeSymbol)
        {
            return InferTypedFunctionCall(
                callExpression,
                callExpression.Function.String(),
                functionTypeSymbol.ParameterTypes,
                functionTypeSymbol.ReturnType,
                result);
        }

        return SetType(callExpression, TypeSymbol.Unknown, result);
    }

    private TypeSymbol InferTypedFunctionCall(
        CallExpression callExpression,
        string functionName,
        IReadOnlyList<TypeSymbol> parameterTypes,
        TypeSymbol returnType,
        TypeInferenceResult result)
    {
        if (callExpression.Arguments.Count != parameterTypes.Count)
        {
            return AddErrorAndSetType(
                callExpression,
                $"wrong number of arguments for {functionName}: want={parameterTypes.Count}, got={callExpression.Arguments.Count}",
                TypeSymbol.Unknown,
                result);
        }

        for (var i = 0; i < callExpression.Arguments.Count; i++)
        {
            var argumentType = result.GetNodeTypeSymbol(callExpression.Arguments[i]);
            var parameterType = parameterTypes[i];
            if (argumentType != TypeSymbol.Unknown
                && parameterType != TypeSymbol.Unknown
                && (!parameterType.IsCompatibleWith(argumentType) || !argumentType.IsCompatibleWith(parameterType)))
            {
                return AddErrorAndSetType(
                    callExpression,
                    $"Type error: argument {i + 1} for {functionName} expects {parameterType}, got {argumentType}",
                    TypeSymbol.Unknown,
                    result);
            }
        }

        return SetType(callExpression, returnType, result);
    }

    private static TypeSymbol SetType(INode node, TypeSymbol type, TypeInferenceResult result)
    {
        result.AddNodeType(node, type);
        return type;
    }

    private static TypeSymbol AddErrorAndSetType(INode node, string error, TypeSymbol type, TypeInferenceResult result)
    {
        result.AddError(error);
        return SetType(node, type, result);
    }

    private TypeSymbol InferUnsupported(INode node, TypeInferenceResult result)
    {
        return AddErrorAndSetType(node, $"Unsupported node type: {node.GetType().Name}", TypeSymbol.Unknown, result);
    }
}
