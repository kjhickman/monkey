using Kong.Ast;
using Kong.Lexer;
using Kong.Parser;

namespace Kong.Tests;

public class ParserTests
{
    [Theory]
    [InlineData("let x = 5;", "x", 5L)]
    [InlineData("let y = true;", "y", true)]
    [InlineData("let foobar = y;", "foobar", "y")]
    public void TestLetStatements(string input, string expectedIdentifier, object expectedValue)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = program.Statements[0];
        TestLetStatement(stmt, expectedIdentifier);

        var val = Assert.IsType<LetStatement>(stmt).Value!;
        TestLiteralExpression(val, expectedValue);
    }

    [Theory]
    [InlineData("return 5;", 5L)]
    [InlineData("return true;", true)]
    [InlineData("return foobar;", "foobar")]
    public void TestReturnStatements(string input, object expectedValue)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var returnStmt = Assert.IsType<ReturnStatement>(program.Statements[0]);
        Assert.Equal("return", returnStmt.TokenLiteral());
        TestLiteralExpression(returnStmt.ReturnValue!, expectedValue);
    }

    [Fact]
    public void TestIdentifierExpression()
    {
        var input = "foobar;";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var ident = Assert.IsType<Identifier>(stmt.Expression);
        Assert.Equal("foobar", ident.Value);
        Assert.Equal("foobar", ident.TokenLiteral());
    }

    [Fact]
    public void TestIntegerLiteralExpression()
    {
        var input = "5;";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var literal = Assert.IsType<IntegerLiteral>(stmt.Expression);
        Assert.Equal(5L, literal.Value);
        Assert.Equal("5", literal.TokenLiteral());
    }

    [Theory]
    [InlineData("!5;", "!", 5L)]
    [InlineData("-15;", "-", 15L)]
    [InlineData("!foobar;", "!", "foobar")]
    [InlineData("-foobar;", "-", "foobar")]
    [InlineData("!true;", "!", true)]
    [InlineData("!false;", "!", false)]
    public void TestParsingPrefixExpressions(string input, string op, object value)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var exp = Assert.IsType<PrefixExpression>(stmt.Expression);
        Assert.Equal(op, exp.Operator);
        TestLiteralExpression(exp.Right, value);
    }

    [Theory]
    [InlineData("5 + 5;", 5L, "+", 5L)]
    [InlineData("5 - 5;", 5L, "-", 5L)]
    [InlineData("5 * 5;", 5L, "*", 5L)]
    [InlineData("5 / 5;", 5L, "/", 5L)]
    [InlineData("5 > 5;", 5L, ">", 5L)]
    [InlineData("5 < 5;", 5L, "<", 5L)]
    [InlineData("5 == 5;", 5L, "==", 5L)]
    [InlineData("5 != 5;", 5L, "!=", 5L)]
    [InlineData("foobar + barfoo;", "foobar", "+", "barfoo")]
    [InlineData("foobar - barfoo;", "foobar", "-", "barfoo")]
    [InlineData("foobar * barfoo;", "foobar", "*", "barfoo")]
    [InlineData("foobar / barfoo;", "foobar", "/", "barfoo")]
    [InlineData("foobar > barfoo;", "foobar", ">", "barfoo")]
    [InlineData("foobar < barfoo;", "foobar", "<", "barfoo")]
    [InlineData("foobar == barfoo;", "foobar", "==", "barfoo")]
    [InlineData("foobar != barfoo;", "foobar", "!=", "barfoo")]
    [InlineData("true == true", true, "==", true)]
    [InlineData("true != false", true, "!=", false)]
    [InlineData("false == false", false, "==", false)]
    public void TestParsingInfixExpressions(string input, object leftValue, string op, object rightValue)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        TestInfixExpression(stmt.Expression!, leftValue, op, rightValue);
    }

    [Theory]
    [InlineData("-a * b", "((-a) * b)")]
    [InlineData("!-a", "(!(-a))")]
    [InlineData("a + b + c", "((a + b) + c)")]
    [InlineData("a + b - c", "((a + b) - c)")]
    [InlineData("a * b * c", "((a * b) * c)")]
    [InlineData("a * b / c", "((a * b) / c)")]
    [InlineData("a + b / c", "(a + (b / c))")]
    [InlineData("a + b * c + d / e - f", "(((a + (b * c)) + (d / e)) - f)")]
    [InlineData("3 + 4; -5 * 5", "(3 + 4)((-5) * 5)")]
    [InlineData("5 > 4 == 3 < 4", "((5 > 4) == (3 < 4))")]
    [InlineData("5 < 4 != 3 > 4", "((5 < 4) != (3 > 4))")]
    [InlineData("3 + 4 * 5 == 3 * 1 + 4 * 5", "((3 + (4 * 5)) == ((3 * 1) + (4 * 5)))")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("3 > 5 == false", "((3 > 5) == false)")]
    [InlineData("3 < 5 == true", "((3 < 5) == true)")]
    [InlineData("1 + (2 + 3) + 4", "((1 + (2 + 3)) + 4)")]
    [InlineData("(5 + 5) * 2", "((5 + 5) * 2)")]
    [InlineData("2 / (5 + 5)", "(2 / (5 + 5))")]
    [InlineData("(5 + 5) * 2 * (5 + 5)", "(((5 + 5) * 2) * (5 + 5))")]
    [InlineData("-(5 + 5)", "(-(5 + 5))")]
    [InlineData("!(true == true)", "(!(true == true))")]
    [InlineData("a + add(b * c) + d", "((a + add((b * c))) + d)")]
    [InlineData("add(a, b, 1, 2 * 3, 4 + 5, add(6, 7 * 8))", "add(a, b, 1, (2 * 3), (4 + 5), add(6, (7 * 8)))")]
    [InlineData("add(a + b + c * d / f + g)", "add((((a + b) + ((c * d) / f)) + g))")]
    [InlineData("a * [1, 2, 3, 4][b * c] * d", "((a * ([1, 2, 3, 4][(b * c)])) * d)")]
    [InlineData("add(a * b[2], b[1], 2 * [1, 2][1])", "add((a * (b[2])), (b[1]), (2 * ([1, 2][1])))")]
    public void TestOperatorPrecedenceParsing(string input, string expected)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Equal(expected, program.String());
    }

    [Theory]
    [InlineData("true;", true)]
    [InlineData("false;", false)]
    public void TestBooleanExpression(string input, bool expectedBoolean)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var boolean = Assert.IsType<BooleanLiteral>(stmt.Expression);
        Assert.Equal(expectedBoolean, boolean.Value);
    }

    [Fact]
    public void TestIfExpression()
    {
        var input = "if (x < y) { x }";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var exp = Assert.IsType<IfExpression>(stmt.Expression);

        TestInfixExpression(exp.Condition, "x", "<", "y");

        Assert.Single(exp.Consequence.Statements);

        var consequence = Assert.IsType<ExpressionStatement>(exp.Consequence.Statements[0]);
        TestIdentifier(consequence.Expression!, "x");

        Assert.Null(exp.Alternative);
    }

    [Fact]
    public void TestIfElseExpression()
    {
        var input = "if (x < y) { x } else { y }";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var exp = Assert.IsType<IfExpression>(stmt.Expression);

        TestInfixExpression(exp.Condition, "x", "<", "y");

        Assert.Single(exp.Consequence.Statements);
        var consequence = Assert.IsType<ExpressionStatement>(exp.Consequence.Statements[0]);
        TestIdentifier(consequence.Expression!, "x");

        Assert.NotNull(exp.Alternative);
        Assert.Single(exp.Alternative!.Statements);
        var alternative = Assert.IsType<ExpressionStatement>(exp.Alternative.Statements[0]);
        TestIdentifier(alternative.Expression!, "y");
    }

    [Fact]
    public void TestFunctionLiteralParsing()
    {
        var input = "fn(x, y) { x + y; }";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Expression);

        Assert.Equal(2, function.Parameters.Count);

        TestLiteralExpression(function.Parameters[0], "x");
        TestLiteralExpression(function.Parameters[1], "y");

        Assert.Single(function.Body.Statements);

        var bodyStmt = Assert.IsType<ExpressionStatement>(function.Body.Statements[0]);
        TestInfixExpression(bodyStmt.Expression!, "x", "+", "y");
    }

    [Theory]
    [InlineData("fn() {};", new string[] { })]
    [InlineData("fn(x) {};", new[] { "x" })]
    [InlineData("fn(x, y, z) {};", new[] { "x", "y", "z" })]
    public void TestFunctionParameterParsing(string input, string[] expectedParams)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Expression);

        Assert.Equal(expectedParams.Length, function.Parameters.Count);

        for (var i = 0; i < expectedParams.Length; i++)
        {
            TestLiteralExpression(function.Parameters[i], expectedParams[i]);
        }
    }

    [Fact]
    public void TestCallExpressionParsing()
    {
        var input = "add(1, 2 * 3, 4 + 5);";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var exp = Assert.IsType<CallExpression>(stmt.Expression);

        TestIdentifier(exp.Function, "add");

        Assert.Equal(3, exp.Arguments.Count);

        TestLiteralExpression(exp.Arguments[0], 1L);
        TestInfixExpression(exp.Arguments[1], 2L, "*", 3L);
        TestInfixExpression(exp.Arguments[2], 4L, "+", 5L);
    }

    [Theory]
    [InlineData("add();", "add", new string[] { })]
    [InlineData("add(1);", "add", new[] { "1" })]
    [InlineData("add(1, 2 * 3, 4 + 5);", "add", new[] { "1", "(2 * 3)", "(4 + 5)" })]
    public void TestCallExpressionParameterParsing(string input, string expectedIdent, string[] expectedArgs)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var exp = Assert.IsType<CallExpression>(stmt.Expression);

        TestIdentifier(exp.Function, expectedIdent);

        Assert.Equal(expectedArgs.Length, exp.Arguments.Count);

        for (var i = 0; i < expectedArgs.Length; i++)
        {
            Assert.Equal(expectedArgs[i], exp.Arguments[i].String());
        }
    }

    [Fact]
    public void TestStringLiteralExpression()
    {
        var input = "\"hello world\";";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var literal = Assert.IsType<StringLiteral>(stmt.Expression);

        Assert.Equal("hello world", literal.Value);
    }

    [Fact]
    public void TestParsingArrayLiterals()
    {
        var input = "[1, 2 * 2, 3 + 3]";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var array = Assert.IsType<ArrayLiteral>(stmt.Expression);

        Assert.Equal(3, array.Elements.Count);

        TestIntegerLiteral(array.Elements[0], 1);
        TestInfixExpression(array.Elements[1], 2L, "*", 2L);
        TestInfixExpression(array.Elements[2], 3L, "+", 3L);
    }

    [Fact]
    public void TestParsingIndexExpressions()
    {
        var input = "myArray[1 + 1]";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var indexExp = Assert.IsType<IndexExpression>(stmt.Expression);

        TestIdentifier(indexExp.Left, "myArray");
        TestInfixExpression(indexExp.Index, 1L, "+", 1L);
    }

    [Fact]
    public void TestParsingHashLiteralsStringKeys()
    {
        var input = "{\"one\": 1, \"two\": 2, \"three\": 3}";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var hash = Assert.IsType<HashLiteral>(stmt.Expression);

        Assert.Equal(3, hash.Pairs.Count);

        var expected = new Dictionary<string, long>
        {
            { "one", 1 },
            { "two", 2 },
            { "three", 3 },
        };

        foreach (var pair in hash.Pairs)
        {
            var literal = Assert.IsType<StringLiteral>(pair.Key);
            var expectedValue = expected[literal.String()];
            TestIntegerLiteral(pair.Value, expectedValue);
        }
    }

    [Fact]
    public void TestParsingEmptyHashLiteral()
    {
        var input = "{}";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var hash = Assert.IsType<HashLiteral>(stmt.Expression);

        Assert.Empty(hash.Pairs);
    }

    [Fact]
    public void TestParsingHashLiteralsWithExpressions()
    {
        var input = "{\"one\": 0 + 1, \"two\": 10 - 8, \"three\": 15 / 5}";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(program.Statements[0]);
        var hash = Assert.IsType<HashLiteral>(stmt.Expression);

        Assert.Equal(3, hash.Pairs.Count);

        var tests = new Dictionary<string, Action<IExpression>>
        {
            { "one", e => TestInfixExpression(e, 0L, "+", 1L) },
            { "two", e => TestInfixExpression(e, 10L, "-", 8L) },
            { "three", e => TestInfixExpression(e, 15L, "/", 5L) },
        };

        foreach (var pair in hash.Pairs)
        {
            var literal = Assert.IsType<StringLiteral>(pair.Key);
            Assert.True(tests.ContainsKey(literal.String()), $"No test function for key {literal.String()} found");
            tests[literal.String()](pair.Value);
        }
    }

    [Fact]
    public void TestFunctionLiteralWithName()
    {
        var input = "let myFunction = fn() { };";

        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        var program = p.ParseProgram();
        CheckParserErrors(p);

        Assert.Single(program.Statements);

        var stmt = Assert.IsType<LetStatement>(program.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Value);

        Assert.Equal("myFunction", function.Name);
    }

    // --- Helper methods ---

    private static void CheckParserErrors(Parser.Parser p)
    {
        var errors = p.Errors();
        if (errors.Count == 0) return;

        var message = $"parser has {errors.Count} errors\n";
        foreach (var err in errors)
        {
            message += $"parser error: \"{err}\"\n";
        }
        Assert.Fail(message);
    }

    private static void TestLetStatement(IStatement stmt, string name)
    {
        Assert.Equal("let", stmt.TokenLiteral());
        var letStmt = Assert.IsType<LetStatement>(stmt);
        Assert.Equal(name, letStmt.Name.Value);
        Assert.Equal(name, letStmt.Name.TokenLiteral());
    }

    private static void TestInfixExpression(IExpression exp, object left, string op, object right)
    {
        var opExp = Assert.IsType<InfixExpression>(exp);
        TestLiteralExpression(opExp.Left, left);
        Assert.Equal(op, opExp.Operator);
        TestLiteralExpression(opExp.Right, right);
    }

    private static void TestLiteralExpression(IExpression exp, object expected)
    {
        switch (expected)
        {
            case long l:
                TestIntegerLiteral(exp, l);
                break;
            case int i:
                TestIntegerLiteral(exp, i);
                break;
            case string s:
                TestIdentifier(exp, s);
                break;
            case bool b:
                TestBooleanLiteral(exp, b);
                break;
            default:
                Assert.Fail($"type of expected not handled. got={expected.GetType()}");
                break;
        }
    }

    private static void TestIntegerLiteral(IExpression exp, long value)
    {
        var integ = Assert.IsType<IntegerLiteral>(exp);
        Assert.Equal(value, integ.Value);
        Assert.Equal(value.ToString(), integ.TokenLiteral());
    }

    private static void TestIdentifier(IExpression exp, string value)
    {
        var ident = Assert.IsType<Identifier>(exp);
        Assert.Equal(value, ident.Value);
        Assert.Equal(value, ident.TokenLiteral());
    }

    private static void TestBooleanLiteral(IExpression exp, bool value)
    {
        var bo = Assert.IsType<BooleanLiteral>(exp);
        Assert.Equal(value, bo.Value);
        Assert.Equal(value.ToString().ToLower(), bo.TokenLiteral());
    }
}
