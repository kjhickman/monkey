using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Tests;

namespace Kong.Tests.Semantic;

public class TypeCheckerTests
{
    [Fact]
    public void TestTypedLetAndIdentifierUsage()
    {
        var (unit, names, result) = ParseResolveAndCheck("let x: int = 6; let y: int = x; y;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var yStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var yIdentifier = Assert.IsType<Identifier>(yStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(yIdentifier, out var yType));
        Assert.Equal(TypeSymbols.Int, yType);
    }

    [Fact]
    public void TestAllowsAssigningIntExpressionToLongVariable()
    {
        var (unit, names, result) = ParseResolveAndCheck("let x: long = 6; x;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var xLet = Assert.IsType<LetStatement>(unit.Statements[1]);
        Assert.Equal(TypeSymbols.Long, result.VariableTypes[xLet]);

        var xUse = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var xIdentifier = Assert.IsType<Identifier>(xUse.Expression);
        Assert.Equal(TypeSymbols.Long, result.ExpressionTypes[xIdentifier]);
    }

    [Fact]
    public void TestTypedFunctionCall()
    {
        var input = "fn(x: int, y: int) -> int { return x + y; }(1, 2);";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestInfersLetTypeFromInitializer()
    {
        var (unit, _, result) = ParseResolveAndCheck("let x = 6; x;");

        Assert.False(result.Diagnostics.HasErrors);

        var xLet = Assert.IsType<LetStatement>(unit.Statements[1]);
        Assert.True(result.VariableTypes.TryGetValue(xLet, out var xType));
        Assert.Equal(TypeSymbols.Int, xType);

        var xUse = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var xIdentifier = Assert.IsType<Identifier>(xUse.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(xIdentifier, out var xUseType));
        Assert.Equal(TypeSymbols.Int, xUseType);
    }

    [Fact]
    public void TestCannotInferLetTypeFromNullInitializer()
    {
        var (_, _, result) = ParseResolveAndCheck("let x = if (true) { 1 }; ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T119");
    }

    [Fact]
    public void TestCannotInferLetTypeFromEmptyArrayInitializer()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs = []; ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T120");
    }

    [Fact]
    public void TestMissingFunctionParameterTypeAnnotationReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn(x) -> int { return x; };");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T105");
    }

    [Fact]
    public void TestReturnTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn() -> int { return true; };");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T104");
    }

    [Fact]
    public void TestInfixTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = 1 + true;");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T103");
    }

    [Fact]
    public void TestCallArgumentTypeMismatchReportsDiagnostic()
    {
        var input = "fn(x: int) -> int { return x; }(true);";
        var (_, _, result) = ParseResolveAndCheck(input);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
    }

    [Fact]
    public void TestArrayElementMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs: int[] = [1, true];");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T114");
    }

    [Fact]
    public void TestIndexExpressionType()
    {
        var (unit, _, result) = ParseResolveAndCheck("let xs: int[] = [1, 2, 3]; xs[0];");

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.Int, type);
    }

    [Fact]
    public void TestIndexExpressionTypeForStringArray()
    {
        var (unit, _, result) = ParseResolveAndCheck("let xs: string[] = [\"a\", \"b\"]; xs[1];");

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.String, type);
    }

    [Fact]
    public void TestIncludesNameResolutionDiagnostics()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = y;");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N001");
    }

    [Fact]
    public void TestNamedFunctionDeclarationWithImplicitVoidReturnType()
    {
        var (_, names, result) = ParseResolveAndCheck("fn Main() { } let x: int = 1; x;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T106");
    }

    [Fact]
    public void TestForwardReferenceAcrossNamedFunctionDeclarations()
    {
        var input = "fn A() -> int { B(); } fn B() -> int { 1; } A();";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCall()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.Abs(-42);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Int, callType);
    }

    [Fact]
    public void TestReportsUnsupportedStaticClrMethodReturnType()
    {
        var (_, _, result) = ParseResolveAndCheck("System.Guid.NewGuid();");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T122");
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCallViaImportAlias()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System.Math; Math.Abs(-42);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Int, callType);
        Assert.Equal("System.Math.Abs", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCallViaNamespaceImport()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System; Console.WriteLine(42);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Void, callType);
        Assert.Equal("System.Console.WriteLine", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCallWithTwoArguments()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.Max(10, 3);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Int, callType);
        Assert.Equal("System.Math.Max", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrFileCallViaNamespaceImport()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System.IO; File.Exists(\"/tmp/missing-file\");");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Bool, callType);
        Assert.Equal("System.IO.File.Exists", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrPathCombineViaNamespaceImport()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System.IO; Path.Combine(\"a\", \"b\");");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.String, callType);
        Assert.Equal("System.IO.Path.Combine", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrEnvironmentCallsViaNamespaceImport()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System; Environment.SetEnvironmentVariable(\"KONG_TEST\", \"ok\"); Environment.GetEnvironmentVariable(\"KONG_TEST\");");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var getCallStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var getCall = Assert.IsType<CallExpression>(getCallStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(getCall, out var getCallType));
        Assert.Equal(TypeSymbols.String, getCallType);
        Assert.Equal("System.Environment.GetEnvironmentVariable", result.ResolvedStaticMethodPaths[getCall]);
    }

    [Fact]
    public void TestTypeChecksStaticClrEnvironmentPropertyAccess()
    {
        var (unit, names, result) = ParseResolveAndCheck("import System; Environment.NewLine;");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var memberAccess = Assert.IsType<MemberAccessExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(memberAccess, out var memberType));
        Assert.Equal(TypeSymbols.String, memberType);
        Assert.Equal("System.Environment.NewLine", result.ResolvedStaticValuePaths[memberAccess]);
    }

    [Fact]
    public void TestTypeChecksStaticClrLongReturnType()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.BigMul(30000, 30000);");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.Equal(TypeSymbols.Long, result.ExpressionTypes[call]);
        Assert.Equal("System.Math.BigMul", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrDoubleReturnType()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Convert.ToDouble(\"4\");");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.Equal(TypeSymbols.Double, result.ExpressionTypes[call]);
        Assert.Equal("System.Convert.ToDouble", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrNumericWideningForOverloadResolution()
    {
        var source = "System.Math.Sqrt(9); System.Math.Max(System.Byte.Parse(\"7\"), 10);";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var sqrtStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var sqrtCall = Assert.IsType<CallExpression>(sqrtStatement.Expression);
        Assert.Equal(TypeSymbols.Double, result.ExpressionTypes[sqrtCall]);
        Assert.Equal("System.Math.Sqrt", result.ResolvedStaticMethodPaths[sqrtCall]);

        var maxStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var maxCall = Assert.IsType<CallExpression>(maxStatement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[maxCall]);
        Assert.Equal("System.Math.Max", result.ResolvedStaticMethodPaths[maxCall]);
    }

    [Fact]
    public void TestTypeChecksStaticClrParamsOverloadResolution()
    {
        var source = "System.String.Concat(\"a\", \"b\", \"c\", \"d\", \"e\");";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        Assert.Equal(TypeSymbols.String, result.ExpressionTypes[call]);
        Assert.Equal("System.String.Concat", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksDoubleCharAndByteLiterals()
    {
        var source = "let d: double = 1.5; let c: char = 'a'; let b: byte = 42b;";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrCharAndByteReturnTypes()
    {
        var source = "System.Char.Parse(\"a\"); System.Byte.Parse(\"42\");";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var charStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var charCall = Assert.IsType<CallExpression>(charStatement.Expression);
        Assert.Equal(TypeSymbols.Char, result.ExpressionTypes[charCall]);
        Assert.Equal("System.Char.Parse", result.ResolvedStaticMethodPaths[charCall]);

        var byteStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var byteCall = Assert.IsType<CallExpression>(byteStatement.Expression);
        Assert.Equal(TypeSymbols.Byte, result.ExpressionTypes[byteCall]);
        Assert.Equal("System.Byte.Parse", result.ResolvedStaticMethodPaths[byteCall]);
    }

    [Fact]
    public void TestAllowsWideningFromByteAndChar()
    {
        var source = "let fromByte: int = System.Byte.Parse(\"7\"); let fromChar: long = System.Char.Parse(\"A\");";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrStringMethods()
    {
        var source = "import System; String.IsNullOrEmpty(\"\"); String.Concat(\"a\", \"b\"); String.Equals(\"x\", \"x\");";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var isNullOrEmptyStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var isNullOrEmptyCall = Assert.IsType<CallExpression>(isNullOrEmptyStatement.Expression);
        Assert.Equal(TypeSymbols.Bool, result.ExpressionTypes[isNullOrEmptyCall]);
        Assert.Equal("System.String.IsNullOrEmpty", result.ResolvedStaticMethodPaths[isNullOrEmptyCall]);

        var concatStatement = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var concatCall = Assert.IsType<CallExpression>(concatStatement.Expression);
        Assert.Equal(TypeSymbols.String, result.ExpressionTypes[concatCall]);
        Assert.Equal("System.String.Concat", result.ResolvedStaticMethodPaths[concatCall]);

        var equalsStatement = Assert.IsType<ExpressionStatement>(unit.Statements[4]);
        var equalsCall = Assert.IsType<CallExpression>(equalsStatement.Expression);
        Assert.Equal(TypeSymbols.Bool, result.ExpressionTypes[equalsCall]);
        Assert.Equal("System.String.Equals", result.ResolvedStaticMethodPaths[equalsCall]);
    }

    [Fact]
    public void TestTypeChecksAdditionalStaticClrMathMethods()
    {
        var source = "System.Math.Min(10, 3); System.Math.Max(10, 3);";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var minStatement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var minCall = Assert.IsType<CallExpression>(minStatement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[minCall]);
        Assert.Equal("System.Math.Min", result.ResolvedStaticMethodPaths[minCall]);

        var maxStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var maxCall = Assert.IsType<CallExpression>(maxStatement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[maxCall]);
        Assert.Equal("System.Math.Max", result.ResolvedStaticMethodPaths[maxCall]);
    }

    [Fact]
    public void TestTypeChecksStaticClrMathClampMethod()
    {
        var source = "System.Math.Clamp(-4, 0, 10);";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[call]);
        Assert.Equal("System.Math.Clamp", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestReportsUnsupportedStaticClrMethodOverloadsWhenNoCompatibleSignatureExists()
    {
        var (_, _, result) = ParseResolveAndCheck("System.Math.DivRem(10, 3);");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T122");
    }

    private static (CompilationUnit Unit, NameResolution Names, TypeCheckResult Result) ParseResolveAndCheck(string input)
    {
        input = TestSourceUtilities.EnsureFileScopedNamespace(input);
        var lexer = new Lexer(input);
        var parser = new Parser(lexer);
        var unit = parser.ParseCompilationUnit();

        if (parser.Diagnostics.HasErrors)
        {
            var message = $"parser has {parser.Diagnostics.Count} errors\n";
            foreach (var diagnostic in parser.Diagnostics.All)
            {
                message += $"parser error: \"{diagnostic.Message}\"\n";
            }
            Assert.Fail(message);
        }

        var resolver = new NameResolver();
        var names = resolver.Resolve(unit);

        var checker = new TypeChecker();
        var result = checker.Check(unit, names);

        return (unit, names, result);
    }

}
