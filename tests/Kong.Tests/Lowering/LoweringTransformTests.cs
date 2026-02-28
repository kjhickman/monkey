using Kong.Compilation;
using Kong.Lexing;
using Kong.Lowering;
using P = Kong.Parsing;
using Kong.Semantics;
using Kong.Semantics.Binding;

namespace Kong.Tests.Lowering;

public class LoweringTransformTests
{
    [Fact]
    public void Compile_LoweringCanonicalizesBuiltinCallsToIntrinsics()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-lowering-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("puts(len([1, 2, 3]));", "program", outputAssemblyPath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.LoweringResult);

        var intrinsicNames = CollectIntrinsicNames(result.LoweringResult!.Program);
        Assert.Contains("puts", intrinsicNames);
        Assert.Contains("len", intrinsicNames);
    }

    [Fact]
    public void Compile_LoweringExtractsTemporariesForNestedCallArguments()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-lowering-{Guid.NewGuid():N}.dll");
        var source = "let add = fn(a: int, b: int) { a + b }; puts(add(1 + 2, len([1, 2, 3])));";

        var result = compiler.Compile(source, "program", outputAssemblyPath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.LoweringResult);

        Assert.Contains(
            result.LoweringResult!.Program.Statements,
            statement => statement is LetStatement { Name.Value: var name } && name.StartsWith("__lowered_tmp_", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_LoweringConvertsExpressionIfToExplicitIfStatement()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-lowering-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("if (true) { puts(1); }", "program", outputAssemblyPath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.LoweringResult);

        var ifStatement = Assert.IsType<IfStatement>(result.LoweringResult!.Program.Statements.Single());
        Assert.NotNull(ifStatement.Alternative);
        Assert.Empty(ifStatement.Alternative.Statements);
    }

    [Fact]
    public void Compile_LoweringConvertsNestedStatementIfBranchesToIfStatements()
    {
        var compiler = new Compiler();
        var outputAssemblyPath = Path.Combine(Path.GetTempPath(), $"kong-lowering-{Guid.NewGuid():N}.dll");

        var result = compiler.Compile("if (true) { if (false) { 1 } else { 2 } }; puts(3);", "program", outputAssemblyPath);

        Assert.True(result.Succeeded);
        var outerIf = Assert.IsType<IfStatement>(result.LoweringResult!.Program.Statements[0]);
        var nested = Assert.IsType<IfStatement>(outerIf.Consequence.Statements.Single());
        Assert.NotNull(nested.Alternative);
    }

    [Fact]
    public void Lowering_TemporaryNamesAvoidExistingIdentifiers()
    {
        var existingName = "__lowered_tmp_0";
        var letStatement = new P.LetStatement
        {
            Token = new Token(TokenType.Let, "let"),
            Name = new P.Identifier { Token = new Token(TokenType.Ident, existingName), Value = existingName },
            Value = new P.IntegerLiteral { Token = new Token(TokenType.Int, "100"), Value = 100 },
        };

        var callStatement = new P.ExpressionStatement
        {
            Token = new Token(TokenType.Ident, "puts"),
            Expression = new P.CallExpression
            {
                Token = new Token(TokenType.LParen, "("),
                Function = new P.Identifier { Token = new Token(TokenType.Ident, "puts"), Value = "puts" },
                Arguments =
                [
                    new P.InfixExpression
                    {
                        Token = new Token(TokenType.Plus, "+"),
                        Left = new P.IntegerLiteral { Token = new Token(TokenType.Int, "1"), Value = 1 },
                        Operator = "+",
                        Right = new P.IntegerLiteral { Token = new Token(TokenType.Int, "2"), Value = 2 },
                    },
                ],
            },
        };

        var program = new P.Program { Statements = [letStatement, callStatement] };
        var boundProgram = new BoundProgram(program, [], new Dictionary<P.FunctionLiteral, BoundFunctionExpression>());
        boundProgram.SetTypeInfo(new SemanticModel());

        var lowerer = new CanonicalLowerer();
        var result = lowerer.Lower(program, boundProgram);

        Assert.False(result.DiagnosticBag.HasErrors);
        var letNames = result.Program.Statements
            .OfType<LetStatement>()
            .Select(s => s.Name.Value)
            .ToList();

        Assert.Equal(letNames.Count, letNames.Distinct(StringComparer.Ordinal).Count());
    }

    private static HashSet<string> CollectIntrinsicNames(Program program)
    {
        var intrinsicNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in program.Statements)
        {
            VisitStatement(statement, intrinsicNames);
        }

        return intrinsicNames;
    }

    private static void VisitStatement(IStatement statement, ISet<string> names)
    {
        switch (statement)
        {
            case LetStatement { Value: not null } letStatement:
                VisitExpression(letStatement.Value, names);
                break;
            case ReturnStatement { ReturnValue: not null } returnStatement:
                VisitExpression(returnStatement.ReturnValue, names);
                break;
            case ExpressionStatement { Expression: not null } expressionStatement:
                VisitExpression(expressionStatement.Expression, names);
                break;
            case IfStatement ifStatement:
                VisitExpression(ifStatement.Condition, names);
                foreach (var child in ifStatement.Consequence.Statements)
                {
                    VisitStatement(child, names);
                }

                foreach (var child in ifStatement.Alternative.Statements)
                {
                    VisitStatement(child, names);
                }

                break;
            case BlockStatement blockStatement:
                foreach (var child in blockStatement.Statements)
                {
                    VisitStatement(child, names);
                }

                break;
        }
    }

    private static void VisitExpression(IExpression expression, ISet<string> names)
    {
        switch (expression)
        {
            case IntrinsicCallExpression intrinsic:
                names.Add(intrinsic.Name);
                foreach (var argument in intrinsic.Arguments)
                {
                    VisitExpression(argument, names);
                }

                break;
            case CallExpression callExpression:
                VisitExpression(callExpression.Function, names);
                foreach (var argument in callExpression.Arguments)
                {
                    VisitExpression(argument, names);
                }

                break;
            case PrefixExpression prefixExpression:
                VisitExpression(prefixExpression.Right, names);
                break;
            case InfixExpression infixExpression:
                VisitExpression(infixExpression.Left, names);
                VisitExpression(infixExpression.Right, names);
                break;
            case IfExpression ifExpression:
                VisitExpression(ifExpression.Condition, names);
                foreach (var child in ifExpression.Consequence.Statements)
                {
                    VisitStatement(child, names);
                }

                if (ifExpression.Alternative is not null)
                {
                    foreach (var child in ifExpression.Alternative.Statements)
                    {
                        VisitStatement(child, names);
                    }
                }

                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    VisitExpression(element, names);
                }

                break;
            case HashLiteral hashLiteral:
                foreach (var pair in hashLiteral.Pairs)
                {
                    VisitExpression(pair.Key, names);
                    VisitExpression(pair.Value, names);
                }

                break;
            case IndexExpression indexExpression:
                VisitExpression(indexExpression.Left, names);
                VisitExpression(indexExpression.Index, names);
                break;
            case FunctionLiteral functionLiteral:
                foreach (var child in functionLiteral.Body.Statements)
                {
                    VisitStatement(child, names);
                }

                break;
        }
    }
}
