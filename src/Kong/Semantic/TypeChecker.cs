using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public class TypeCheckResult
{
    public Dictionary<IExpression, TypeSymbol> ExpressionTypes { get; } = [];
    public Dictionary<CallExpression, string> ResolvedStaticMethodPaths { get; } = [];
    public Dictionary<MemberAccessExpression, string> ResolvedStaticValuePaths { get; } = [];
    public Dictionary<CallExpression, string> ResolvedInstanceMethodMembers { get; } = [];
    public Dictionary<MemberAccessExpression, string> ResolvedInstanceValueMembers { get; } = [];
    public Dictionary<NewExpression, string> ResolvedConstructorTypePaths { get; } = [];
    public Dictionary<LetStatement, TypeSymbol> VariableTypes { get; } = [];
    public Dictionary<FunctionLiteral, FunctionTypeSymbol> FunctionTypes { get; } = [];
    public Dictionary<FunctionDeclaration, FunctionTypeSymbol> DeclaredFunctionTypes { get; } = [];
    public Dictionary<string, EnumDefinitionSymbol> EnumDefinitions { get; } = new(StringComparer.Ordinal);
    public Dictionary<CallExpression, EnumVariantConstruction> ResolvedEnumVariantConstructions { get; } = [];
    public Dictionary<MatchExpression, MatchResolution> ResolvedMatches { get; } = [];
    public DiagnosticBag Diagnostics { get; } = new();
}

public sealed record EnumVariantConstruction(
    string EnumName,
    string VariantName,
    int Tag,
    int MaxPayloadArity,
    IReadOnlyList<TypeSymbol> PayloadTypes);

public sealed record MatchArmResolution(
    string VariantName,
    int Tag,
    IReadOnlyList<TypeSymbol> PayloadTypes,
    IReadOnlyList<NameSymbol> BindingSymbols);

public sealed record MatchResolution(
    string EnumName,
    IReadOnlyList<MatchArmResolution> Arms);

public class TypeChecker
{
    private readonly TypeCheckResult _result = new();
    private readonly Dictionary<NameSymbol, TypeSymbol> _symbolTypes = [];
    private readonly Stack<TypeSymbol> _currentFunctionReturnTypes = [];
    private int _loopDepth;
    private NameResolution _names = null!;
    private IReadOnlyDictionary<string, FunctionTypeSymbol> _externalFunctionTypes =
        new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal);
    private readonly ControlFlowAnalyzer _controlFlowAnalyzer = new();
    private readonly List<EnumDeclaration> _externalEnumDeclarations = [];
    private readonly Dictionary<string, EnumTypeSymbol> _enumTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (EnumDefinitionSymbol Enum, EnumVariantDefinition Variant)> _variantSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeSymbol> _namedTypeSymbols = new(StringComparer.Ordinal);

    public TypeCheckResult Check(CompilationUnit unit, NameResolution names)
    {
        return Check(
            unit,
            names,
            new Dictionary<string, FunctionTypeSymbol>(StringComparer.Ordinal),
            []);
    }

    public TypeCheckResult Check(
        CompilationUnit unit,
        NameResolution names,
        IReadOnlyDictionary<string, FunctionTypeSymbol> externalFunctionTypes,
        IReadOnlyList<EnumDeclaration> externalEnumDeclarations)
    {
        _names = names;
        _externalFunctionTypes = externalFunctionTypes;
        _externalEnumDeclarations.Clear();
        _enumTypes.Clear();
        _variantSymbols.Clear();
        _namedTypeSymbols.Clear();
        _result.EnumDefinitions.Clear();
        _result.ResolvedEnumVariantConstructions.Clear();
        _result.ResolvedMatches.Clear();
        foreach (var declaration in externalEnumDeclarations)
        {
            _externalEnumDeclarations.Add(declaration);
        }

        _result.Diagnostics.AddRange(names.Diagnostics);
        PredeclareEnumDeclarations(unit);
        PredeclareFunctionDeclarations(unit);

        foreach (var statement in unit.Statements)
        {
            CheckStatement(statement);
        }

        return _result;
    }

    private void PredeclareEnumDeclarations(CompilationUnit unit)
    {
        foreach (var external in _externalEnumDeclarations)
        {
            _enumTypes[external.Name.Value] = new EnumTypeSymbol(external.Name.Value, []);
            _namedTypeSymbols[external.Name.Value] = _enumTypes[external.Name.Value];
        }

        var allDeclarations = _externalEnumDeclarations.Concat(unit.Statements.OfType<EnumDeclaration>()).ToList();
        foreach (var declaration in allDeclarations)
        {
            if (_enumTypes.ContainsKey(declaration.Name.Value))
            {
                if (_externalEnumDeclarations.Contains(declaration))
                {
                    continue;
                }

                _result.Diagnostics.Report(declaration.Name.Span,
                    $"duplicate enum declaration '{declaration.Name.Value}'",
                    "T128");
                continue;
            }

            if (declaration.TypeParameters.Count > 0)
            {
                _result.Diagnostics.Report(declaration.Name.Span,
                    $"generic enum declarations are not implemented yet for '{declaration.Name.Value}'",
                    "T129");
            }

            _enumTypes[declaration.Name.Value] = new EnumTypeSymbol(declaration.Name.Value, []);
            _namedTypeSymbols[declaration.Name.Value] = _enumTypes[declaration.Name.Value];
        }

        foreach (var declaration in allDeclarations)
        {
            if (!_enumTypes.TryGetValue(declaration.Name.Value, out _))
            {
                continue;
            }

            var variants = new List<EnumVariantDefinition>(declaration.Variants.Count);
            var seenVariants = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < declaration.Variants.Count; i++)
            {
                var variant = declaration.Variants[i];
                if (!seenVariants.Add(variant.Name.Value))
                {
                    _result.Diagnostics.Report(variant.Name.Span,
                        $"duplicate enum variant '{variant.Name.Value}' in enum '{declaration.Name.Value}'",
                        "T128");
                    continue;
                }

                var payloadTypes = new List<TypeSymbol>(variant.PayloadTypes.Count);
                foreach (var payloadTypeNode in variant.PayloadTypes)
                {
                    payloadTypes.Add(TypeAnnotationBinder.Bind(payloadTypeNode, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error);
                }

                variants.Add(new EnumVariantDefinition(variant.Name.Value, payloadTypes, i));
            }

            var enumDefinition = new EnumDefinitionSymbol(declaration.Name.Value, [], variants);
            _result.EnumDefinitions[declaration.Name.Value] = enumDefinition;
            RegisterVariantSymbols(enumDefinition);
        }
    }

    private void RegisterVariantSymbols(EnumDefinitionSymbol enumDefinition)
    {
        foreach (var variant in enumDefinition.Variants)
        {
            _variantSymbols[variant.Name] = (enumDefinition, variant);
        }
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

                parameterTypes.Add(TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error);
            }

            var returnType = declaration.ReturnTypeAnnotation == null
                ? TypeSymbols.Void
                : TypeAnnotationBinder.Bind(declaration.ReturnTypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;

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
            case AssignmentStatement assignmentStatement:
                CheckAssignmentStatement(assignmentStatement);
                break;
            case IndexAssignmentStatement indexAssignmentStatement:
                CheckIndexAssignmentStatement(indexAssignmentStatement);
                break;
            case ForInStatement forInStatement:
                CheckForInStatement(forInStatement);
                break;
            case BreakStatement breakStatement:
                CheckBreakStatement(breakStatement);
                break;
            case ContinueStatement continueStatement:
                CheckContinueStatement(continueStatement);
                break;
            case FunctionDeclaration functionDeclaration:
                CheckFunctionDeclaration(functionDeclaration);
                break;
            case EnumDeclaration:
                break;
            case ReturnStatement returnStatement:
                CheckReturnStatement(returnStatement);
                break;
            case ImportStatement:
            case NamespaceStatement:
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
            declaredType = TypeAnnotationBinder.Bind(statement.TypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
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

    private void CheckAssignmentStatement(AssignmentStatement statement)
    {
        if (!_names.IdentifierSymbols.TryGetValue(statement.Name, out var symbol))
        {
            return;
        }

        if (!_names.MutableSymbols.Contains(symbol))
        {
            _result.Diagnostics.Report(statement.Span,
                $"cannot assign to immutable variable '{statement.Name.Value}'; declare with 'var' to allow reassignment",
                "T123");
        }

        if (!_symbolTypes.TryGetValue(symbol, out var targetType))
        {
            _result.Diagnostics.Report(statement.Span,
                $"cannot resolve type for assignment target '{statement.Name.Value}'",
                "T124");
            return;
        }

        var valueType = CheckExpression(statement.Value);
        if (!IsAssignable(valueType, targetType))
        {
            _result.Diagnostics.Report(statement.Value.Span,
                $"cannot assign expression of type '{valueType}' to variable '{statement.Name.Value}' of type '{targetType}'",
                "T102");
        }
    }

    private void CheckForInStatement(ForInStatement statement)
    {
        var iterableType = CheckExpression(statement.Iterable);
        var elementType = iterableType is ArrayTypeSymbol arrayType
            ? arrayType.ElementType
            : TypeSymbols.Error;

        if (iterableType is not ArrayTypeSymbol && iterableType != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(statement.Iterable.Span,
                $"for-in iterable must be an array, got '{iterableType}'",
                "T125");
        }

        if (_names.IdentifierSymbols.TryGetValue(statement.Iterator, out var symbol))
        {
            _symbolTypes[symbol] = elementType;
        }

        _loopDepth++;
        CheckBlockStatement(statement.Body);
        _loopDepth--;
    }

    private void CheckIndexAssignmentStatement(IndexAssignmentStatement statement)
    {
        var left = CheckExpression(statement.Target.Left);
        var index = CheckExpression(statement.Target.Index);
        var valueType = CheckExpression(statement.Value);

        if (index != TypeSymbols.Int && index != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(statement.Target.Index.Span,
                $"array index must be 'int', got '{index}'",
                "T115");
        }

        if (left is not ArrayTypeSymbol arrayType)
        {
            if (left != TypeSymbols.Error)
            {
                _result.Diagnostics.Report(statement.Target.Left.Span,
                    $"cannot index-assign expression of type '{left}'",
                    "T116");
            }

            return;
        }

        if (!IsAssignable(valueType, arrayType.ElementType))
        {
            _result.Diagnostics.Report(statement.Value.Span,
                $"cannot assign expression of type '{valueType}' to array element of type '{arrayType.ElementType}'",
                "T102");
        }
    }

    private void CheckBreakStatement(BreakStatement statement)
    {
        if (_loopDepth == 0)
        {
            _result.Diagnostics.Report(statement.Span,
                "'break' can only be used inside a loop",
                "T126");
        }
    }

    private void CheckContinueStatement(ContinueStatement statement)
    {
        if (_loopDepth == 0)
        {
            _result.Diagnostics.Report(statement.Span,
                "'continue' can only be used inside a loop",
                "T127");
        }
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
            IntegerLiteral integerLiteral => CheckIntegerLiteral(integerLiteral),
            DoubleLiteral => TypeSymbols.Double,
            CharLiteral => TypeSymbols.Char,
            ByteLiteral => TypeSymbols.Byte,
            StringLiteral => TypeSymbols.String,
            BooleanLiteral => TypeSymbols.Bool,
            Identifier identifier => CheckIdentifier(identifier),
            PrefixExpression prefixExpression => CheckPrefixExpression(prefixExpression),
            InfixExpression infixExpression => CheckInfixExpression(infixExpression),
            IfExpression ifExpression => CheckIfExpression(ifExpression),
            MatchExpression matchExpression => CheckMatchExpression(matchExpression),
            FunctionLiteral functionLiteral => CheckFunctionLiteral(functionLiteral),
            CallExpression callExpression => CheckCallExpression(callExpression),
            MemberAccessExpression memberAccessExpression => CheckMemberAccessExpression(memberAccessExpression),
            NewExpression newExpression => CheckNewExpression(newExpression),
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

        if (symbol.Kind == NameSymbolKind.EnumVariant)
        {
            _result.Diagnostics.Report(identifier.Span,
                $"enum variant '{identifier.Value}' must be called like '{identifier.Value}(...)'",
                "T128");
            return TypeSymbols.Error;
        }

        if (_symbolTypes.TryGetValue(symbol, out var declarationType))
        {
            return declarationType;
        }

        if (symbol.Kind == NameSymbolKind.Global &&
            _externalFunctionTypes.TryGetValue(symbol.Name, out var externalFunctionType))
        {
            _symbolTypes[symbol] = externalFunctionType;
            return externalFunctionType;
        }

        _result.Diagnostics.Report(identifier.Span,
            $"symbol '{identifier.Value}' is resolved but has no known type",
            "T108");
        return TypeSymbols.Error;
    }

    private TypeSymbol CheckIntegerLiteral(IntegerLiteral literal)
    {
        if (literal.Value is < int.MinValue or > int.MaxValue)
        {
            _result.Diagnostics.Report(literal.Span,
                $"integer literal '{literal.Value}' is out of range for type 'int'",
                "T121");
            return TypeSymbols.Error;
        }

        return TypeSymbols.Int;
    }

    private TypeSymbol CheckPrefixExpression(PrefixExpression expression)
    {
        var right = CheckExpression(expression.Right);

        return expression.Operator switch
        {
            "!" when right == TypeSymbols.Bool => TypeSymbols.Bool,
            "-" when TryGetUnaryNegationResultType(right, out var resultType) => resultType,
            "!" => ReportPrefixTypeError(expression, right, "bool"),
            "-" => ReportPrefixTypeError(expression, right, "numeric type"),
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
                RequireNumericOperands(expression, left, right),
            "&&" or "||" =>
                RequireBothBool(expression, left, right),
            "<" or ">" =>
                RequireComparableNumericTypes(expression, left, right),
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

    private TypeSymbol CheckMatchExpression(MatchExpression expression)
    {
        var targetType = CheckExpression(expression.Target);
        if (targetType is not EnumTypeSymbol enumType)
        {
            if (targetType != TypeSymbols.Error)
            {
                _result.Diagnostics.Report(expression.Target.Span,
                    $"match target must be an enum value, got '{targetType}'",
                    "T130");
            }

            return TypeSymbols.Error;
        }

        if (!_result.EnumDefinitions.TryGetValue(enumType.EnumName, out var enumDefinition))
        {
            _result.Diagnostics.Report(expression.Span,
                $"unknown enum type '{enumType.EnumName}' in match expression",
                "T130");
            return TypeSymbols.Error;
        }

        if (expression.Arms.Count == 0)
        {
            _result.Diagnostics.Report(expression.Span,
                $"match expression for enum '{enumDefinition.Name}' must declare at least one arm",
                "T131");
            return TypeSymbols.Error;
        }

        var seenVariants = new HashSet<string>(StringComparer.Ordinal);
        var resolvedArms = new List<MatchArmResolution>(expression.Arms.Count);
        var armTypes = new List<TypeSymbol>(expression.Arms.Count);

        foreach (var arm in expression.Arms)
        {
            var variant = enumDefinition.Variants.FirstOrDefault(v => string.Equals(v.Name, arm.VariantName.Value, StringComparison.Ordinal));
            if (variant == null)
            {
                _result.Diagnostics.Report(arm.VariantName.Span,
                    $"unknown enum variant '{arm.VariantName.Value}' for enum '{enumDefinition.Name}'",
                    "T130");
                continue;
            }

            if (!seenVariants.Add(variant.Name))
            {
                _result.Diagnostics.Report(arm.VariantName.Span,
                    $"duplicate match arm for enum variant '{variant.Name}'",
                    "T131");
                continue;
            }

            if (arm.Bindings.Count != variant.PayloadTypes.Count)
            {
                _result.Diagnostics.Report(arm.Span,
                    $"match arm '{variant.Name}' expects {variant.PayloadTypes.Count} binding(s), got {arm.Bindings.Count}",
                    "T131");
                continue;
            }

            var bindingSymbols = new List<NameSymbol>(arm.Bindings.Count);
            for (var i = 0; i < arm.Bindings.Count; i++)
            {
                var binding = arm.Bindings[i];
                if (_names.IdentifierSymbols.TryGetValue(binding, out var bindingSymbol))
                {
                    _symbolTypes[bindingSymbol] = variant.PayloadTypes[i];
                    bindingSymbols.Add(bindingSymbol);
                }
            }

            var armType = CheckBlockExpressionType(arm.Body);
            armTypes.Add(armType);
            resolvedArms.Add(new MatchArmResolution(variant.Name, variant.Tag, variant.PayloadTypes, bindingSymbols));
        }

        if (seenVariants.Count != enumDefinition.Variants.Count)
        {
            _result.Diagnostics.Report(expression.Span,
                $"match expression for enum '{enumDefinition.Name}' is not exhaustive",
                "T131");
        }

        if (armTypes.Count == 0)
        {
            return TypeSymbols.Error;
        }

        var resultType = armTypes[0];
        for (var i = 1; i < armTypes.Count; i++)
        {
            if (!AreCompatible(resultType, armTypes[i]))
            {
                _result.Diagnostics.Report(expression.Span,
                    $"match arms must have matching types, got '{resultType}' and '{armTypes[i]}'",
                    "T110");
                return TypeSymbols.Error;
            }
        }

        _result.ResolvedMatches[expression] = new MatchResolution(enumDefinition.Name, resolvedArms);
        return resultType;
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
            returnType = TypeAnnotationBinder.Bind(functionLiteral.ReturnTypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
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

        return TypeAnnotationBinder.Bind(parameter.TypeAnnotation, _result.Diagnostics, _namedTypeSymbols) ?? TypeSymbols.Error;
    }

    private TypeSymbol CheckCallExpression(CallExpression expression)
    {
        var clrArguments = BuildClrCallArguments(expression);
        var argumentTypes = clrArguments.Select(a => a.Type).ToList();

        if (expression.Function is MemberAccessExpression memberAccessExpression &&
            TryCheckInstanceMethodCall(expression, memberAccessExpression, clrArguments, out var instanceReturnType))
        {
            return instanceReturnType;
        }

        if (expression.Function is MemberAccessExpression memberAccessExpression2 &&
            TryCheckStaticMethodCall(expression, memberAccessExpression2, clrArguments, out var staticReturnType))
        {
            return staticReturnType;
        }

        if (expression.Arguments.Any(a => a.Modifier != CallArgumentModifier.None))
        {
            _result.Diagnostics.Report(expression.Span,
                "out/ref modifiers are supported only for CLR interop method calls",
                "T122");
        }

        if (expression.Function is Identifier identifier &&
            _names.IdentifierSymbols.TryGetValue(identifier, out var symbol) &&
            symbol.Kind == NameSymbolKind.EnumVariant &&
            _variantSymbols.TryGetValue(symbol.Name, out var variantBinding))
        {
            var expectedPayloadTypes = variantBinding.Variant.PayloadTypes;
            if (argumentTypes.Count != expectedPayloadTypes.Count)
            {
                _result.Diagnostics.Report(
                    expression.Span,
                    $"wrong number of arguments for enum variant '{variantBinding.Variant.Name}': expected {expectedPayloadTypes.Count}, got {argumentTypes.Count}",
                    "T112");
                return new EnumTypeSymbol(variantBinding.Enum.Name, []);
            }

            for (var i = 0; i < expectedPayloadTypes.Count; i++)
            {
                if (!IsAssignable(argumentTypes[i], expectedPayloadTypes[i]))
                {
                    _result.Diagnostics.Report(
                        expression.Arguments[i].Span,
                        $"argument {i + 1} type mismatch for enum variant '{variantBinding.Variant.Name}': expected '{expectedPayloadTypes[i]}', got '{argumentTypes[i]}'",
                        "T113");
                }
            }

            _result.ResolvedEnumVariantConstructions[expression] = new EnumVariantConstruction(
                variantBinding.Enum.Name,
                variantBinding.Variant.Name,
                variantBinding.Variant.Tag,
                variantBinding.Enum.MaxPayloadArity,
                variantBinding.Variant.PayloadTypes);
            return _namedTypeSymbols.TryGetValue(variantBinding.Enum.Name, out var enumType)
                ? enumType
                : new EnumTypeSymbol(variantBinding.Enum.Name, []);
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

    private TypeSymbol CheckNewExpression(NewExpression expression)
    {
        var argumentTypes = expression.Arguments.Select(CheckExpression).ToList();
        if (!TryResolveImportedTypePath(expression.TypePath, out var resolvedTypePath, out var diagnostic))
        {
            _result.Diagnostics.Report(expression.Span, diagnostic, "T122");
            return TypeSymbols.Error;
        }

        if (!ConstructorClrResolver.TryResolve(
                resolvedTypePath,
                argumentTypes,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            return TypeSymbols.Error;
        }

        _result.ResolvedConstructorTypePaths[expression] = resolvedTypePath;
        return binding.ConstructedType;
    }

    private TypeSymbol CheckMemberAccessExpression(MemberAccessExpression expression)
    {
        if (TryCheckInstanceValueAccess(expression, out var instanceType))
        {
            return instanceType;
        }

        if (!TryGetQualifiedMemberPath(expression, out var memberPath))
        {
            _result.Diagnostics.Report(
                expression.Span,
                "static member access target must be a member path like 'System.Environment.NewLine' or 'Environment.NewLine'",
                "T122");
            return TypeSymbols.Error;
        }

        if (!TryResolveImportedPath(memberPath, StaticClrMethodResolver.IsKnownValuePath, out var resolvedPath, out var resolutionDiagnostic))
        {
            _result.Diagnostics.Report(expression.Span, resolutionDiagnostic, "T122");
            return TypeSymbols.Error;
        }

        if (!StaticClrMethodResolver.TryResolveValue(resolvedPath, out var binding, out var resolutionError, out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            return TypeSymbols.Error;
        }

        _result.ResolvedStaticValuePaths[expression] = resolvedPath;
        return binding.Type;
    }

    private bool TryCheckInstanceValueAccess(MemberAccessExpression expression, out TypeSymbol valueType)
    {
        valueType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(expression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(expression.Object);
        if (receiverType == TypeSymbols.Error)
        {
            return true;
        }

        if (!InstanceClrMemberResolver.TryResolveValue(
                receiverType,
                expression.Member,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(expression.Span, resolutionMessage, code);
            valueType = TypeSymbols.Error;
            return true;
        }

        _result.ResolvedInstanceValueMembers[expression] = expression.Member;
        valueType = binding.Type;
        return true;
    }

    private bool TryCheckInstanceMethodCall(
        CallExpression callExpression,
        MemberAccessExpression memberAccessExpression,
        IReadOnlyList<ClrCallArgument> arguments,
        out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (!IsPotentialInstanceReceiver(memberAccessExpression.Object))
        {
            return false;
        }

        var receiverType = CheckExpression(memberAccessExpression.Object);
        if (receiverType == TypeSymbols.Error)
        {
            return true;
        }

        if (!InstanceClrMemberResolver.TryResolveMethod(
                receiverType,
                memberAccessExpression.Member,
                arguments,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionMessage, code);
            return true;
        }

        _result.ResolvedInstanceMethodMembers[callExpression] = memberAccessExpression.Member;
        returnType = binding.ReturnType;
        return true;
    }

    private bool TryCheckStaticMethodCall(
        CallExpression callExpression,
        MemberAccessExpression memberAccessExpression,
        IReadOnlyList<ClrCallArgument> arguments,
        out TypeSymbol returnType)
    {
        returnType = TypeSymbols.Error;

        if (!TryGetQualifiedMemberPath(memberAccessExpression, out var methodPath))
        {
            _result.Diagnostics.Report(
                memberAccessExpression.Span,
                "static method call target must be a member path like 'System.Console.WriteLine' or 'Console.WriteLine'",
                "T122");
            return true;
        }

        if (!TryResolveImportedPath(methodPath, StaticClrMethodResolver.IsKnownMethodPath, out var resolvedMethodPath, out var resolutionDiagnostic))
        {
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionDiagnostic, "T122");
            return true;
        }

        if (!StaticClrMethodResolver.TryResolve(
                resolvedMethodPath,
                arguments,
                out var binding,
                out var resolutionError,
                out var resolutionMessage))
        {
            var code = resolutionError == StaticClrMethodResolutionError.NoMatchingOverload ? "T113" : "T122";
            _result.Diagnostics.Report(memberAccessExpression.Span, resolutionMessage, code);
            return true;
        }

        _result.ResolvedStaticMethodPaths[callExpression] = resolvedMethodPath;
        returnType = binding.ReturnType;

        return true;
    }

    private List<ClrCallArgument> BuildClrCallArguments(CallExpression expression)
    {
        var arguments = new List<ClrCallArgument>(expression.Arguments.Count);
        foreach (var argument in expression.Arguments)
        {
            var type = CheckExpression(argument.Expression);

            if (argument.Modifier is CallArgumentModifier.Out or CallArgumentModifier.Ref)
            {
                ValidateByRefArgument(argument);
            }

            arguments.Add(new ClrCallArgument(type, argument.Modifier));
        }

        return arguments;
    }

    private void ValidateByRefArgument(CallArgument argument)
    {
        if (argument.Expression is not Identifier identifier)
        {
            _result.Diagnostics.Report(argument.Span,
                "out/ref arguments must be identifier variables",
                "T122");
            return;
        }

        if (!_names.IdentifierSymbols.TryGetValue(identifier, out var symbol))
        {
            return;
        }

        if (!_names.MutableSymbols.Contains(symbol))
        {
            _result.Diagnostics.Report(argument.Span,
                $"out/ref argument '{identifier.Value}' must be declared with 'var'",
                "T123");
        }
    }

    private static bool TryGetQualifiedMemberPath(MemberAccessExpression expression, out string methodPath)
    {
        var segments = new Stack<string>();
        IExpression current = expression;

        while (current is MemberAccessExpression memberAccess)
        {
            if (string.IsNullOrWhiteSpace(memberAccess.Member))
            {
                methodPath = string.Empty;
                return false;
            }

            segments.Push(memberAccess.Member);
            current = memberAccess.Object;
        }

        if (current is not Identifier identifier)
        {
            methodPath = string.Empty;
            return false;
        }

        segments.Push(identifier.Value);
        methodPath = string.Join('.', segments);
        return true;
    }

    private bool TryResolveImportedPath(
        string memberPath,
        Func<string, bool> isKnownCandidate,
        out string resolvedPath,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        resolvedPath = memberPath;

        if (isKnownCandidate(memberPath))
        {
            return true;
        }

        var firstDot = memberPath.IndexOf('.');
        if (firstDot <= 0)
        {
            return true;
        }

        var root = memberPath[..firstDot];
        var suffix = memberPath[firstDot..];
        var candidates = new HashSet<string>();

        if (!_names.ImportedTypeAliases.TryGetValue(root, out var qualifiedTypeName))
        {
            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + memberPath);
            }
        }
        else
        {
            candidates.Add(qualifiedTypeName + suffix);

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + memberPath);
            }
        }

        var knownCandidates = candidates
            .Where(isKnownCandidate)
            .ToList();

        if (knownCandidates.Count == 0)
        {
            return true;
        }

        if (knownCandidates.Count == 1)
        {
            resolvedPath = knownCandidates[0];
            return true;
        }

        diagnostic = $"ambiguous static member reference '{memberPath}' from imports; matching candidates: {string.Join(", ", knownCandidates.OrderBy(c => c))}";
        return false;
    }

    private bool TryResolveImportedTypePath(string typePath, out string resolvedPath, out string diagnostic)
    {
        diagnostic = string.Empty;
        resolvedPath = typePath;

        if (ConstructorClrResolver.IsKnownTypePath(typePath))
        {
            return true;
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var firstDot = typePath.IndexOf('.');

        if (firstDot > 0)
        {
            var root = typePath[..firstDot];
            var suffix = typePath[firstDot..];
            if (_names.ImportedTypeAliases.TryGetValue(root, out var qualifiedTypeName))
            {
                candidates.Add(qualifiedTypeName + suffix);
            }

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + typePath);
            }
        }
        else
        {
            if (_names.ImportedTypeAliases.TryGetValue(typePath, out var qualifiedTypeName))
            {
                candidates.Add(qualifiedTypeName);
            }

            foreach (var importedNamespace in _names.ImportedNamespaces)
            {
                candidates.Add(importedNamespace + "." + typePath);
            }
        }

        var knownCandidates = candidates.Where(ConstructorClrResolver.IsKnownTypePath).ToList();
        if (knownCandidates.Count == 0)
        {
            return true;
        }

        if (knownCandidates.Count == 1)
        {
            resolvedPath = knownCandidates[0];
            return true;
        }

        diagnostic = $"ambiguous type reference '{typePath}' from imports; matching candidates: {string.Join(", ", knownCandidates.OrderBy(c => c))}";
        return false;
    }

    private bool IsPotentialInstanceReceiver(IExpression expression)
    {
        if (expression is Identifier identifier)
        {
            return _names.IdentifierSymbols.ContainsKey(identifier);
        }

        if (expression is MemberAccessExpression memberAccess)
        {
            return IsPotentialInstanceReceiver(memberAccess.Object);
        }

        return true;
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

    private TypeSymbol RequireNumericOperands(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (TryGetBinaryNumericResultType(left, right, out var resultType))
        {
            return resultType;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires matching numeric operands, got '{left}' and '{right}'",
                "T103");
        }

        return TypeSymbols.Error;
    }

    private TypeSymbol RequireComparableNumericTypes(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (TryGetBinaryNumericResultType(left, right, out _))
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires matching numeric operands, got '{left}' and '{right}'",
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

    private TypeSymbol RequireBothBool(InfixExpression expression, TypeSymbol left, TypeSymbol right)
    {
        if (left == TypeSymbols.Bool && right == TypeSymbols.Bool)
        {
            return TypeSymbols.Bool;
        }

        if (left != TypeSymbols.Error && right != TypeSymbols.Error)
        {
            _result.Diagnostics.Report(expression.Span,
                $"operator '{expression.Operator}' requires 'bool' operands, got '{left}' and '{right}'",
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

        if (CanWidenNumeric(source, target))
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

        if (left is EnumTypeSymbol leftEnum && right is EnumTypeSymbol rightEnum)
        {
            if (!string.Equals(leftEnum.EnumName, rightEnum.EnumName, StringComparison.Ordinal) ||
                leftEnum.TypeArguments.Count != rightEnum.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftEnum.TypeArguments.Count; i++)
            {
                if (!TypeEquals(leftEnum.TypeArguments[i], rightEnum.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return left == right;
    }

    private static bool IsNumericType(TypeSymbol type)
    {
        return type == TypeSymbols.Int ||
               type == TypeSymbols.Long ||
               type == TypeSymbols.Float ||
               type == TypeSymbols.Double;
    }

    private static bool TryGetUnaryNegationResultType(TypeSymbol operandType, out TypeSymbol resultType)
    {
        resultType = TypeSymbols.Error;
        if (operandType != TypeSymbols.Int && operandType != TypeSymbols.Long && operandType != TypeSymbols.Float && operandType != TypeSymbols.Double)
        {
            return false;
        }

        resultType = operandType;
        return true;
    }

    private static bool TryGetBinaryNumericResultType(TypeSymbol left, TypeSymbol right, out TypeSymbol resultType)
    {
        resultType = TypeSymbols.Error;
        if (!IsNumericType(left) || !IsNumericType(right))
        {
            return false;
        }

        if (!TypeEquals(left, right))
        {
            return false;
        }

        resultType = left;
        return true;
    }

    private static bool CanWidenNumeric(TypeSymbol source, TypeSymbol target)
    {
        if (source == TypeSymbols.Char && target == TypeSymbols.Char)
        {
            return true;
        }

        if (source == TypeSymbols.Char)
        {
            return target == TypeSymbols.Int || target == TypeSymbols.Long || target == TypeSymbols.Float || target == TypeSymbols.Double;
        }

        if (source == TypeSymbols.Byte)
        {
            return target == TypeSymbols.Byte || target == TypeSymbols.Int || target == TypeSymbols.Long || target == TypeSymbols.Float || target == TypeSymbols.Double;
        }

        if (!IsNumericType(source) || !IsNumericType(target))
        {
            return false;
        }

        return GetNumericRank(source) <= GetNumericRank(target);
    }

    private static int GetNumericRank(TypeSymbol type)
    {
        if (type == TypeSymbols.Byte)
        {
            return 1;
        }

        if (type == TypeSymbols.Int)
        {
            return 2;
        }

        if (type == TypeSymbols.Long)
        {
            return 3;
        }

        if (type == TypeSymbols.Float)
        {
            return 4;
        }

        if (type == TypeSymbols.Double)
        {
            return 5;
        }

        return int.MaxValue;
    }
}
