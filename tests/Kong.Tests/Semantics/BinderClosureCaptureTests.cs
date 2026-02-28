using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantics.Binding;

namespace Kong.Tests.Semantics;

public class BinderClosureCaptureTests
{
    [Fact]
    public void Bind_CapturesOuterParameterInNestedClosure()
    {
        var program = Parse("let newClosure = fn(a: int) { fn() { a; }; };\n");
        var binder = new Binder();

        var result = binder.Bind(program);

        Assert.Empty(result.Errors);
        var outer = GetTopLevelFunction(program, 0);
        var inner = GetNestedFunction(outer);
        var captures = result.BoundProgram.GetClosureCaptureNames(inner);
        Assert.Equal(["a"], captures);
    }

    [Fact]
    public void Bind_DoesNotCaptureTopLevelFunctionSymbol()
    {
        var program = Parse("let a = fn() { 1 }; let b = fn() { a() };\n");
        var binder = new Binder();

        var result = binder.Bind(program);

        Assert.Empty(result.Errors);
        var b = GetTopLevelFunction(program, 1);
        var captures = result.BoundProgram.GetClosureCaptureNames(b);
        Assert.Empty(captures);
    }

    [Fact]
    public void Bind_CapturesOnlyImmediateFreeVariablesPerClosure()
    {
        var source = "let newAdderOuter = fn(a: int, b: int) { let c = a + b; fn(d: int) { let e = d + c; fn(f: int) { e + f; }; }; };";
        var program = Parse(source);
        var binder = new Binder();

        var result = binder.Bind(program);

        Assert.Empty(result.Errors);
        var outer = GetTopLevelFunction(program, 0);
        var middle = GetNestedFunction(outer);
        var inner = GetNestedFunction(middle);

        Assert.Equal(["c"], result.BoundProgram.GetClosureCaptureNames(middle));
        Assert.Equal(["e"], result.BoundProgram.GetClosureCaptureNames(inner));
    }

    private static Program Parse(string source)
    {
        var parser = new Parser(new Lexer(source));
        var program = parser.ParseProgram();
        Assert.Empty(parser.Errors());
        return program;
    }

    private static FunctionLiteral GetTopLevelFunction(Program program, int index)
    {
        var letStatement = Assert.IsType<LetStatement>(program.Statements[index]);
        return Assert.IsType<FunctionLiteral>(letStatement.Value);
    }

    private static FunctionLiteral GetNestedFunction(FunctionLiteral functionLiteral)
    {
        var expression = Assert.IsType<ExpressionStatement>(functionLiteral.Body.Statements.Last());
        return Assert.IsType<FunctionLiteral>(expression.Expression);
    }
}
