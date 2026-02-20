namespace Kong;

public class TypeCheckResult
{
    public Dictionary<IExpression, TypeSymbol> ExpressionTypes { get; } = [];
    public Dictionary<LetStatement, TypeSymbol> VariableTypes { get; } = [];
    public Dictionary<FunctionLiteral, FunctionTypeSymbol> FunctionTypes { get; } = [];
    public Dictionary<FunctionDeclaration, FunctionTypeSymbol> DeclaredFunctionTypes { get; } = [];
    public DiagnosticBag Diagnostics { get; } = new();
}

public class TypeChecker
{
    private readonly TypeCheckResult _result = new();
    private readonly Dictionary<NameSymbol, TypeSymbol> _symbolTypes = [];
    private readonly Stack<TypeSymbol> _currentFunctionReturnTypes = [];
    private readonly BuiltinRegistry _builtinRegistry = BuiltinRegistry.Default;
    private NameResolution _names = null!;
    private readonly ControlFlowAnalyzer _controlFlowAnalyzer = new();

    public TypeCheckResult Check(CompilationUnit unit, NameResolution names)
    {
        _names = names;
        _result.Diagnostics.AddRange(names.Diagnostics);
        PredeclareFunctionDeclarations(unit);

        foreach (var statement in unit.Statements)
        {
            CheckStatement(statement);
        }

        return _result;
    }

    private void PredeclareFunctionDeclarations(CompilationUnit unit)
    {
        foreach (var statement in unit.Statements)
        {
            if (statement is not FunctionDeclaration declaration)
            {
                continue;
            }

            var parameterTypes = new List<TypeSymbol>(declaration.Parameters.Count);
            foreach (var parameter in declaration.Parameters)
            {
                if (parameter.TypeAnnotation == null)
                {
                    _result.Diagnostics.Report(parameter.Span,
                        $"missing type annotation for parameter '{parameter.Name}'",
                        "T105");
                    parameterTypes.Add(TypeSymbols.Error);
                    continue;
                }

                parameterTypes.Add(TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error);
            }

            var returnType = declaration.ReturnTypeAnnotation == null
                ? TypeSymbols.Void
                : TypeAnnotationBinder.Bind(declaration.ReturnTypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error;

            var functionType = new FunctionTypeSymbol(parameterTypes, returnType);
            _result.DeclaredFunctionTypes[declaration] = functionType;

            if (_names.IdentifierSymbols.TryGetValue(declaration.Name, out var symbol))
            {
                _symbolTypes[symbol] = functionType;
            }
        }
    }

    private void CheckStatement(IStatement statement)
    {
        switch (statement)
        {
            case LetStatement letStatement:
                CheckLetStatement(letStatement);
                break;
            case FunctionDeclaration functionDeclaration:
                CheckFunctionDeclaration(functionDeclaration);
                break;
            case ReturnStatement returnStatement:
                CheckReturnStatement(returnStatement);
                break;
            case ExpressionStatement expressionStatement:
                if (expressionStatement.Expression != null)
                {
                    CheckExpression(expressionStatement.Expression);
                }
                break;
            case BlockStatement blockStatement:
                CheckBlockStatement(blockStatement);
                break;
        }
    }

    private void CheckFunctionDeclaration(FunctionDeclaration declaration)
    {
        var functionLiteral = declaration.ToFunctionLiteral();
        if (!_result.DeclaredFunctionTypes.TryGetValue(declaration, out var functionType))
        {
            var symbol = _names.IdentifierSymbols.TryGetValue(declaration.Name, out var resolvedSymbol)
                ? resolvedSymbol
                : (NameSymbol?)null;
            functionType = CheckFunctionLiteral(functionLiteral, symbol);
            _result.DeclaredFunctionTypes[declaration] = functionType;
            return;
        }

        _result.FunctionTypes[functionLiteral] = functionType;
        CheckFunctionBody(functionLiteral, functionType, functionType.ParameterTypes);
    }

    private void CheckLetStatement(LetStatement statement)
    {
        var diagnosticsBeforeInitializer = _result.Diagnostics.Count;
        var initializerType = statement.Value != null
            ? CheckExpression(statement.Value)
            : TypeSymbols.Error;
        var initializerHadErrors = _result.Diagnostics.Count > diagnosticsBeforeInitializer;

        TypeSymbol declaredType;
        if (statement.TypeAnnotation == null)
        {
            declaredType = InferLetType(statement, initializerType, initializerHadErrors);
        }
        else
        {
            declaredType = TypeAnnotationBinder.Bind(statement.TypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error;
            if (!IsAssignable(initializerType, declaredType))
            {
                _result.Diagnostics.Report(statement.Span,
                    $"cannot assign expression of type '{initializerType}' to variable '{statement.Name.Value}' of type '{declaredType}'",
                    "T102");
            }
        }

        _result.VariableTypes[statement] = declaredType;

        if (_names.IdentifierSymbols.TryGetValue(statement.Name, out var symbol))
        {
            _symbolTypes[symbol] = declaredType;
        }
    }

    private TypeSymbol InferLetType(LetStatement statement, TypeSymbol initializerType, bool initializerHadErrors)
    {
        if (initializerHadErrors || initializerType == TypeSymbols.Error)
        {
            return TypeSymbols.Error;
        }

        if (initializerType == TypeSymbols.Null)
        {
            _result.Diagnostics.Report(
                statement.Name.Span,
                $"cannot infer type for '{statement.Name.Value}' from null initializer",
                "T119");
            return TypeSymbols.Error;
        }

        if (initializerType is ArrayTypeSymbol { ElementType: ErrorTypeSymbol })
        {
            _result.Diagnostics.Report(
                statement.Name.Span,
                $"cannot infer element type for '{statement.Name.Value}' from empty array initializer",
                "T120");
            return TypeSymbols.Error;
        }

        return initializerType;
    }

    private void CheckReturnStatement(ReturnStatement statement)
    {
        if (_currentFunctionReturnTypes.Count == 0)
        {
            _result.Diagnostics.Report(statement.Span, "return statement outside of function", "T107");
            return;
        }

        var expectedReturnType = _currentFunctionReturnTypes.Peek();
        var actualReturnType = statement.ReturnValue != null
            ? CheckExpression(statement.ReturnValue)
            : TypeSymbols.Void;

        if (!IsAssignable(actualReturnType, expectedReturnType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"return type mismatch: expected '{expectedReturnType}', got '{actualReturnType}'",
                "T104");
        }
    }

    private void CheckBlockStatement(BlockStatement blockStatement)
    {
        foreach (var statement in blockStatement.Statements)
        {
            CheckStatement(statement);
        }
    }

    private TypeSymbol CheckExpression(IExpression expression)
    {
        var type = expression switch
        {
            IntegerLiteral => TypeSymbols.Int,
            StringLiteral => TypeSymbols.String,
            BooleanLiteral => TypeSymbols.Bool,
            Identifier identifier => CheckIdentifier(identifier),
            PrefixExpression prefixExpression => CheckPrefixExpression(prefixExpression),
            InfixExpression infixExpression => CheckInfixExpression(infixExpression),
            IfExpression ifExpression => CheckIfExpression(ifExpression),
            FunctionLiteral functionLiteral => CheckFunctionLiteral(functionLiteral),
            CallExpression callExpression => CheckCallExpression(callExpression),
            ArrayLiteral arrayLiteral => CheckArrayLiteral(arrayLiteral),
            IndexExpression indexExpression => CheckIndexExpression(indexExpression),
            _ => TypeSymbols.Error,
        };

        _result.ExpressionTypes[expression] = type;
        return type;
    }

    private TypeSymbol CheckIdentifier(Identifier identifier)
    {
        if (!_names.IdentifierSymbols.TryGetValue(identifier, out var symbol))
        {
            return TypeSymbols.Error;
        }

        if (_symbolTypes.TryGetValue(symbol, out var declarationType))
        {
            return declarationType;
        }

        if (TryGetBuiltinType(symbol, out var builtinType))
        {
            _symbolTypes[symbol] = builtinType;
            return builtinType;
        }

        _result.Diagnostics.Report(identifier.Span,
            $"symbol '{identifier.Value}' is resolved but has no known type",
            "T108");
        return TypeSymbols.Error;
    }

    private TypeSymbol CheckPrefixExpression(PrefixExpression expression)
    {
        var right = CheckExpression(expression.Right);

        return expression.Operator switch
        {
            "!" when right == TypeSymbols.Bool => TypeSymbols.Bool,
            "-" when right == TypeSymbols.Int => TypeSymbols.Int,
            "!" => ReportPrefixTypeError(expression, right, "bool"),
            "-" => ReportPrefixTypeError(expression, right, "int"),
            _ => ReportUnknownOperator(expression, expression.Operator),
        };
    }

    private TypeSymbol CheckInfixExpression(InfixExpression expression)
    {
        var left = CheckExpression(expression.Left);
        var right = CheckExpression(expression.Right);

        return expression.Operator switch
        {
            "+" or "-" or "*" or "/" =>
                RequireBothInt(expression, left, right, returnType: TypeSymbols.Int),
            "<" or ">" =>
                RequireBothInt(expression, left, right, returnType: TypeSymbols.Bool),
            "==" or "!=" =>
                RequireComparable(expression, left, right),
            _ => ReportUnknownOperator(expression, expression.Operator),
        };
    }

    private TypeSymbol CheckIfExpression(IfExpression expression)
    {
        var conditionType = CheckExpression(expression.Condition);
        if (conditionType != TypeSymbols.Bool && conditionType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Condition.Span,
                $"if condition must be 'bool', got '{conditionType}'",
                "T109");
        }

        var consequenceType = CheckBlockExpressionType(expression.Consequence);
        if (expression.Alternative == null)
        {
            return TypeSymbols.Null;
        }

        var alternativeType = CheckBlockExpressionType(expression.Alternative);
        if (AreCompatible(consequenceType, alternativeType))
        {
            return consequenceType;
        }

        _result.Diagnostics.Report(expression.Span,
            $"if branches must have matching types, got '{consequenceType}' and '{alternativeType}'",
            "T110");
        return TypeSymbols.Error;
    }

    private TypeSymbol CheckBlockExpressionType(BlockStatement block)
    {
        TypeSymbol? lastType = null;

        foreach (var statement in block.Statements)
        {
            CheckStatement(statement);

            if (statement is ExpressionStatement { Expression: not null } expressionStatement)
            {
                lastType = _result.ExpressionTypes.GetValueOrDefault(expressionStatement.Expression, TypeSymbols.Error);
            }
        }

        return lastType ?? TypeSymbols.Void;
    }

    private FunctionTypeSymbol CheckFunctionLiteral(FunctionLiteral functionLiteral, NameSymbol? declaredSymbol = null)
    {
        var parameterTypes = new List<TypeSymbol>(functionLiteral.Parameters.Count);
        foreach (var parameter in functionLiteral.Parameters)
        {
            var parameterType = CheckFunctionParameter(parameter);
            parameterTypes.Add(parameterType);
        }

        TypeSymbol returnType;
        if (functionLiteral.ReturnTypeAnnotation == null)
        {
            _result.Diagnostics.Report(functionLiteral.Span,
                "missing return type annotation for function",
                "T106");
            returnType = TypeSymbols.Error;
        }
        else
        {
            returnType = TypeAnnotationBinder.Bind(functionLiteral.ReturnTypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error;
        }

        var functionType = new FunctionTypeSymbol(parameterTypes, returnType);
        _result.FunctionTypes[functionLiteral] = functionType;

        if (declaredSymbol != null)
        {
            _symbolTypes[declaredSymbol.Value] = functionType;
        }

        CheckFunctionBody(functionLiteral, functionType, parameterTypes);

        return functionType;
    }

    private void CheckFunctionBody(FunctionLiteral functionLiteral, FunctionTypeSymbol functionType, IReadOnlyList<TypeSymbol> parameterTypes)
    {
        for (var i = 0; i < functionLiteral.Parameters.Count && i < parameterTypes.Count; i++)
        {
            var parameter = functionLiteral.Parameters[i];
            if (_names.ParameterSymbols.TryGetValue(parameter, out var parameterSymbol))
            {
                _symbolTypes[parameterSymbol] = parameterTypes[i];
            }
        }

        _currentFunctionReturnTypes.Push(functionType.ReturnType);
        CheckBlockStatement(functionLiteral.Body);
        _currentFunctionReturnTypes.Pop();

        _controlFlowAnalyzer.AnalyzeFunction(
            functionLiteral,
            functionType.ReturnType,
            _result.ExpressionTypes,
            _result.Diagnostics);
    }

    private TypeSymbol CheckFunctionParameter(FunctionParameter parameter)
    {
        if (parameter.TypeAnnotation == null)
        {
            _result.Diagnostics.Report(parameter.Span,
                $"missing type annotation for parameter '{parameter.Name}'",
                "T105");
            return TypeSymbols.Error;
        }

        return TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error;
    }

    private TypeSymbol CheckCallExpression(CallExpression expression)
    {
        var argumentTypes = expression.Arguments.Select(CheckExpression).ToList();

        if (TryCheckBuiltinCall(expression, argumentTypes, out var builtinReturnType))
        {
            return builtinReturnType;
        }

        var functionType = CheckExpression(expression.Function);

        if (functionType is not FunctionTypeSymbol fn)
        {
            _result.Diagnostics.Report(expression.Span,
                $"cannot call expression of type '{functionType}'",
                "T111");
            return TypeSymbols.Error;
        }

        if (argumentTypes.Count != fn.ParameterTypes.Count)
        {
            _result.Diagnostics.Report(expression.Span,
                $"wrong number of arguments: expected {fn.ParameterTypes.Count}, got {argumentTypes.Count}",
                "T112");
            return fn.ReturnType;
        }

        for (var i = 0; i < argumentTypes.Count; i++)
        {
            if (!IsAssignable(argumentTypes[i], fn.ParameterTypes[i]))
            {
                _result.Diagnostics.Report(expression.Arguments[i].Span,
                    $"argument {i + 1} type mismatch: expected '{fn.ParameterTypes[i]}', got '{argumentTypes[i]}'",
                    "T113");
            }
        }

        return fn.ReturnType;
    }

    private bool TryCheckBuiltinCall(CallExpression expression, IReadOnlyList<TypeSymbol> argumentTypes, out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (expression.Function is not Identifier identifier)
        {
            return false;
        }

        if (!_names.IdentifierSymbols.TryGetValue(identifier, out var symbol) || symbol.Kind != NameSymbolKind.Builtin)
        {
            return false;
        }

        var binding = _builtinRegistry.ResolveByTypeSignature(identifier.Value, argumentTypes);
        if (binding == null)
        {
            var overloads = _builtinRegistry.GetBindingsForName(identifier.Value).Select(b => b.Signature.ToString());
            var expected = string.Join("; ", overloads);
            if (string.IsNullOrEmpty(expected))
            {
                expected = "<none>";
            }

            _result.Diagnostics.Report(
                expression.Span,
                $"no matching overload for builtin '{identifier.Value}' with argument types ({string.Join(", ", argumentTypes)}); expected one of: {expected}",
                "T113");
            returnType = TypeSymbols.Error;
            _result.ExpressionTypes[identifier] = TypeSymbols.Error;
            return true;
        }

        var functionType = new FunctionTypeSymbol(binding.Signature.ParameterTypes, binding.Signature.ReturnType);
        _result.ExpressionTypes[identifier] = functionType;
        returnType = binding.Signature.ReturnType;
        return true;
    }

    private bool TryGetBuiltinType(NameSymbol symbol, out TypeSymbol type)
    {
        if (symbol.Kind != NameSymbolKind.Builtin)
        {
            type = TypeSymbols.Error;
            return false;
        }

        var overloads = _builtinRegistry.GetBindingsForName(symbol.Name).ToList();
        if (overloads.Count == 0)
        {
            type = TypeSymbols.Error;
            return false;
        }

        if (overloads.Count == 1)
        {
            var signature = overloads[0].Signature;
            type = new FunctionTypeSymbol(signature.ParameterTypes, signature.ReturnType);
            return true;
        }

        type = TypeSymbols.Error;
        return false;
    }

    private TypeSymbol CheckArrayLiteral(ArrayLiteral expression)
    {
        if (expression.Elements.Count == 0)
        {
            return new ArrayTypeSymbol(TypeSymbols.Error);
        }

        var elementType = CheckExpression(expression.Elements[0]);
        for (var i = 1; i < expression.Elements.Count; i++)
        {
            var current = CheckExpression(expression.Elements[i]);
            if (!AreCompatible(elementType, current))
            {
                _result.Diagnostics.Report(expression.Elements[i].Span,
                    $"array elements must have matching types, expected '{elementType}', got '{current}'",
                    "T114");
                return new ArrayTypeSymbol(TypeSymbols.Error);
            }
        }

        return new ArrayTypeSymbol(elementType);
    }

    private TypeSymbol CheckIndexExpression(IndexExpression expression)
    {
        var left = CheckExpression(expression.Left);
        var index = CheckExpression(expression.Index);

        if (index != TypeSymbols.Int && index != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Index.Span,
                $"array index must be 'int', got '{index}'",
                "T115");
        }

        if (left is ArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (left != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Left.Span,
                $"cannot index expression of type '{left}'",
                "T116");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireBothInt(InfixExpression expression, TypeSymbol left, TypeSymbol right, TypeSymbol returnType)
    {
        if (left == TypeSymbols.Int && right == TypeSymbols.Int)
        {
            return returnType;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires 'int' operands, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireComparable(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (AreCompatible(left, right))
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires compatible operand types, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol ReportPrefixTypeError(PrefixExpression expression, TypeSymbol actualType, string expectedTypeName)
    {
        if (actualType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires '{expectedTypeName}', got '{actualType}'",
                "T103");
        }
        return TypeSymbols.Error;
    }

    private TypeSymbol ReportUnknownOperator(IExpression expression, string op)
    {
        _result.Diagnostics.Report(expression.Span, $"unknown operator '{op}'", "T103");
        return TypeSymbols.Error;
    }

    private static bool IsAssignable(TypeSymbol source, TypeSymbol target)
    {
        if (source == TypeSymbols.Error || target == TypeSymbols.Error)
        {
            return true;
        }

        return TypeEquals(source, target);
    }

    private static bool AreCompatible(TypeSymbol left, TypeSymbol right)
    {
        if (left == TypeSymbols.Error || right == TypeSymbols.Error)
        {
            return true;
        }

        return TypeEquals(left, right);
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
            if (!TypeEquals(leftFunction.ReturnType, rightFunction.ReturnType))
            {
                return false;
            }

            if (leftFunction.ParameterTypes.Count != rightFunction.ParameterTypes.Count)
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
}
