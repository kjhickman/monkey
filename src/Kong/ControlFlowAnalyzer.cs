namespace Kong;

public class ControlFlowAnalyzer
{
    private readonly record struct FlowState(bool AlwaysReturns);

    public void AnalyzeFunction(
        FunctionLiteral function,
        TypeSymbol returnType,
        IReadOnlyDictionary<IExpression, TypeSymbol> expressionTypes,
        DiagnosticBag diagnostics)
    {
        var flow = AnalyzeBlock(function.Body, diagnostics);
        if (returnType == TypeSymbols.Void || returnType == TypeSymbols.Error)
        {
            return;
        }

        if (flow.AlwaysReturns)
        {
            return;
        }

        if (CanUseTailExpressionReturn(function.Body, returnType, expressionTypes))
        {
            return;
        }

        diagnostics.Report(
            function.Body.Span,
            $"not all code paths return a value of type '{returnType}'",
            "T117");
    }

    private static bool CanUseTailExpressionReturn(
        BlockStatement body,
        TypeSymbol returnType,
        IReadOnlyDictionary<IExpression, TypeSymbol> expressionTypes)
    {
        if (body.Statements.Count == 0)
        {
            return false;
        }

        if (body.Statements[^1] is not ExpressionStatement expressionStatement || expressionStatement.Expression == null)
        {
            return false;
        }

        if (!expressionTypes.TryGetValue(expressionStatement.Expression, out var expressionType))
        {
            return false;
        }

        return IsAssignable(expressionType, returnType);
    }

    private FlowState AnalyzeBlock(BlockStatement block, DiagnosticBag diagnostics)
    {
        var reachable = true;

        foreach (var statement in block.Statements)
        {
            if (!reachable)
            {
                diagnostics.Report(statement.Span, "unreachable code", "T118", Severity.Warning);
                continue;
            }

            var statementFlow = AnalyzeStatement(statement, diagnostics);
            if (statementFlow.AlwaysReturns)
            {
                reachable = false;
            }
        }

        return new FlowState(AlwaysReturns: !reachable);
    }

    private FlowState AnalyzeStatement(IStatement statement, DiagnosticBag diagnostics)
    {
        return statement switch
        {
            ReturnStatement => new FlowState(AlwaysReturns: true),
            BlockStatement blockStatement => AnalyzeBlock(blockStatement, diagnostics),
            LetStatement letStatement => AnalyzeExpression(letStatement.Value, diagnostics),
            ExpressionStatement expressionStatement => AnalyzeExpression(expressionStatement.Expression, diagnostics),
            _ => new FlowState(AlwaysReturns: false),
        };
    }

    private FlowState AnalyzeExpression(IExpression? expression, DiagnosticBag diagnostics)
    {
        if (expression is IfExpression { Alternative: not null } ifExpression)
        {
            var consequence = AnalyzeBlock(ifExpression.Consequence, diagnostics);
            var alternative = AnalyzeBlock(ifExpression.Alternative, diagnostics);
            return new FlowState(AlwaysReturns: consequence.AlwaysReturns && alternative.AlwaysReturns);
        }

        return new FlowState(AlwaysReturns: false);
    }

    private static bool IsAssignable(TypeSymbol source, TypeSymbol target)
    {
        if (source == TypeSymbols.Error || target == TypeSymbols.Error)
        {
            return true;
        }

        return TypeEquals(source, target);
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
}
