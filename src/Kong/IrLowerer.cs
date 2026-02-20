namespace Kong;

public class IrLowerer
{
    private IrLoweringResult _result = new();
    private readonly Dictionary<IExpression, TypeSymbol> _expressionTypes = [];
    private IrFunction _function = null!;
    private IrBlock _entryBlock = null!;
    private int _nextValueId;

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

        if (unit.Statements.Count != 1)
        {
            _result.Diagnostics.Report(unit.Span,
                "phase-2 IR lowerer currently supports exactly one top-level expression statement",
                "IR001");
            return _result;
        }

        if (unit.Statements[0] is not ExpressionStatement { Expression: { } expression })
        {
            _result.Diagnostics.Report(unit.Statements[0].Span,
                "phase-2 IR lowerer currently supports expression statements only",
                "IR001");
            return _result;
        }

        if (!TryGetExpressionType(expression, out var returnType))
        {
            return _result;
        }

        if (returnType != TypeSymbols.Int)
        {
            _result.Diagnostics.Report(expression.Span,
                $"phase-2 IR lowerer currently supports only 'int' top-level expressions, got '{returnType}'",
                "IR001");
            return _result;
        }

        _nextValueId = 0;
        _function = new IrFunction
        {
            Name = "__main",
            ReturnType = returnType,
        };
        _entryBlock = new IrBlock { Id = 0 };
        _function.Blocks.Add(_entryBlock);

        var loweredValue = LowerExpression(expression);
        if (loweredValue == null)
        {
            return _result;
        }

        _entryBlock.Terminator = new IrReturn(loweredValue.Value);
        _result.Program = new IrProgram { EntryPoint = _function };
        return _result;
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
