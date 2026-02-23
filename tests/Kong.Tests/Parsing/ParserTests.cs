using Kong.Common;
using Kong.Lexing;
using Kong.Parsing;

namespace Kong.Tests.Parsing;

public class ParserTests
{
    [Theory]
    [InlineData("let x = 5;", "x", 5L)]
    [InlineData("let y = true;", "y", true)]
    [InlineData("let foobar = y;", "foobar", "y")]
    public void TestLetStatements(string input, string expectedIdentifier, object expectedValue)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = unit.Statements[0];
        TestLetStatement(stmt, expectedIdentifier);

        var val = Assert.IsType<LetStatement>(stmt).Value!;
        TestLiteralExpression(val, expectedValue);
    }

    [Theory]
    [InlineData("let x: int = 5;", "int")]
    [InlineData("let pi: double = 1.25;", "double")]
    [InlineData("let c: char = 'a';", "char")]
    [InlineData("let b: byte = 42b;", "byte")]
    [InlineData("let xs: int[] = [1, 2];", "int[]")]
    public void TestLetStatementsWithTypeAnnotations(string input, string expectedType)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<LetStatement>(unit.Statements[0]);
        Assert.NotNull(stmt.TypeAnnotation);
        Assert.Equal(expectedType, stmt.TypeAnnotation!.String());
    }

    [Theory]
    [InlineData("return 5;", 5L)]
    [InlineData("return true;", true)]
    [InlineData("return foobar;", "foobar")]
    public void TestReturnStatements(string input, object expectedValue)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var returnStmt = Assert.IsType<ReturnStatement>(unit.Statements[0]);
        Assert.Equal("return", returnStmt.TokenLiteral());
        TestLiteralExpression(returnStmt.ReturnValue!, expectedValue);
    }

    [Fact]
    public void TestIdentifierExpression()
    {
        var input = "foobar;";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var ident = Assert.IsType<Identifier>(stmt.Expression);
        Assert.Equal("foobar", ident.Value);
        Assert.Equal("foobar", ident.TokenLiteral());
    }

    [Fact]
    public void TestIntegerLiteralExpression()
    {
        var input = "5;";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var literal = Assert.IsType<IntegerLiteral>(stmt.Expression);
        Assert.Equal(5L, literal.Value);
        Assert.Equal("5", literal.TokenLiteral());
    }

    [Fact]
    public void TestDoubleLiteralExpression()
    {
        var input = "1.5;";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var literal = Assert.IsType<DoubleLiteral>(stmt.Expression);
        Assert.Equal(1.5, literal.Value);
        Assert.Equal("1.5", literal.TokenLiteral());
    }

    [Fact]
    public void TestCharLiteralExpression()
    {
        var input = "'a';";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var literal = Assert.IsType<CharLiteral>(stmt.Expression);
        Assert.Equal('a', literal.Value);
        Assert.Equal("a", literal.TokenLiteral());
    }

    [Fact]
    public void TestByteLiteralExpression()
    {
        var input = "42b;";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var literal = Assert.IsType<ByteLiteral>(stmt.Expression);
        Assert.Equal((byte)42, literal.Value);
        Assert.Equal("42", literal.TokenLiteral());
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
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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
    [InlineData("true && false", true, "&&", false)]
    [InlineData("true || false", true, "||", false)]
    public void TestParsingInfixExpressions(string input, object leftValue, string op, object rightValue)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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
    [InlineData("true || false && true", "(true || (false && true))")]
    [InlineData("(true || false) && true", "((true || false) && true)")]
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
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Equal(expected, unit.String());
    }

    [Theory]
    [InlineData("true;", true)]
    [InlineData("false;", false)]
    public void TestBooleanExpression(string input, bool expectedBoolean)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var boolean = Assert.IsType<BooleanLiteral>(stmt.Expression);
        Assert.Equal(expectedBoolean, boolean.Value);
    }

    [Fact]
    public void TestIfExpression()
    {
        var input = "if (x < y) { x }";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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
    public void TestParsesProgramWithLineComments()
    {
        var input = "let x: int = 1; // keep this around\nlet y: int = x + 1;";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Equal(2, unit.Statements.Count);
        var yLet = Assert.IsType<LetStatement>(unit.Statements[1]);
        TestIdentifier(yLet.Name, "y");
        var infix = Assert.IsType<InfixExpression>(yLet.Value);
        TestIdentifier(infix.Left, "x");
        TestIntegerLiteral(infix.Right, 1);
    }

    [Fact]
    public void TestFunctionLiteralParsing()
    {
        var input = "fn(x, y) { x + y; }";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Expression);

        Assert.Equal(2, function.Parameters.Count);

        Assert.Equal("x", function.Parameters[0].Name);
        Assert.Equal("y", function.Parameters[1].Name);

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
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Expression);

        Assert.Equal(expectedParams.Length, function.Parameters.Count);

        for (var i = 0; i < expectedParams.Length; i++)
        {
            Assert.Equal(expectedParams[i], function.Parameters[i].Name);
        }
    }

    [Fact]
    public void TestFunctionParameterAndReturnTypeParsing()
    {
        var input = "fn(x: int, y: string[]) -> int[] { x }";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Expression);

        Assert.Equal(2, function.Parameters.Count);
        Assert.Equal("x", function.Parameters[0].Name);
        Assert.Equal("int", function.Parameters[0].TypeAnnotation?.String());
        Assert.Equal("y", function.Parameters[1].Name);
        Assert.Equal("string[]", function.Parameters[1].TypeAnnotation?.String());

        Assert.NotNull(function.ReturnTypeAnnotation);
        Assert.Equal("int[]", function.ReturnTypeAnnotation!.String());
    }

    [Fact]
    public void TestNamedFunctionDeclarationParsing()
    {
        var input = "fn Add(x: int, y: int) -> int { x + y; } Add(1, 2);";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var declaration = Assert.IsType<FunctionDeclaration>(unit.Statements[0]);
        Assert.Equal("Add", declaration.Name.Value);
        Assert.Equal(2, declaration.Parameters.Count);
        Assert.Equal("int", declaration.ReturnTypeAnnotation?.String());

        var callStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var callExpression = Assert.IsType<CallExpression>(callStatement.Expression);
        var callIdentifier = Assert.IsType<Identifier>(callExpression.Function);
        Assert.Equal("Add", callIdentifier.Value);
    }

    [Fact]
    public void TestNamedFunctionDeclarationDefaultsReturnTypeToVoid()
    {
        var input = "fn Main() { }";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var declaration = Assert.IsType<FunctionDeclaration>(unit.Statements[0]);
        var returnType = Assert.IsType<NamedType>(declaration.ReturnTypeAnnotation);
        Assert.Equal("void", returnType.Name);
    }

    [Fact]
    public void TestCallExpressionParsing()
    {
        var input = "add(1, 2 * 3, 4 + 5);";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var exp = Assert.IsType<CallExpression>(stmt.Expression);

        TestIdentifier(exp.Function, "add");

        Assert.Equal(3, exp.Arguments.Count);

        TestLiteralExpression(exp.Arguments[0].Expression, 1L);
        TestInfixExpression(exp.Arguments[1].Expression, 2L, "*", 3L);
        TestInfixExpression(exp.Arguments[2].Expression, 4L, "+", 5L);
    }

    [Theory]
    [InlineData("add();", "add", new string[] { })]
    [InlineData("add(1);", "add", new[] { "1" })]
    [InlineData("add(1, 2 * 3, 4 + 5);", "add", new[] { "1", "(2 * 3)", "(4 + 5)" })]
    public void TestCallExpressionParameterParsing(string input, string expectedIdent, string[] expectedArgs)
    {
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var literal = Assert.IsType<StringLiteral>(stmt.Expression);

        Assert.Equal("hello world", literal.Value);
    }

    [Fact]
    public void TestParsingArrayLiterals()
    {
        var input = "[1, 2 * 2, 3 + 3]";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
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

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var indexExp = Assert.IsType<IndexExpression>(stmt.Expression);

        TestIdentifier(indexExp.Left, "myArray");
        TestInfixExpression(indexExp.Index, 1L, "+", 1L);
    }

    [Fact]
    public void TestFunctionLiteralWithName()
    {
        var input = "let myFunction = fn() { };";

        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Single(unit.Statements);

        var stmt = Assert.IsType<LetStatement>(unit.Statements[0]);
        var function = Assert.IsType<FunctionLiteral>(stmt.Value);

        Assert.Equal("myFunction", function.Name);
    }

    // --- Helper methods ---

    private static void CheckParserErrors(Parser p)
    {
        var diagnostics = p.Diagnostics;
        if (diagnostics.Count == 0) return;

        var message = $"parser has {diagnostics.Count} errors\n";
        foreach (var d in diagnostics.All)
        {
            message += $"parser error: \"{d.Message}\"\n";
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
            case double d:
                TestDoubleLiteral(exp, d);
                break;
            case char c:
                TestCharLiteral(exp, c);
                break;
            case byte b:
                TestByteLiteral(exp, b);
                break;
            case bool boolValue:
                TestBooleanLiteral(exp, boolValue);
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

    private static void TestDoubleLiteral(IExpression exp, double value)
    {
        var literal = Assert.IsType<DoubleLiteral>(exp);
        Assert.Equal(value, literal.Value);
    }

    private static void TestCharLiteral(IExpression exp, char value)
    {
        var literal = Assert.IsType<CharLiteral>(exp);
        Assert.Equal(value, literal.Value);
    }

    private static void TestByteLiteral(IExpression exp, byte value)
    {
        var literal = Assert.IsType<ByteLiteral>(exp);
        Assert.Equal(value, literal.Value);
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

    // --- Span tests ---

    private static void AssertSpan(
        Span span,
        int startLine, int startCol,
        int endLine, int endCol)
    {
        Assert.Equal(startLine, span.Start.Line);
        Assert.Equal(startCol, span.Start.Column);
        Assert.Equal(endLine, span.End.Line);
        Assert.Equal(endCol, span.End.Column);
    }

    [Fact]
    public void TestLetStatementSpan()
    {
        // Input:  let x = 5;
        // Cols:   1234567890
        var input = "let x = 5;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<LetStatement>(unit.Statements[0]);
        // Span covers the whole statement from 'let' to ';'
        AssertSpan(stmt.Span, 1, 1, 1, 11);

        // The identifier 'x'
        AssertSpan(stmt.Name.Span, 1, 5, 1, 6);

        // The value '5'
        var value = Assert.IsType<IntegerLiteral>(stmt.Value);
        AssertSpan(value.Span, 1, 9, 1, 10);
    }

    [Fact]
    public void TestReturnStatementSpan()
    {
        var input = "return 42;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var stmt = Assert.IsType<ReturnStatement>(unit.Statements[0]);
        AssertSpan(stmt.Span, 1, 1, 1, 11);
    }

    [Fact]
    public void TestIdentifierSpan()
    {
        var input = "foobar;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var ident = Assert.IsType<Identifier>(exprStmt.Expression);
        AssertSpan(ident.Span, 1, 1, 1, 7);
    }

    [Fact]
    public void TestIntegerLiteralSpan()
    {
        var input = "123;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var intLit = Assert.IsType<IntegerLiteral>(exprStmt.Expression);
        AssertSpan(intLit.Span, 1, 1, 1, 4);
    }

    [Fact]
    public void TestStringLiteralSpan()
    {
        var input = "\"hello\";";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var strLit = Assert.IsType<StringLiteral>(exprStmt.Expression);
        AssertSpan(strLit.Span, 1, 1, 1, 8);
    }

    [Fact]
    public void TestBooleanLiteralSpan()
    {
        var input = "true;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var boolLit = Assert.IsType<BooleanLiteral>(exprStmt.Expression);
        AssertSpan(boolLit.Span, 1, 1, 1, 5);
    }

    [Fact]
    public void TestPrefixExpressionSpan()
    {
        // Input:  !true;
        // Cols:   123456
        var input = "!true;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var prefix = Assert.IsType<PrefixExpression>(exprStmt.Expression);
        // Span from '!' to end of 'true'
        AssertSpan(prefix.Span, 1, 1, 1, 6);
    }

    [Fact]
    public void TestInfixExpressionSpan()
    {
        // Input:  1 + 20;
        // Cols:   1234567
        var input = "1 + 20;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var infix = Assert.IsType<InfixExpression>(exprStmt.Expression);
        // Span from start of '1' to end of '20'
        AssertSpan(infix.Span, 1, 1, 1, 7);
    }

    [Fact]
    public void TestIfExpressionSpan()
    {
        // Input:  if (x) { y }
        // Cols:   123456789012
        var input = "if (x) { y }";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var ifExpr = Assert.IsType<IfExpression>(exprStmt.Expression);
        // Span from 'if' to closing '}'
        AssertSpan(ifExpr.Span, 1, 1, 1, 13);
    }

    [Fact]
    public void TestIfElseExpressionSpan()
    {
        var input = "if (x) { y } else { z }";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var ifExpr = Assert.IsType<IfExpression>(exprStmt.Expression);
        // Span from 'if' to closing '}' of else block
        AssertSpan(ifExpr.Span, 1, 1, 1, 24);
    }

    [Fact]
    public void TestFunctionLiteralSpan()
    {
        // Input:  fn(x) { x }
        // Cols:   12345678901
        var input = "fn(x) { x }";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var fnLit = Assert.IsType<FunctionLiteral>(exprStmt.Expression);
        // Span from 'fn' to closing '}'
        AssertSpan(fnLit.Span, 1, 1, 1, 12);
        // Parameter 'x'
        AssertSpan(fnLit.Parameters[0].Span, 1, 4, 1, 5);
    }

    [Fact]
    public void TestCallExpressionSpan()
    {
        // Input:  add(1, 2)
        // Cols:   123456789
        var input = "add(1, 2)";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var call = Assert.IsType<CallExpression>(exprStmt.Expression);
        // Span from 'add' to closing ')'
        AssertSpan(call.Span, 1, 1, 1, 10);
    }

    [Fact]
    public void TestArrayLiteralSpan()
    {
        // Input:  [1, 2, 3]
        // Cols:   123456789
        var input = "[1, 2, 3]";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var arr = Assert.IsType<ArrayLiteral>(exprStmt.Expression);
        AssertSpan(arr.Span, 1, 1, 1, 10);
    }

    [Fact]
    public void TestIndexExpressionSpan()
    {
        // Input:  arr[0]
        // Cols:   123456
        var input = "arr[0]";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var idx = Assert.IsType<IndexExpression>(exprStmt.Expression);
        AssertSpan(idx.Span, 1, 1, 1, 7);
    }

    [Fact]
    public void TestBlockStatementSpan()
    {
        // Test via function literal body
        var input = "fn() { 1; 2 }";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var fnLit = Assert.IsType<FunctionLiteral>(exprStmt.Expression);
        // Block span from '{' to '}'
        AssertSpan(fnLit.Body.Span, 1, 6, 1, 14);
    }

    [Fact]
    public void TestMultiLineSpans()
    {
        var input = "let x = 1;\nlet y = 2;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Equal(2, unit.Statements.Count);

        var stmt1 = Assert.IsType<LetStatement>(unit.Statements[0]);
        AssertSpan(stmt1.Span, 1, 1, 1, 11);

        var stmt2 = Assert.IsType<LetStatement>(unit.Statements[1]);
        AssertSpan(stmt2.Span, 2, 1, 2, 11);

        // Program span covers everything
        AssertSpan(unit.Span, 1, 1, 2, 11);
    }

    [Fact]
    public void TestMultiLineFunctionSpan()
    {
        var input = "fn(x) {\n  return x;\n}";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var fnLit = Assert.IsType<FunctionLiteral>(exprStmt.Expression);
        // Span from 'fn' on line 1 to '}' on line 3
        AssertSpan(fnLit.Span, 1, 1, 3, 2);
        // Body span from '{' on line 1 to '}' on line 3
        AssertSpan(fnLit.Body.Span, 1, 7, 3, 2);
        // Return statement on line 2
        var retStmt = Assert.IsType<ReturnStatement>(fnLit.Body.Statements[0]);
        AssertSpan(retStmt.Span, 2, 3, 2, 12);
    }

    [Fact]
    public void TestExpressionStatementSpan()
    {
        // Expression statement with semicolon
        var input = "5;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var exprStmt = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        // Includes the semicolon
        AssertSpan(exprStmt.Span, 1, 1, 1, 3);
    }

    [Fact]
    public void TestParsesMemberAccessExpression()
    {
        var input = "System.Console.WriteLine;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var topLevel = Assert.IsType<MemberAccessExpression>(statement.Expression);
        Assert.Equal("WriteLine", topLevel.Member);

        var parent = Assert.IsType<MemberAccessExpression>(topLevel.Object);
        Assert.Equal("Console", parent.Member);

        var root = Assert.IsType<Identifier>(parent.Object);
        Assert.Equal("System", root.Value);
    }

    [Fact]
    public void TestParsesStaticMethodCallExpression()
    {
        var input = "System.Console.WriteLine(42);";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        var function = Assert.IsType<MemberAccessExpression>(call.Function);
        Assert.Equal("WriteLine", function.Member);
        Assert.Single(call.Arguments);
        Assert.IsType<IntegerLiteral>(call.Arguments[0].Expression);
    }

    [Fact]
    public void TestParsesNewExpression()
    {
        var input = "new System.Text.StringBuilder(16);";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var newExpression = Assert.IsType<NewExpression>(statement.Expression);
        Assert.Equal("System.Text.StringBuilder", newExpression.TypePath);
        Assert.Single(newExpression.Arguments);
        TestIntegerLiteral(newExpression.Arguments[0], 16);
    }

    [Fact]
    public void TestParsesVarStatement()
    {
        var input = "var x: int = 1;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<LetStatement>(unit.Statements[0]);
        Assert.True(statement.IsMutable);
        Assert.Equal("x", statement.Name.Value);
    }

    [Fact]
    public void TestParsesAssignmentStatement()
    {
        var input = "var x = 1; x = x + 1;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        Assert.Equal(2, unit.Statements.Count);
        var assignment = Assert.IsType<AssignmentStatement>(unit.Statements[1]);
        Assert.Equal("x", assignment.Name.Value);
        var infix = Assert.IsType<InfixExpression>(assignment.Value);
        TestIdentifier(infix.Left, "x");
        TestIntegerLiteral(infix.Right, 1);
    }

    [Fact]
    public void TestParsesCallArgumentModifiers()
    {
        var input = "Foo(out x, ref y, z);";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[0]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal(CallArgumentModifier.Out, call.Arguments[0].Modifier);
        Assert.Equal(CallArgumentModifier.Ref, call.Arguments[1].Modifier);
        Assert.Equal(CallArgumentModifier.None, call.Arguments[2].Modifier);
    }

    [Fact]
    public void TestParsesForInStatement()
    {
        var input = "for i in xs { i; }";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var statement = Assert.IsType<ForInStatement>(unit.Statements[0]);
        Assert.Equal("i", statement.Iterator.Value);
        var iterable = Assert.IsType<Identifier>(statement.Iterable);
        Assert.Equal("xs", iterable.Value);
        Assert.Single(statement.Body.Statements);
    }

    [Fact]
    public void TestParsesImportStatement()
    {
        var input = "import System.Console;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var importStatement = Assert.IsType<ImportStatement>(unit.Statements[0]);
        Assert.Equal("System.Console", importStatement.QualifiedName);
        Assert.Equal("Console", importStatement.Alias);
    }

    [Fact]
    public void TestRejectsPathImportStatement()
    {
        var input = "import \"./util.kg\";";
        var l = new Lexer(input);
        var p = new Parser(l);
        p.ParseCompilationUnit();

        Assert.True(p.Diagnostics.HasErrors);
        Assert.Contains(p.Diagnostics.All, d => d.Code == "P001");
    }

    [Fact]
    public void TestParsesNamespaceStatement()
    {
        var input = "namespace Foo.Bar;";
        var l = new Lexer(input);
        var p = new Parser(l);
        var unit = p.ParseCompilationUnit();
        CheckParserErrors(p);

        var namespaceStatement = Assert.IsType<NamespaceStatement>(unit.Statements[0]);
        Assert.Equal("Foo.Bar", namespaceStatement.QualifiedName);
    }

    [Fact]
    public void TestParserErrorIncludesPosition()
    {
        var input = "let = 5;";
        var l = new Lexer(input);
        var p = new Parser(l);
        p.ParseCompilationUnit();

        Assert.True(p.Diagnostics.HasErrors);
        // Diagnostic should carry position information in its Span
        var diag = p.Diagnostics.All[0];
        Assert.NotEqual(Span.Empty, diag.Span);
        Assert.True(diag.Span.Start.Line > 0);
    }
}
