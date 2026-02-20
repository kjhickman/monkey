namespace Kong;

public class IrLowerer
{
    private IrLoweringResult _result = new();
    private readonly Dictionary<IExpression, TypeSymbol> _expressionTypes = [];
    private IrFunction _function = null!;
    private IrBlock _entryBlock = null!;
    private readonly Dictionary<string, IrLocalId> _localsByName = [];
    private int _nextValueId;
    private int _nextLocalId;

    public IrLoweringResult Lower(CompilationUnit unit, TypeCheckResult typeCheckResult)
    {
        _result = new IrLoweringResult();
        _expressionTypes.Clear();
        foreach (var pair in typeCheckResult.ExpressionTypes)
        {
            _expressionTypes[pair.Key] = pair.Value;
        }

        _result.Diagnostics.AddRange(typeCheckResult.Diagnostics);
        if (_result.Diagnostics.HasErrors)
        {
            return _result;
        }

        _nextValueId = 0;
        _nextLocalId = 0;
        _localsByName.Clear();
        _function = new IrFunction
        {
            Name = "__main",
            ReturnType = TypeSymbols.Int,
        };
        _entryBlock = new IrBlock { Id = 0 };
        _function.Blocks.Add(_entryBlock);

        IrValueId? returnValue = null;
        for (var i = 0; i < unit.Statements.Count; i++)
        {
            var isLast = i == unit.Statements.Count - 1;
            var statement = unit.Statements[i];

            switch (statement)
            {
                case LetStatement letStatement:
                    if (!LowerLetStatement(letStatement))
                    {
                        return _result;
                    }
                    break;

                case ExpressionStatement { Expression: { } expressionStatementExpression }:
                {
                    var loweredValue = LowerExpression(expressionStatementExpression);
                    if (loweredValue == null)
                    {
                        return _result;
                    }

                    if (isLast)
                    {
                        if (!TryGetExpressionType(expressionStatementExpression, out var expressionType))
                        {
                            return _result;
                        }

                        if (expressionType != TypeSymbols.Int)
                        {
                            _result.Diagnostics.Report(expressionStatementExpression.Span,
                                $"phase-3 IR lowerer currently supports only 'int' top-level result expressions, got '{expressionType}'",
                                "IR001");
                            return _result;
                        }

                        returnValue = loweredValue.Value;
                    }
                    break;
                }

                default:
                    _result.Diagnostics.Report(statement.Span,
                        "phase-3 IR lowerer currently supports top-level let and expression statements only",
                        "IR001");
                    return _result;
            }
        }

        if (returnValue == null)
        {
            _result.Diagnostics.Report(unit.Span,
                "phase-3 IR lowerer requires a final expression statement result",
                "IR001");
            return _result;
        }

        _entryBlock.Terminator = new IrReturn(returnValue.Value);
        _result.Program = new IrProgram { EntryPoint = _function };
        return _result;
    }

    private bool LowerLetStatement(LetStatement statement)
    {
        if (!TryGetVariableType(statement, out var variableType))
        {
            return false;
        }

        if (variableType != TypeSymbols.Int)
        {
            _result.Diagnostics.Report(statement.Span,
                $"phase-3 IR lowerer currently supports only 'int' let bindings, got '{variableType}'",
                "IR001");
            return false;
        }

        if (statement.Value == null)
        {
            _result.Diagnostics.Report(statement.Span,
                "phase-3 IR lowerer requires let initializers",
                "IR001");
            return false;
        }

        var value = LowerExpression(statement.Value);
        if (value == null)
        {
            return false;
        }

        var local = AllocateLocal(statement.Name.Value, variableType);
        _entryBlock.Instructions.Add(new IrStoreLocal(local, value.Value));
        return true;
    }

    private IrValueId? LowerExpression(IExpression expression)
    {
        switch (expression)
        {
            case IntegerLiteral integerLiteral:
            {
                var destination = AllocateValue(TypeSymbols.Int);
                _entryBlock.Instructions.Add(new IrConstInt(destination, integerLiteral.Value));
                return destination;
            }

            case Identifier identifier:
            {
                if (!_localsByName.TryGetValue(identifier.Value, out var local))
                {
                    _result.Diagnostics.Report(identifier.Span,
                        $"phase-3 IR lowerer could not resolve local '{identifier.Value}'",
                        "IR002");
                    return null;
                }

                if (!TryGetExpressionType(identifier, out var identifierType))
                {
                    return null;
                }

                if (identifierType != TypeSymbols.Int)
                {
                    _result.Diagnostics.Report(identifier.Span,
                        $"phase-3 IR lowerer currently supports only 'int' identifiers, got '{identifierType}'",
                        "IR001");
                    return null;
                }

                var destination = AllocateValue(identifierType);
                _entryBlock.Instructions.Add(new IrLoadLocal(destination, local));
                return destination;
            }

            case InfixExpression infixExpression:
            {
                if (!TryMapBinaryOperator(infixExpression.Operator, out var op))
                {
                    _result.Diagnostics.Report(infixExpression.Span,
                        $"phase-2 IR lowerer does not support operator '{infixExpression.Operator}'",
                        "IR001");
                    return null;
                }

                var leftTypeOk = TryGetExpressionType(infixExpression.Left, out var leftType);
                var rightTypeOk = TryGetExpressionType(infixExpression.Right, out var rightType);
                if (!leftTypeOk || !rightTypeOk)
                {
                    return null;
                }

                if (leftType != TypeSymbols.Int || rightType != TypeSymbols.Int)
                {
                    _result.Diagnostics.Report(infixExpression.Span,
                        $"phase-2 IR lowerer currently supports only 'int' arithmetic operands, got '{leftType}' and '{rightType}'",
                        "IR001");
                    return null;
                }

                var left = LowerExpression(infixExpression.Left);
                if (left == null)
                {
                    return null;
                }

                var right = LowerExpression(infixExpression.Right);
                if (right == null)
                {
                    return null;
                }

                var destination = AllocateValue(TypeSymbols.Int);
                _entryBlock.Instructions.Add(new IrBinary(destination, op, left.Value, right.Value));
                return destination;
            }

            case PrefixExpression prefixExpression when prefixExpression.Operator == "-":
            {
                if (!TryGetExpressionType(prefixExpression.Right, out var rightType))
                {
                    return null;
                }

                if (rightType != TypeSymbols.Int)
                {
                    _result.Diagnostics.Report(prefixExpression.Span,
                        $"phase-3 IR lowerer currently supports only int unary negation, got '{rightType}'",
                        "IR001");
                    return null;
                }

                var zero = AllocateValue(TypeSymbols.Int);
                _entryBlock.Instructions.Add(new IrConstInt(zero, 0));

                var right = LowerExpression(prefixExpression.Right);
                if (right == null)
                {
                    return null;
                }

                var destination = AllocateValue(TypeSymbols.Int);
                _entryBlock.Instructions.Add(new IrBinary(destination, IrBinaryOperator.Subtract, zero, right.Value));
                return destination;
            }

            default:
                _result.Diagnostics.Report(expression.Span,
                    $"phase-2 IR lowerer does not support expression '{expression.TokenLiteral()}'",
                    "IR001");
                return null;
        }
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

    private IrValueId AllocateValue(TypeSymbol type)
    {
        var id = new IrValueId(_nextValueId++);
        _function.ValueTypes[id] = type;
        return id;
    }

    private IrLocalId AllocateLocal(string name, TypeSymbol type)
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

    private bool TryGetVariableType(LetStatement statement, out TypeSymbol type)
    {
        if (statement.TypeAnnotation != null)
        {
            type = TypeAnnotationBinder.Bind(statement.TypeAnnotation, _result.Diagnostics) ?? TypeSymbols.Error;
            if (type == TypeSymbols.Error)
            {
                return false;
            }

            return true;
        }

        if (statement.Value != null && TryGetExpressionType(statement.Value, out type))
        {
            return true;
        }

        type = TypeSymbols.Error;
        return false;
    }

    private static bool TryMapBinaryOperator(string op, out IrBinaryOperator irOp)
    {
        irOp = op switch
        {
            "+" => IrBinaryOperator.Add,
            "-" => IrBinaryOperator.Subtract,
            "*" => IrBinaryOperator.Multiply,
            "/" => IrBinaryOperator.Divide,
            _ => default,
        };

        return op is "+" or "-" or "*" or "/";
    }
}
