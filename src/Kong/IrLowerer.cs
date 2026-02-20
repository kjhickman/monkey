namespace Kong;

public class IrLowerer
{
    private IrLoweringResult _result = new();
    private readonly Dictionary<IExpression, TypeSymbol> _expressionTypes = [];
    private readonly Dictionary<LetStatement, TypeSymbol> _variableTypes = [];
    private readonly Dictionary<FunctionDeclaration, FunctionTypeSymbol> _declaredFunctionTypes = [];
    private readonly Dictionary<string, FunctionTypeSymbol> _declaredFunctionTypesByName = [];
    private NameResolution? _nameResolution;

    private IrProgram _program = null!;
    private IrFunction _function = null!;
    private IrBlock _currentBlock = null!;

    private readonly Dictionary<string, IrLocalId> _localsByName = [];

    private int _nextValueId;
    private int _nextLocalId;
    private int _nextBlockId;
    private int _nextLambdaId;
    private readonly BuiltinRegistry _builtinRegistry = BuiltinRegistry.Default;


    public IrLoweringResult Lower(CompilationUnit unit, TypeCheckResult typeCheckResult)
    {
        return Lower(unit, typeCheckResult, nameResolution: null);
    }

    public IrLoweringResult Lower(CompilationUnit unit, TypeCheckResult typeCheckResult, NameResolution? nameResolution)
    {
        _result = new IrLoweringResult();
        _nameResolution = nameResolution;
        _expressionTypes.Clear();
        _variableTypes.Clear();
        _declaredFunctionTypes.Clear();
        _declaredFunctionTypesByName.Clear();
        foreach (var pair in typeCheckResult.ExpressionTypes)
        {
            _expressionTypes[pair.Key] = pair.Value;
        }

        foreach (var pair in typeCheckResult.VariableTypes)
        {
            _variableTypes[pair.Key] = pair.Value;
        }

        foreach (var pair in typeCheckResult.DeclaredFunctionTypes)
        {
            _declaredFunctionTypes[pair.Key] = pair.Value;
            _declaredFunctionTypesByName[pair.Key.Name.Value] = pair.Value;
        }

        _result.Diagnostics.AddRange(typeCheckResult.Diagnostics);
        if (_result.Diagnostics.HasErrors)
        {
            return _result;
        }

        var entryFunction = new IrFunction
        {
            Name = "__main",
            ReturnType = TypeSymbols.Int,
        };
        _program = new IrProgram { EntryPoint = entryFunction };
        if (!LowerFunctionBody(entryFunction, unit.Statements, isTopLevel: true))
        {
            return _result;
        }

        _result.Program = _program;
        return _result;
    }

    private bool LowerFunctionBody(
        IrFunction function,
        IReadOnlyList<IStatement> statements,
        bool isTopLevel)
    {
        var savedFunction = _function;
        var savedBlock = _currentBlock;
        var savedNextValueId = _nextValueId;
        var savedNextLocalId = _nextLocalId;
        var savedNextBlockId = _nextBlockId;
        var savedLocals = _localsByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _function = function;
        _nextValueId = 0;
        _nextLocalId = 0;
        _nextBlockId = 0;
        _localsByName.Clear();

        foreach (var parameter in function.Parameters)
        {
            _localsByName[parameter.Name] = parameter.LocalId;
            _function.LocalTypes[parameter.LocalId] = parameter.Type;
            if (parameter.LocalId.Id >= _nextLocalId)
            {
                _nextLocalId = parameter.LocalId.Id + 1;
            }
        }

        _currentBlock = NewBlock();

        IrValueId? finalExpressionValue = null;
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var isLast = i == statements.Count - 1;

            switch (statement)
            {
                case LetStatement letStatement:
                    if (!LowerLetStatement(letStatement))
                    {
                        Restore();
                        return false;
                    }
                    break;

                case FunctionDeclaration functionDeclaration:
                    if (!LowerFunctionDeclaration(functionDeclaration, isTopLevel))
                    {
                        Restore();
                        return false;
                    }
                    break;

                case ReturnStatement returnStatement:
                {
                    if (returnStatement.ReturnValue == null)
                    {
                        if (_function.ReturnType == TypeSymbols.Void)
                        {
                            _currentBlock.Terminator = new IrReturnVoid();
                            break;
                        }

                        _result.Diagnostics.Report(returnStatement.Span,
                            "phase-4 IR lowerer requires explicit return expressions",
                            "IR001");
                        Restore();
                        return false;
                    }

                    var returnValue = LowerExpression(returnStatement.ReturnValue);
                    if (returnValue == null)
                    {
                        Restore();
                        return false;
                    }

                    _currentBlock.Terminator = new IrReturn(returnValue.Value);
                    break;
                }

                case ExpressionStatement { Expression: { } expression }:
                {
                    var expressionValue = LowerExpression(expression);
                    if (expressionValue == null)
                    {
                        if (!TryGetExpressionType(expression, out var expressionType) || expressionType != TypeSymbols.Void)
                        {
                            Restore();
                            return false;
                        }
                    }
                    else if (isLast)
                    {
                        finalExpressionValue = expressionValue.Value;
                    }

                    break;
                }

                default:
                    _result.Diagnostics.Report(statement.Span,
                        "phase-4 IR lowerer supports let, return, and expression statements only",
                        "IR001");
                    Restore();
                    return false;
            }

            if (_currentBlock.Terminator != null)
            {
                break;
            }
        }

        if (_currentBlock.Terminator == null)
        {
            if (_function.ReturnType == TypeSymbols.Void)
            {
                _currentBlock.Terminator = new IrReturnVoid();
                Restore();
                return true;
            }

            if (finalExpressionValue == null)
            {
                if (isTopLevel)
                {
                    var zero = AllocateValue(TypeSymbols.Int);
                    _currentBlock.Instructions.Add(new IrConstInt(zero, 0));
                    _currentBlock.Terminator = new IrReturn(zero);
                    Restore();
                    return true;
                }

                _result.Diagnostics.Report(Span.Empty,
                    "phase-4 IR lowerer requires a final expression or return statement",
                    "IR001");
                Restore();
                return false;
            }

            if (isTopLevel)
            {
                if (!TryGetValueType(finalExpressionValue.Value, out var finalType) || finalType != TypeSymbols.Int)
                {
                    _result.Diagnostics.Report(Span.Empty,
                        "phase-4 IR lowerer requires top-level expression result type to be 'int'",
                        "IR001");
                    Restore();
                    return false;
                }
            }

            _currentBlock.Terminator = new IrReturn(finalExpressionValue.Value);
        }

        Restore();
        return true;

        void Restore()
        {
            _function = savedFunction;
            _currentBlock = savedBlock;
            _nextValueId = savedNextValueId;
            _nextLocalId = savedNextLocalId;
            _nextBlockId = savedNextBlockId;
            _localsByName.Clear();
            foreach (var pair in savedLocals)
            {
                _localsByName[pair.Key] = pair.Value;
            }

        }
    }

    private bool LowerFunctionDeclaration(FunctionDeclaration declaration, bool isTopLevel)
    {
        if (!isTopLevel)
        {
            _result.Diagnostics.Report(declaration.Span,
                "phase-6 IR lowerer currently supports function declarations only at top-level",
                "IR001");
            return false;
        }

        if (!_declaredFunctionTypes.TryGetValue(declaration, out var functionType))
        {
            _result.Diagnostics.Report(declaration.Span,
                "missing declared function type for IR lowering",
                "IR002");
            return false;
        }

        var lowered = LowerFunctionLiteral(declaration.ToFunctionLiteral(), declaration.Name.Value, functionType);
        if (lowered == null)
        {
            return false;
        }

        var local = AllocateNamedLocal(declaration.Name.Value, functionType);
        var closureValue = AllocateValue(functionType);
        _currentBlock.Instructions.Add(new IrCreateClosure(closureValue, lowered.Value.FunctionName, lowered.Value.CapturedLocals));
        _currentBlock.Instructions.Add(new IrStoreLocal(local, closureValue));
        return true;
    }

    private bool LowerLetStatement(LetStatement statement)
    {
        if (!_variableTypes.TryGetValue(statement, out var variableType))
        {
            _result.Diagnostics.Report(statement.Span, "missing variable type for IR lowering", "IR002");
            return false;
        }

        if (!IsSupportedRuntimeType(variableType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"phase-4 IR lowerer does not support let type '{variableType}'",
                "IR001");
            return false;
        }

        if (statement.Value == null)
        {
            _result.Diagnostics.Report(statement.Span,
                "phase-4 IR lowerer requires let initializers",
                "IR001");
            return false;
        }

        var local = AllocateNamedLocal(statement.Name.Value, variableType);

        var value = LowerExpression(statement.Value);
        if (value == null)
        {
            return false;
        }

        _currentBlock.Instructions.Add(new IrStoreLocal(local, value.Value));
        return true;
    }

    private (string FunctionName, IReadOnlyList<IrLocalId> CapturedLocals)? LowerFunctionLiteral(
        FunctionLiteral functionLiteral,
        string? preferredName,
        FunctionTypeSymbol? predeclaredType = null)
    {
        FunctionTypeSymbol functionType;
        if (predeclaredType != null)
        {
            functionType = predeclaredType;
        }
        else if (!TryGetExpressionType(functionLiteral, out var functionTypeSymbol) || functionTypeSymbol is not FunctionTypeSymbol inferredFunctionType)
        {
            _result.Diagnostics.Report(functionLiteral.Span, "missing function type for IR lowering", "IR002");
            return null;
        }
        else
        {
            functionType = inferredFunctionType;
        }

        if (!IsSupportedRuntimeType(functionType.ReturnType))
        {
            _result.Diagnostics.Report(functionLiteral.Span,
                $"phase-4 IR lowerer does not support function return type '{functionType.ReturnType}'",
                "IR001");
            return null;
        }

        var functionName = preferredName == null
            ? $"__lambda{_nextLambdaId++}"
            : predeclaredType != null
                ? preferredName
                : $"__fn_{preferredName}_{_nextLambdaId++}";

        var capturedSymbols = _nameResolution?.GetCapturedSymbols(functionLiteral) ?? [];
        var capturedLocals = new List<IrLocalId>(capturedSymbols.Count);

        var function = new IrFunction
        {
            Name = functionName,
            ReturnType = functionType.ReturnType,
            CaptureParameterCount = capturedSymbols.Count,
        };

        if (functionLiteral.Parameters.Count != functionType.ParameterTypes.Count)
        {
            _result.Diagnostics.Report(functionLiteral.Span,
                "phase-4 IR lowerer could not match parameter count to function type",
                "IR002");
            return null;
        }

        foreach (var capture in capturedSymbols)
        {
            if (!_localsByName.TryGetValue(capture.Name, out var capturedLocal))
            {
                _result.Diagnostics.Report(functionLiteral.Span,
                    $"phase-6 IR lowerer could not resolve captured symbol '{capture.Name}'",
                    "IR002");
                return null;
            }

            if (!_function.LocalTypes.TryGetValue(capturedLocal, out var capturedType))
            {
                _result.Diagnostics.Report(functionLiteral.Span,
                    $"phase-6 IR lowerer could not resolve captured type for '{capture.Name}'",
                    "IR002");
                return null;
            }

            var captureParamLocalId = new IrLocalId(function.Parameters.Count);
            function.LocalTypes[captureParamLocalId] = capturedType;
            function.Parameters.Add(new IrParameter(captureParamLocalId, capture.Name, capturedType));
            capturedLocals.Add(capturedLocal);
        }

        for (var i = 0; i < functionLiteral.Parameters.Count; i++)
        {
            var parameter = functionLiteral.Parameters[i];
            var parameterType = functionType.ParameterTypes[i];
            if (!IsSupportedRuntimeType(parameterType))
            {
                _result.Diagnostics.Report(parameter.Span,
                    $"phase-4 IR lowerer does not support parameter type '{parameterType}'",
                    "IR001");
                return null;
            }

            var localId = new IrLocalId(function.Parameters.Count);
            function.LocalTypes[localId] = parameterType;
            function.Parameters.Add(new IrParameter(localId, parameter.Name, parameterType));
        }

        var bodyLowered = LowerFunctionBody(
            function,
            functionLiteral.Body.Statements,
            isTopLevel: false);

        if (!bodyLowered)
        {
            return null;
        }

        _program.Functions.Add(function);
        return (functionName, capturedLocals);
    }

    private IrValueId? LowerExpression(IExpression expression)
    {
        switch (expression)
        {
            case IntegerLiteral integerLiteral:
            {
                var destination = AllocateValue(TypeSymbols.Int);
                _currentBlock.Instructions.Add(new IrConstInt(destination, integerLiteral.Value));
                return destination;
            }

            case BooleanLiteral booleanLiteral:
            {
                var destination = AllocateValue(TypeSymbols.Bool);
                _currentBlock.Instructions.Add(new IrConstBool(destination, booleanLiteral.Value));
                return destination;
            }

            case StringLiteral stringLiteral:
            {
                var destination = AllocateValue(TypeSymbols.String);
                _currentBlock.Instructions.Add(new IrConstString(destination, stringLiteral.Value));
                return destination;
            }

            case FunctionLiteral functionLiteral:
            {
                var lowered = LowerFunctionLiteral(functionLiteral, preferredName: null);
                if (lowered == null)
                {
                    return null;
                }

                if (!TryGetExpressionType(functionLiteral, out var functionType) || functionType is not FunctionTypeSymbol)
                {
                    _result.Diagnostics.Report(functionLiteral.Span,
                        "phase-6 IR lowerer requires function type for function literal",
                        "IR002");
                    return null;
                }

                var destination = AllocateValue(functionType);
                _currentBlock.Instructions.Add(new IrCreateClosure(destination, lowered.Value.FunctionName, lowered.Value.CapturedLocals));
                return destination;
            }

            case Identifier identifier:
            {
                if (_localsByName.TryGetValue(identifier.Value, out var local))
                {
                    if (!TryGetExpressionType(identifier, out var identifierType))
                    {
                        return null;
                    }

                    var destination = AllocateValue(identifierType);
                    _currentBlock.Instructions.Add(new IrLoadLocal(destination, local));
                    return destination;
                }

                if (_declaredFunctionTypesByName.TryGetValue(identifier.Value, out var declaredFunctionType))
                {
                    var destination = AllocateValue(declaredFunctionType);
                    _currentBlock.Instructions.Add(new IrCreateClosure(destination, identifier.Value, []));
                    return destination;
                }

                _result.Diagnostics.Report(identifier.Span,
                    $"phase-4 IR lowerer could not resolve local '{identifier.Value}'",
                    "IR002");

                return null;
            }

            case PrefixExpression prefixExpression when prefixExpression.Operator == "-":
            {
                if (!TryGetExpressionType(prefixExpression.Right, out var rightType) || rightType != TypeSymbols.Int)
                {
                    _result.Diagnostics.Report(prefixExpression.Span,
                        "phase-4 IR lowerer supports unary '-' only for int",
                        "IR001");
                    return null;
                }

                var zero = AllocateValue(TypeSymbols.Int);
                _currentBlock.Instructions.Add(new IrConstInt(zero, 0));

                var right = LowerExpression(prefixExpression.Right);
                if (right == null)
                {
                    return null;
                }

                var destination = AllocateValue(TypeSymbols.Int);
                _currentBlock.Instructions.Add(new IrBinary(destination, IrBinaryOperator.Subtract, zero, right.Value));
                return destination;
            }

            case PrefixExpression prefixExpression when prefixExpression.Operator == "!":
            {
                if (!TryGetExpressionType(prefixExpression.Right, out var rightType) || rightType != TypeSymbols.Bool)
                {
                    _result.Diagnostics.Report(prefixExpression.Span,
                        "phase-6 IR lowerer supports unary '!' only for bool",
                        "IR001");
                    return null;
                }

                var right = LowerExpression(prefixExpression.Right);
                if (right == null)
                {
                    return null;
                }

                var falseValue = AllocateValue(TypeSymbols.Bool);
                _currentBlock.Instructions.Add(new IrConstBool(falseValue, false));

                var destination = AllocateValue(TypeSymbols.Bool);
                _currentBlock.Instructions.Add(new IrBinary(destination, IrBinaryOperator.Equal, right.Value, falseValue));
                return destination;
            }

            case InfixExpression infixExpression:
            {
                if (!TryMapBinaryOperator(infixExpression.Operator, out var op))
                {
                    _result.Diagnostics.Report(infixExpression.Span,
                        $"phase-4 IR lowerer does not support operator '{infixExpression.Operator}'",
                        "IR001");
                    return null;
                }

                if (!TryGetExpressionType(infixExpression.Left, out var leftType) ||
                    !TryGetExpressionType(infixExpression.Right, out var rightType))
                {
                    return null;
                }

                var areBothInt = leftType == TypeSymbols.Int && rightType == TypeSymbols.Int;
                var areSameType = TypeEquals(leftType, rightType);
                var supportsEqualityType = leftType == TypeSymbols.Int || leftType == TypeSymbols.Bool || leftType == TypeSymbols.String;

                if (op is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply or IrBinaryOperator.Divide)
                {
                    if (!areBothInt)
                    {
                        _result.Diagnostics.Report(infixExpression.Span,
                            $"phase-6 IR lowerer supports arithmetic operators only for int, got '{leftType}' and '{rightType}'",
                            "IR001");
                        return null;
                    }
                }
                else if (op is IrBinaryOperator.LessThan or IrBinaryOperator.GreaterThan)
                {
                    if (!areBothInt)
                    {
                        _result.Diagnostics.Report(infixExpression.Span,
                            $"phase-6 IR lowerer supports comparison operators only for int, got '{leftType}' and '{rightType}'",
                            "IR001");
                        return null;
                    }
                }
                else if (op is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual)
                {
                    if (!areSameType || !supportsEqualityType)
                    {
                        _result.Diagnostics.Report(infixExpression.Span,
                            $"phase-6 IR lowerer supports equality operators for matching int/bool/string types, got '{leftType}' and '{rightType}'",
                            "IR001");
                        return null;
                    }
                }

                var left = LowerExpression(infixExpression.Left);
                var right = LowerExpression(infixExpression.Right);
                if (left == null || right == null)
                {
                    return null;
                }

                if (!TryGetExpressionType(infixExpression, out var expressionType))
                {
                    return null;
                }

                var destination = AllocateValue(expressionType);
                _currentBlock.Instructions.Add(new IrBinary(destination, op, left.Value, right.Value));
                return destination;
            }

            case IfExpression ifExpression:
                return LowerIfExpression(ifExpression);

            case CallExpression callExpression:
                return LowerCallExpression(callExpression);

            case ArrayLiteral arrayLiteral:
                return LowerArrayLiteral(arrayLiteral);

            case IndexExpression indexExpression:
                return LowerIndexExpression(indexExpression);

            default:
                _result.Diagnostics.Report(expression.Span,
                    $"phase-4 IR lowerer does not support expression '{expression.TokenLiteral()}'",
                    "IR001");
                return null;
        }
    }

    private IrValueId? LowerIfExpression(IfExpression ifExpression)
    {
        if (!TryGetExpressionType(ifExpression, out var resultType) || !IsSupportedRuntimeType(resultType))
        {
            _result.Diagnostics.Report(ifExpression.Span,
                "phase-4 IR lowerer requires supported if-expression result type",
                "IR001");
            return null;
        }

        if (ifExpression.Alternative == null)
        {
            _result.Diagnostics.Report(ifExpression.Span,
                "phase-4 IR lowerer requires if expressions with else branch",
                "IR001");
            return null;
        }

        if (!TryGetExpressionType(ifExpression.Condition, out var conditionType) || conditionType != TypeSymbols.Bool)
        {
            _result.Diagnostics.Report(ifExpression.Condition.Span,
                "phase-4 IR lowerer requires bool if condition",
                "IR001");
            return null;
        }

        var condition = LowerExpression(ifExpression.Condition);
        if (condition == null)
        {
            return null;
        }

        if (resultType == TypeSymbols.Void)
        {
            return LowerVoidIfExpression(ifExpression, condition.Value);
        }

        var resultLocal = AllocateAnonymousLocal(resultType);

        var thenBlock = NewBlock();
        var elseBlock = NewBlock();
        var mergeBlock = NewBlock();

        _currentBlock.Terminator = new IrBranch(condition.Value, thenBlock.Id, elseBlock.Id);

        _currentBlock = thenBlock;
        var thenValue = LowerBlockResultExpression(ifExpression.Consequence, resultType);
        if (thenValue == null)
        {
            return null;
        }
        _currentBlock.Instructions.Add(new IrStoreLocal(resultLocal, thenValue.Value));
        _currentBlock.Terminator = new IrJump(mergeBlock.Id);

        _currentBlock = elseBlock;
        var elseValue = LowerBlockResultExpression(ifExpression.Alternative, resultType);
        if (elseValue == null)
        {
            return null;
        }
        _currentBlock.Instructions.Add(new IrStoreLocal(resultLocal, elseValue.Value));
        _currentBlock.Terminator = new IrJump(mergeBlock.Id);

        _currentBlock = mergeBlock;
        var destination = AllocateValue(resultType);
        _currentBlock.Instructions.Add(new IrLoadLocal(destination, resultLocal));
        return destination;
    }

    private IrValueId? LowerVoidIfExpression(IfExpression ifExpression, IrValueId condition)
    {
        var thenBlock = NewBlock();
        var elseBlock = NewBlock();
        var mergeBlock = NewBlock();

        _currentBlock.Terminator = new IrBranch(condition, thenBlock.Id, elseBlock.Id);

        _currentBlock = thenBlock;
        var thenTerminated = LowerVoidBranchBlock(ifExpression.Consequence, mergeBlock.Id);
        if (thenTerminated == null)
        {
            return null;
        }

        _currentBlock = elseBlock;
        var elseTerminated = LowerVoidBranchBlock(ifExpression.Alternative!, mergeBlock.Id);
        if (elseTerminated == null)
        {
            return null;
        }

        if (thenTerminated.Value && elseTerminated.Value)
        {
            _function.Blocks.Remove(mergeBlock);
            _currentBlock = thenBlock;
            return null;
        }

        _currentBlock = mergeBlock;
        return null;
    }

    private bool? LowerVoidBranchBlock(BlockStatement block, int mergeBlockId)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case LetStatement letStatement:
                    if (!LowerLetStatement(letStatement))
                    {
                        return null;
                    }
                    break;

                case ReturnStatement returnStatement:
                    if (returnStatement.ReturnValue == null)
                    {
                        if (_function.ReturnType == TypeSymbols.Void)
                        {
                            _currentBlock.Terminator = new IrReturnVoid();
                            return true;
                        }

                        _result.Diagnostics.Report(returnStatement.Span,
                            "phase-6 IR lowerer requires return values for non-void functions",
                            "IR001");
                        return null;
                    }

                    var returnValue = LowerExpression(returnStatement.ReturnValue);
                    if (returnValue == null)
                    {
                        return null;
                    }

                    _currentBlock.Terminator = new IrReturn(returnValue.Value);
                    return true;

                case ExpressionStatement { Expression: { } expression }:
                {
                    var value = LowerExpression(expression);
                    if (value == null && (!TryGetExpressionType(expression, out var type) || type != TypeSymbols.Void))
                    {
                        return null;
                    }

                    break;
                }

                default:
                    _result.Diagnostics.Report(statement.Span,
                        "phase-6 IR lowerer does not support this statement in if branch",
                        "IR001");
                    return null;
            }

            if (_currentBlock.Terminator != null)
            {
                return true;
            }
        }

        _currentBlock.Terminator = new IrJump(mergeBlockId);
        return false;
    }

    private IrValueId? LowerBlockResultExpression(BlockStatement block, TypeSymbol expectedType)
    {
        IrValueId? result = null;
        for (var i = 0; i < block.Statements.Count; i++)
        {
            var statement = block.Statements[i];
            var isLast = i == block.Statements.Count - 1;

            switch (statement)
            {
                case LetStatement letStatement:
                    if (!LowerLetStatement(letStatement))
                    {
                        return null;
                    }
                    break;

                case ExpressionStatement { Expression: { } expression }:
                {
                    var value = LowerExpression(expression);
                    if (value == null)
                    {
                        return null;
                    }

                    if (isLast)
                    {
                        if (!TryGetExpressionType(expression, out var type) || type != expectedType)
                        {
                            _result.Diagnostics.Report(expression.Span,
                                $"phase-4 IR lowerer expected block expression type '{expectedType}'",
                                "IR001");
                            return null;
                        }

                        result = value.Value;
                    }
                    break;
                }

                default:
                    _result.Diagnostics.Report(statement.Span,
                        "phase-4 IR lowerer requires if branch blocks to end with an expression statement",
                        "IR001");
                    return null;
            }
        }

        if (result == null)
        {
            _result.Diagnostics.Report(block.Span,
                "phase-4 IR lowerer requires non-empty if branch blocks with expression result",
                "IR001");
        }

        return result;
    }

    private IrValueId? LowerCallExpression(CallExpression callExpression)
    {
        if (!TryGetExpressionType(callExpression, out var returnType) || !IsSupportedRuntimeType(returnType))
        {
            _result.Diagnostics.Report(callExpression.Span,
                "phase-4 IR lowerer requires supported call return type",
                "IR001");
            return null;
        }

        if (callExpression.Function is Identifier builtinIdentifier &&
            ((_nameResolution != null &&
              _nameResolution.IdentifierSymbols.TryGetValue(builtinIdentifier, out var symbol) &&
              symbol.Kind == NameSymbolKind.Builtin) ||
             _builtinRegistry.IsDefined(builtinIdentifier.Value)) &&
            TryLowerBuiltinCallName(builtinIdentifier, callExpression, out var builtinName))
        {
            var builtinArguments = new List<IrValueId>(callExpression.Arguments.Count);
            foreach (var argument in callExpression.Arguments)
            {
                var value = LowerExpression(argument);
                if (value == null)
                {
                    return null;
                }

                builtinArguments.Add(value.Value);
            }

            if (returnType == TypeSymbols.Void)
            {
                _currentBlock.Instructions.Add(new IrCallVoid(builtinName, builtinArguments));
                return null;
            }

            var builtinDestination = AllocateValue(returnType);
            _currentBlock.Instructions.Add(new IrCall(builtinDestination, builtinName, builtinArguments));
            return builtinDestination;
        }

        var closureValue = LowerExpression(callExpression.Function);
        if (closureValue == null)
        {
            return null;
        }

        var arguments = new List<IrValueId>(callExpression.Arguments.Count);
        foreach (var argument in callExpression.Arguments)
        {
            var value = LowerExpression(argument);
            if (value == null)
            {
                return null;
            }

            arguments.Add(value.Value);
        }

        if (returnType == TypeSymbols.Void)
        {
            _currentBlock.Instructions.Add(new IrInvokeClosureVoid(closureValue.Value, arguments));
            return null;
        }

        var destination = AllocateValue(returnType);
        _currentBlock.Instructions.Add(new IrInvokeClosure(destination, closureValue.Value, arguments));
        return destination;
    }

    private bool TryLowerBuiltinCallName(Identifier identifier, CallExpression callExpression, out string builtinName)
    {
        builtinName = string.Empty;

        var argumentTypes = new List<TypeSymbol>(callExpression.Arguments.Count);
        foreach (var argument in callExpression.Arguments)
        {
            if (!TryGetExpressionType(argument, out var argType))
            {
                return false;
            }
            argumentTypes.Add(argType);
        }

        var binding = _builtinRegistry.ResolveByTypeSignature(identifier.Value, argumentTypes);
        if (binding != null)
        {
            builtinName = binding.Signature.IrFunctionName;
            return true;
        }

        return false;
    }


    private IrValueId? LowerArrayLiteral(ArrayLiteral arrayLiteral)
    {
        if (!TryGetExpressionType(arrayLiteral, out var arrayType) || arrayType is not ArrayTypeSymbol { ElementType: var elementType })
        {
            _result.Diagnostics.Report(arrayLiteral.Span,
                "phase-5 IR lowerer requires array literal type information",
                "IR002");
            return null;
        }

        if (elementType != TypeSymbols.Int)
        {
            _result.Diagnostics.Report(arrayLiteral.Span,
                $"phase-5 IR lowerer supports only int[] literals, got '{arrayType}'",
                "IR001");
            return null;
        }

        var elements = new List<IrValueId>(arrayLiteral.Elements.Count);
        foreach (var elementExpression in arrayLiteral.Elements)
        {
            var lowered = LowerExpression(elementExpression);
            if (lowered == null)
            {
                return null;
            }

            elements.Add(lowered.Value);
        }

        var destination = AllocateValue(arrayType);
        _currentBlock.Instructions.Add(new IrNewIntArray(destination, elements));
        return destination;
    }

    private IrValueId? LowerIndexExpression(IndexExpression indexExpression)
    {
        if (!TryGetExpressionType(indexExpression.Left, out var leftType) || leftType is not ArrayTypeSymbol { ElementType: var elementType })
        {
            _result.Diagnostics.Report(indexExpression.Left.Span,
                "phase-5 IR lowerer supports indexing only on int[]",
                "IR001");
            return null;
        }

        if (elementType != TypeSymbols.Int)
        {
            _result.Diagnostics.Report(indexExpression.Left.Span,
                $"phase-5 IR lowerer supports indexing only on int[]; got '{leftType}'",
                "IR001");
            return null;
        }

        if (!TryGetExpressionType(indexExpression.Index, out var indexType) || indexType != TypeSymbols.Int)
        {
            _result.Diagnostics.Report(indexExpression.Index.Span,
                "phase-5 IR lowerer requires int index expressions",
                "IR001");
            return null;
        }

        var left = LowerExpression(indexExpression.Left);
        var index = LowerExpression(indexExpression.Index);
        if (left == null || index == null)
        {
            return null;
        }

        var destination = AllocateValue(TypeSymbols.Int);
        _currentBlock.Instructions.Add(new IrIntArrayIndex(destination, left.Value, index.Value));
        return destination;
    }

    private IrBlock NewBlock()
    {
        var block = new IrBlock { Id = _nextBlockId++ };
        _function.Blocks.Add(block);
        return block;
    }

    private bool TryGetExpressionType(IExpression expression, out TypeSymbol type)
    {
        if (_expressionTypes.TryGetValue(expression, out type!))
        {
            return true;
        }

        _result.Diagnostics.Report(expression.Span,
            "missing expression type for IR lowering",
            "IR002");
        type = TypeSymbols.Error;
        return false;
    }

    private bool TryGetValueType(IrValueId valueId, out TypeSymbol type)
    {
        if (_function.ValueTypes.TryGetValue(valueId, out type!))
        {
            return true;
        }

        type = TypeSymbols.Error;
        return false;
    }

    private IrValueId AllocateValue(TypeSymbol type)
    {
        var id = new IrValueId(_nextValueId++);
        _function.ValueTypes[id] = type;
        return id;
    }

    private IrLocalId AllocateNamedLocal(string name, TypeSymbol type)
    {
        if (_localsByName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var id = new IrLocalId(_nextLocalId++);
        _localsByName[name] = id;
        _function.LocalTypes[id] = type;
        return id;
    }

    private IrLocalId AllocateAnonymousLocal(TypeSymbol type)
    {
        var id = new IrLocalId(_nextLocalId++);
        _function.LocalTypes[id] = type;
        return id;
    }

    private static bool IsSupportedRuntimeType(TypeSymbol type)
    {
        if (type == TypeSymbols.Void)
        {
            return true;
        }

        return type == TypeSymbols.Int ||
               type == TypeSymbols.Bool ||
               type == TypeSymbols.String ||
               type is ArrayTypeSymbol { ElementType: IntTypeSymbol } ||
               type is FunctionTypeSymbol functionType &&
               functionType.ParameterTypes.All(IsSupportedRuntimeType) &&
               IsSupportedRuntimeType(functionType.ReturnType);
    }

    private static bool TypeEquals(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is ArrayTypeSymbol leftArray && right is ArrayTypeSymbol rightArray)
        {
            return TypeEquals(leftArray.ElementType, rightArray.ElementType);
        }

        if (left is FunctionTypeSymbol leftFunction && right is FunctionTypeSymbol rightFunction)
        {
            if (!TypeEquals(leftFunction.ReturnType, rightFunction.ReturnType) ||
                leftFunction.ParameterTypes.Count != rightFunction.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftFunction.ParameterTypes.Count; i++)
            {
                if (!TypeEquals(leftFunction.ParameterTypes[i], rightFunction.ParameterTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return left == right;
    }

    private static bool TryMapBinaryOperator(string op, out IrBinaryOperator irOp)
    {
        irOp = op switch
        {
            "+" => IrBinaryOperator.Add,
            "-" => IrBinaryOperator.Subtract,
            "*" => IrBinaryOperator.Multiply,
            "/" => IrBinaryOperator.Divide,
            "<" => IrBinaryOperator.LessThan,
            ">" => IrBinaryOperator.GreaterThan,
            "==" => IrBinaryOperator.Equal,
            "!=" => IrBinaryOperator.NotEqual,
            _ => default,
        };

        return op is "+" or "-" or "*" or "/" or "<" or ">" or "==" or "!=";
    }
}
