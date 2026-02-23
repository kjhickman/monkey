using Kong.Common;
using Kong.Parsing;

namespace Kong.Semantic;

public static class ProgramValidator
{
    public static DiagnosticBag ValidateEntrypoint(CompilationUnit unit, TypeCheckResult typeCheck)
    {
        var diagnostics = new DiagnosticBag();

        foreach (var statement in unit.Statements)
        {
            if (statement is FunctionDeclaration or
                EnumDeclaration or
                ClassDeclaration or
                InterfaceDeclaration or
                ImplBlock or
                InterfaceImplBlock or
                ImportStatement or
                NamespaceStatement)
            {
                continue;
            }

            diagnostics.Report(
                statement.Span,
                "top-level statements are not allowed; declare functions and imports only",
                "CLI002");
        }

        var mains = unit.Statements
            .OfType<FunctionDeclaration>()
            .Where(d => d.Name.Value == "Main")
            .ToList();

        if (mains.Count == 0)
        {
            diagnostics.Report(unit.Span, "missing required entrypoint 'fn Main() {}'", "CLI003");
            return diagnostics;
        }

        if (mains.Count > 1)
        {
            foreach (var declaration in mains)
            {
                diagnostics.Report(declaration.Name.Span,
                    "duplicate 'Main' function declaration",
                    "CLI004");
            }
            return diagnostics;
        }

        var main = mains[0];
        if (main.Parameters.Count != 0)
        {
            diagnostics.Report(main.Span, "'Main' must not declare parameters", "CLI005");
        }

        if (typeCheck.DeclaredFunctionTypes.TryGetValue(main, out var mainType))
        {
            if (mainType.ReturnType != TypeSymbols.Void && mainType.ReturnType != TypeSymbols.Int)
            {
                diagnostics.Report(main.Span, "'Main' must have return type 'void' or 'int'", "CLI006");
            }
        }

        foreach (var statement in unit.Statements)
        {
            if (statement is FunctionDeclaration declaration)
            {
                ReportUnsupportedIfWithoutElse(declaration.Body, diagnostics);
            }
            else if (statement is ImplBlock implBlock)
            {
                if (implBlock.Constructor != null)
                {
                    ReportUnsupportedIfWithoutElse(implBlock.Constructor.Body, diagnostics);
                }

                foreach (var method in implBlock.Methods)
                {
                    ReportUnsupportedIfWithoutElse(method.Body, diagnostics);
                }
            }
            else if (statement is InterfaceImplBlock interfaceImplBlock)
            {
                foreach (var method in interfaceImplBlock.Methods)
                {
                    ReportUnsupportedIfWithoutElse(method.Body, diagnostics);
                }
            }
        }

        return diagnostics;
    }

    private static void ReportUnsupportedIfWithoutElse(BlockStatement block, DiagnosticBag diagnostics)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case LetStatement { Value: { } value }:
                    ReportUnsupportedIfWithoutElse(value, diagnostics);
                    break;
                case AssignmentStatement { Value: { } assignedValue }:
                    ReportUnsupportedIfWithoutElse(assignedValue, diagnostics);
                    break;
                case IndexAssignmentStatement indexAssignmentStatement:
                    ReportUnsupportedIfWithoutElse(indexAssignmentStatement.Target.Left, diagnostics);
                    ReportUnsupportedIfWithoutElse(indexAssignmentStatement.Target.Index, diagnostics);
                    ReportUnsupportedIfWithoutElse(indexAssignmentStatement.Value, diagnostics);
                    break;
                case MemberAssignmentStatement memberAssignmentStatement:
                    ReportUnsupportedIfWithoutElse(memberAssignmentStatement.Target.Object, diagnostics);
                    ReportUnsupportedIfWithoutElse(memberAssignmentStatement.Value, diagnostics);
                    break;
                case ForInStatement forInStatement:
                    ReportUnsupportedIfWithoutElse(forInStatement.Iterable, diagnostics);
                    ReportUnsupportedIfWithoutElse(forInStatement.Body, diagnostics);
                    break;
                case BreakStatement:
                case ContinueStatement:
                    break;
                case ReturnStatement { ReturnValue: { } returnValue }:
                    ReportUnsupportedIfWithoutElse(returnValue, diagnostics);
                    break;
                case ExpressionStatement { Expression: { } expression }:
                    ReportUnsupportedIfWithoutElse(expression, diagnostics);
                    break;
                case BlockStatement nested:
                    ReportUnsupportedIfWithoutElse(nested, diagnostics);
                    break;
            }
        }
    }

    private static void ReportUnsupportedIfWithoutElse(IExpression expression, DiagnosticBag diagnostics)
    {
        switch (expression)
        {
            case IfExpression ifExpression:
                if (ifExpression.Alternative == null)
                {
                    diagnostics.Report(
                        ifExpression.Span,
                        "if expressions must include an else branch for the current CLR backend",
                        "CLI007");
                }

                ReportUnsupportedIfWithoutElse(ifExpression.Consequence, diagnostics);
                if (ifExpression.Alternative != null)
                {
                    ReportUnsupportedIfWithoutElse(ifExpression.Alternative, diagnostics);
                }
                break;
            case PrefixExpression prefixExpression:
                ReportUnsupportedIfWithoutElse(prefixExpression.Right, diagnostics);
                break;
            case InfixExpression infixExpression:
                ReportUnsupportedIfWithoutElse(infixExpression.Left, diagnostics);
                ReportUnsupportedIfWithoutElse(infixExpression.Right, diagnostics);
                break;
            case FunctionLiteral functionLiteral:
                ReportUnsupportedIfWithoutElse(functionLiteral.Body, diagnostics);
                break;
            case MatchExpression matchExpression:
                ReportUnsupportedIfWithoutElse(matchExpression.Target, diagnostics);
                foreach (var arm in matchExpression.Arms)
                {
                    ReportUnsupportedIfWithoutElse(arm.Body, diagnostics);
                }
                break;
            case CallExpression callExpression:
                ReportUnsupportedIfWithoutElse(callExpression.Function, diagnostics);
                foreach (var argument in callExpression.Arguments)
                {
                    ReportUnsupportedIfWithoutElse(argument.Expression, diagnostics);
                }
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    ReportUnsupportedIfWithoutElse(element, diagnostics);
                }
                break;
            case IndexExpression indexExpression:
                ReportUnsupportedIfWithoutElse(indexExpression.Left, diagnostics);
                ReportUnsupportedIfWithoutElse(indexExpression.Index, diagnostics);
                break;
        }
    }
}
