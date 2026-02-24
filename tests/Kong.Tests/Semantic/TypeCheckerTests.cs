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
        var (unit, names, result) = ParseResolveAndCheck("let x: int = 6 let y: int = x y");

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
        var (unit, names, result) = ParseResolveAndCheck("let x: long = 6 x");

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
        var input = "((x: int, y: int) => x + y)(1, 2)";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestInfersLetTypeFromInitializer()
    {
        var (unit, _, result) = ParseResolveAndCheck("let x = 6 x");

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
        var (_, _, result) = ParseResolveAndCheck("let x = if (true) { 1 } ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T119");
    }

    [Fact]
    public void TestCannotInferLetTypeFromEmptyArrayInitializer()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs = [] ");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T120");
    }

    [Fact]
    public void TestMissingFunctionParameterTypeAnnotationReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn Test(x) -> int { return x }");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T105");
    }

    [Fact]
    public void TestReturnTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("fn Test() -> int { return true }");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T104");
    }

    [Fact]
    public void TestInfixTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = 1 + true");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T103");
    }

    [Fact]
    public void TestLogicalOperatorsTypeCheckAsBool()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: bool = true && false let y: bool = true || false");

        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestLogicalOperatorTypeMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: bool = true && 1");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T103");
    }

    [Fact]
    public void TestCallArgumentTypeMismatchReportsDiagnostic()
    {
        var input = "((x: int) => x)(true)";
        var (_, _, result) = ParseResolveAndCheck(input);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
    }

    [Fact]
    public void TestArrayElementMismatchReportsDiagnostic()
    {
        var (_, _, result) = ParseResolveAndCheck("let xs: int[] = [1, true]");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T114");
    }

    [Fact]
    public void TestIndexExpressionType()
    {
        var (unit, _, result) = ParseResolveAndCheck("let xs: int[] = [1, 2, 3] xs[0]");

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.Int, type);
    }

    [Fact]
    public void TestIndexExpressionTypeForStringArray()
    {
        var (unit, _, result) = ParseResolveAndCheck("let xs: string[] = [\"a\", \"b\"] xs[1]");

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.String, type);
    }

    [Fact]
    public void TestIndexExpressionTypeForNestedIntArray()
    {
        var source = "let xss: int[][] = [[1, 2], [3, 4]] let ys: int[] = xss[1] ys[0]";
        var (unit, _, result) = ParseResolveAndCheck(source);

        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var indexExpression = Assert.IsType<IndexExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(indexExpression, out var type));
        Assert.Equal(TypeSymbols.Int, type);
    }

    [Fact]
    public void TestIncludesNameResolutionDiagnostics()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = y");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "N001");
    }

    [Fact]
    public void TestNamedFunctionDeclarationWithImplicitVoidReturnType()
    {
        var (_, names, result) = ParseResolveAndCheck("fn Main() { } let x: int = 1 x");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T106");
    }

    [Fact]
    public void TestForwardReferenceAcrossNamedFunctionDeclarations()
    {
        var input = "fn A() -> int { B() } fn B() -> int { 1 } A()";
        var (_, names, result) = ParseResolveAndCheck(input);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCall()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.Abs(-42)");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var expressionStatement = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var call = Assert.IsType<CallExpression>(expressionStatement.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        Assert.Equal(TypeSymbols.Int, callType);
    }

    [Fact]
    public void TestTypeChecksEnumVariantConstructorCall()
    {
        var source = "enum Result { Ok(int), Err(string) } let r: Result = Ok(42)";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var letStatement = Assert.IsType<LetStatement>(unit.Statements[2]);
        var call = Assert.IsType<CallExpression>(letStatement.Value);
        Assert.True(result.ExpressionTypes.TryGetValue(call, out var callType));
        var enumType = Assert.IsType<EnumTypeSymbol>(callType);
        Assert.Equal("Result", enumType.EnumName);
        Assert.Empty(enumType.TypeArguments);
        Assert.True(result.ResolvedEnumVariantConstructions.ContainsKey(call));
    }

    [Fact]
    public void TestReportsEnumVariantConstructorArgumentMismatch()
    {
        var (_, _, result) = ParseResolveAndCheck("enum Result { Ok(int), Err(string) } Ok(\"nope\")");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
    }

    [Fact]
    public void TestReportsEnumVariantIdentifierWithoutConstructorCall()
    {
        var (_, _, result) = ParseResolveAndCheck("enum Result { Ok(int), Err(string) } Ok");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T128");
    }

    [Fact]
    public void TestTypeChecksEnumMatchExpression()
    {
        var source = "enum Result { Ok(int), Err(string) } let v: Result = Ok(1) let x: int = match v { Ok(n) => n, Err(msg) => 0 }";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var letX = Assert.IsType<LetStatement>(unit.Statements[3]);
        var match = Assert.IsType<MatchExpression>(letX.Value);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[match]);
        Assert.True(result.ResolvedMatches.ContainsKey(match));
    }

    [Fact]
    public void TestReportsNonExhaustiveEnumMatchExpression()
    {
        var (_, _, result) = ParseResolveAndCheck("enum Result { Ok(int), Err(string) } let v: Result = Ok(1) let x: int = match v { Ok(n) => n }");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T131");
    }

    [Fact]
    public void TestTypeChecksGenericEnumVariantConstructorWithContext()
    {
        var source = "enum Result<T, E> { Ok(T), Err(E) } let r: Result<int, string> = Ok(42)";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsGenericEnumVariantConstructorWithoutEnoughContext()
    {
        var (_, _, result) = ParseResolveAndCheck("enum Result<T, E> { Ok(T), Err(E) } Ok(42)");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T130");
    }

    [Fact]
    public void TestTypeChecksGenericEnumMatchPayloadBindings()
    {
        var source = "enum Result<T, E> { Ok(T), Err(E) } let r: Result<int, string> = Ok(7) let n: int = match r { Ok(v) => v, Err(e) => 0 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksClassImplConstructorAndMethodSelfAccess()
    {
        var source = "class User { name: string age: int } impl User { init(name: string, age: int) { self.name = name self.age = age } fn Age(self) -> int { self.age } }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsInterfaceImplMissingMethod()
    {
        var source = "class User { name: string } interface IGreeter { fn Greet(self) } impl IGreeter for User { }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T133");
    }

    [Fact]
    public void TestReportsMethodWithoutSelfParameter()
    {
        var source = "class User { name: string } impl User { fn Greet() { } }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T134");
    }

    [Fact]
    public void TestReportsClassFieldAssignmentTypeMismatchInConstructor()
    {
        var source = "class User { age: int } impl User { init(age: string) { self.age = age } }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T102");
    }

    [Fact]
    public void TestTypeChecksInterfaceTypedVariableRuntimeValue()
    {
        var source = "class User { age: int } interface IHasAge { fn Age(self) -> int } impl IHasAge for User { fn Age(self) -> int { self.age } } let user = new User() let v: IHasAge = user";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksInterfaceTypedFunctionParameterAndReturn()
    {
        var source = "interface IGreeter { fn Greet(self) } fn Use(x: IGreeter) -> IGreeter { x }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksInterfaceTypedClassField()
    {
        var source = "interface IGreeter { fn Greet(self) } class Holder { inner: IGreeter }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsPublicModifierInsideInterfaceImplMethod()
    {
        var source = "class User { } interface IGreeter { fn Greet(self) } impl IGreeter for User { public fn Greet(self) { } }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T137");
    }

    [Fact]
    public void TestTypeChecksGenericClassConstructorAndMethodCall()
    {
        var source = "class Box<T> { value: T } impl Box { init(value: T) { self.value = value } fn Get(self) -> T { self.value } } let b: Box<int> = new Box<int>(42) let n: int = b.Get()";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksGenericInterfaceAssignmentAndDispatchTyping()
    {
        var source = "class Box<T> { value: T } interface IGet<T> { fn Get(self) -> T } impl Box { init(value: T) { self.value = value } } impl IGet for Box { fn Get(self) -> T { self.value } } let b: Box<int> = new Box<int>(7) let g: IGet<int> = b let n: int = g.Get()";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksGenericFunctionInference()
    {
        var source = "fn Id<T>(value: T) -> T { value } let n: int = Id(3)";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsGenericClassMissingTypeArgumentsInAnnotation()
    {
        var source = "class Box<T> { value: T } let b: Box = new Box<int>(1)";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T001");
    }

    [Fact]
    public void TestReportsUnsupportedStaticClrMethodReturnType()
    {
        var (_, _, result) = ParseResolveAndCheck("System.Guid.NewGuid()");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T122");
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodCallViaImportAlias()
    {
        var (unit, names, result) = ParseResolveAndCheck("use System.Math Math.Abs(-42)");

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
        var (unit, names, result) = ParseResolveAndCheck("use System Console.WriteLine(42)");

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
        var (unit, names, result) = ParseResolveAndCheck("System.Math.Max(10, 3)");

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
        var (unit, names, result) = ParseResolveAndCheck("use System.IO File.Exists(\"/tmp/missing-file\")");

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
        var (unit, names, result) = ParseResolveAndCheck("use System.IO Path.Combine(\"a\", \"b\")");

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
        var (unit, names, result) = ParseResolveAndCheck("use System Environment.SetEnvironmentVariable(\"KONG_TEST\", \"ok\") Environment.GetEnvironmentVariable(\"KONG_TEST\")");

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
        var (unit, names, result) = ParseResolveAndCheck("use System Environment.NewLine");

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
        var (unit, names, result) = ParseResolveAndCheck("System.Math.BigMul(30000, 30000)");

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
        var (unit, names, result) = ParseResolveAndCheck("System.Convert.ToDouble(\"4\")");

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
        var source = "System.Math.Sqrt(9) System.Math.Max(System.Byte.Parse(\"7\"), 10)";
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
        var source = "System.String.Concat(\"a\", \"b\", \"c\", \"d\", \"e\")";
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
        var source = "let d: double = 1.5 let c: char = 'a' let b: byte = 42b";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrCharAndByteReturnTypes()
    {
        var source = "System.Char.Parse(\"a\") System.Byte.Parse(\"42\")";
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
        var source = "let fromByte: int = System.Byte.Parse(\"7\") let fromChar: long = System.Char.Parse(\"A\")";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksStaticClrStringMethods()
    {
        var source = "use System String.IsNullOrEmpty(\"\") String.Concat(\"a\", \"b\") String.Equals(\"x\", \"x\")";
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
        var source = "System.Math.Min(10, 3) System.Math.Max(10, 3)";
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
        var source = "System.Math.Clamp(-4, 0, 10)";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[call]);
        Assert.Equal("System.Math.Clamp", result.ResolvedStaticMethodPaths[call]);
    }

    [Fact]
    public void TestTypeChecksStaticClrMethodReturningGenericTuple()
    {
        var (unit, names, result) = ParseResolveAndCheck("System.Math.DivRem(10, 3)");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var statement = Assert.IsType<ExpressionStatement>(unit.Statements[1]);
        var call = Assert.IsType<CallExpression>(statement.Expression);
        var tupleType = Assert.IsType<ClrGenericTypeSymbol>(result.ExpressionTypes[call]);
        Assert.Equal("System.ValueTuple`2", tupleType.GenericTypeName);
        Assert.Equal(2, tupleType.TypeArguments.Count);
        Assert.Equal(TypeSymbols.Int, tupleType.TypeArguments[0]);
        Assert.Equal(TypeSymbols.Int, tupleType.TypeArguments[1]);
    }

    [Fact]
    public void TestTypeChecksInstanceClrStringMethodsAndProperties()
    {
        var source = "let s: string = \" hello \" s.Trim() s.Contains(\"ell\") s.Length";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var trimStatement = Assert.IsType<ExpressionStatement>(unit.Statements[2]);
        var trimCall = Assert.IsType<CallExpression>(trimStatement.Expression);
        Assert.Equal(TypeSymbols.String, result.ExpressionTypes[trimCall]);
        Assert.Equal("Trim", result.ResolvedInstanceMethodMembers[trimCall]);

        var containsStatement = Assert.IsType<ExpressionStatement>(unit.Statements[3]);
        var containsCall = Assert.IsType<CallExpression>(containsStatement.Expression);
        Assert.Equal(TypeSymbols.Bool, result.ExpressionTypes[containsCall]);
        Assert.Equal("Contains", result.ResolvedInstanceMethodMembers[containsCall]);

        var lengthStatement = Assert.IsType<ExpressionStatement>(unit.Statements[4]);
        var lengthAccess = Assert.IsType<MemberAccessExpression>(lengthStatement.Expression);
        Assert.Equal(TypeSymbols.Int, result.ExpressionTypes[lengthAccess]);
        Assert.Equal("Length", result.ResolvedInstanceValueMembers[lengthAccess]);
    }

    [Fact]
    public void TestTypeChecksConstructorInteropForClrTypes()
    {
        var source = "use System.Text let sb = new StringBuilder() sb.Append(\"x\") sb.ToString()";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var toStringStatement = Assert.IsType<ExpressionStatement>(unit.Statements[4]);
        var toStringCall = Assert.IsType<CallExpression>(toStringStatement.Expression);
        Assert.Equal(TypeSymbols.String, result.ExpressionTypes[toStringCall]);
        Assert.Equal("ToString", result.ResolvedInstanceMethodMembers[toStringCall]);
    }

    [Fact]
    public void TestReportsUnsupportedConstructorSignaturesClearly()
    {
        var (_, _, result) = ParseResolveAndCheck("new System.WeakReference(\"x\")");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
        Assert.Contains(result.Diagnostics.All, d => d.Message.Contains("no matching constructor overload", StringComparison.Ordinal));
    }

    [Fact]
    public void TestAllowsAssignmentToVar()
    {
        var (_, names, result) = ParseResolveAndCheck("var x: int = 1 x = x + 1");

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestRejectsAssignmentToLet()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = 1 x = 2");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T123");
    }

    [Fact]
    public void TestTypeChecksStaticClrOutArgumentCall()
    {
        var source = "use System var value: bool = false let ok: bool = Boolean.TryParse(\"true\", out value)";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestRejectsOutArgumentOnImmutableLet()
    {
        var (_, _, result) = ParseResolveAndCheck("use System let value: bool = false Boolean.TryParse(\"true\", out value)");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T123");
    }

    [Fact]
    public void TestTypeChecksAdditionalPrimitiveClrTypes()
    {
        var source = "System.SByte.Parse(\"1\") System.Int16.Parse(\"2\") System.UInt16.Parse(\"3\") System.UInt32.Parse(\"4\") System.UInt64.Parse(\"5\") System.IntPtr.Zero System.UIntPtr.Zero System.Convert.ToSingle(\"6\") System.Decimal.Parse(\"7\")";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        Assert.Equal(TypeSymbols.SByte, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[1]).Expression)]);
        Assert.Equal(TypeSymbols.Short, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[2]).Expression)]);
        Assert.Equal(TypeSymbols.UShort, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[3]).Expression)]);
        Assert.Equal(TypeSymbols.UInt, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[4]).Expression)]);
        Assert.Equal(TypeSymbols.ULong, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[5]).Expression)]);
        Assert.Equal(TypeSymbols.NInt, result.ExpressionTypes[Assert.IsType<MemberAccessExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[6]).Expression)]);
        Assert.Equal(TypeSymbols.NUInt, result.ExpressionTypes[Assert.IsType<MemberAccessExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[7]).Expression)]);
        Assert.Equal(TypeSymbols.Float, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[8]).Expression)]);
        Assert.Equal(TypeSymbols.Decimal, result.ExpressionTypes[Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(unit.Statements[9]).Expression)]);
    }

    [Fact]
    public void TestTypeChecksForInLoopOverArray()
    {
        var source = "let xs: int[] = [1, 2, 3] for i in xs { let y: int = i }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestTypeChecksWhileLoopWithBoolCondition()
    {
        var source = "var i: int = 0 while i < 3 { i = i + 1 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsWhileLoopRequiresBoolCondition()
    {
        var source = "while 1 { 1 }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T109");
    }

    [Fact]
    public void TestReportsForInLoopRequiresArrayIterable()
    {
        var source = "let x: int = 1 for i in x { i }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T125");
    }

    [Fact]
    public void TestTypeChecksArrayElementAssignment()
    {
        var source = "var xs: int[] = [1, 2, 3] xs[1] = 42";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsBreakContinueOutsideLoop()
    {
        var (_, _, result) = ParseResolveAndCheck("break continue");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T126");
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T127");
    }

    [Fact]
    public void TestTypeChecksBreakContinueInsideWhileLoop()
    {
        var (_, _, result) = ParseResolveAndCheck("while true { break continue }");

        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T126");
        Assert.DoesNotContain(result.Diagnostics.All, d => d.Code == "T127");
    }

    [Fact]
    public void TestTypeChecksLoopExpressionWithBreakValue()
    {
        var source = "let x: int = loop { break 42 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void TestReportsBreakValueOutsideLoopExpression()
    {
        var (_, _, result) = ParseResolveAndCheck("let x: int = loop { while true { break 1 } break 0 }");

        Assert.Contains(result.Diagnostics.All, d => d.Code == "T136");
    }

    [Fact]
    public void TestReportsMismatchedLoopBreakTypes()
    {
        var source = "let x = loop { if (true) { break 1 } else { break true } }";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.Contains(result.Diagnostics.All, d => d.Code == "T137");
    }

    [Fact]
    public void TestTypeChecksLinqExtensionMethodChain()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3, 4, 5] let processed = numbers.Where((n: int) => n > 2).Select((n: int) => n * n).OrderByDescending((n: int) => n).ToList() processed";
        var (unit, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);

        var processedBinding = Assert.IsType<ExpressionStatement>(unit.Statements.Last());
        var processedIdentifier = Assert.IsType<Identifier>(processedBinding.Expression);
        Assert.True(result.ExpressionTypes.TryGetValue(processedIdentifier, out var processedType));
        Assert.IsType<ClrGenericTypeSymbol>(processedType);
        Assert.Equal(4, result.ResolvedExtensionMethodPaths.Count);
        Assert.All(result.ResolvedExtensionMethodPaths.Values, path => Assert.StartsWith("System.Linq.Enumerable.", path, StringComparison.Ordinal));
    }

    [Fact]
    public void TestReportsLinqPredicateTypeMismatch()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3] numbers.Where((n: int) => n + 1)";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113" || d.Code == "T122");
    }

    [Fact]
    public void TestTypeChecksLinqAnyAndAllExtensions()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3, 4] let hasAny = numbers.Any() let allPositive = numbers.All((n: int) => n > 0) if (hasAny && allPositive) { 1 } else { 0 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Equal(2, result.ResolvedExtensionMethodPaths.Count);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Any");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.All");
    }

    [Fact]
    public void TestTypeChecksLinqGroupByExtension()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3, 4, 5] let grouped = numbers.GroupBy((n: int) => n > 2) let count: int = Enumerable.Count(grouped) count";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.GroupBy");
    }

    [Fact]
    public void TestTypeChecksLinqThenByDescendingExtension()
    {
        var source = "use System.Linq let numbers: int[] = [21, 11, 12, 22, 13] let sorted = numbers.OrderBy((n: int) => n / 10).ThenByDescending((n: int) => n).ToList() let first: int = Enumerable.First(sorted) first";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.OrderBy");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.ThenByDescending");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.ToList");
    }

    [Fact]
    public void TestTypeChecksLinqSelectManyExtension()
    {
        var source = "use System.Linq let groups: int[][] = [[1, 2], [3, 4, 5]] let flattened = groups.SelectMany((g: int[]) => g) let count: int = Enumerable.Count(flattened) count";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.SelectMany");
    }

    [Fact]
    public void TestTypeChecksLinqDistinctAndUnionExtensions()
    {
        var source = "use System.Linq let left: int[] = [1, 2, 2, 3] let right: int[] = [3, 4] let unique = left.Distinct().Union(right) let count: int = Enumerable.Count(unique) count";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Distinct");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Union");
    }

    [Fact]
    public void TestTypeChecksLinqMaxAndMinExtensions()
    {
        var source = "use System.Linq let numbers: int[] = [5, 2, 8, 1, 9] let max: int = numbers.Max() let min: int = numbers.Min() max - min";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Max");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Min");
    }

    [Fact]
    public void TestTypeChecksLinqSumAndAverageExtensions()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3, 4, 5] let sum: int = numbers.Sum() let avg: double = numbers.Average() if (sum == 15) { if (avg == 3.0) { 1 } else { 0 } } else { 0 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Sum");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Average");
    }

    [Fact]
    public void TestTypeChecksLinqSumAndAverageSelectorExtensions()
    {
        var source = "use System.Linq let numbers: int[] = [1, 2, 3, 4] let sum: int = numbers.Sum((n: int) => n * 2) let avg: double = numbers.Average((n: int) => n * 2) if (sum == 20) { if (avg == 5.0) { 1 } else { 0 } } else { 0 }";
        var (_, names, result) = ParseResolveAndCheck(source);

        Assert.False(names.Diagnostics.HasErrors);
        Assert.False(result.Diagnostics.HasErrors);
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Sum");
        Assert.Contains(result.ResolvedExtensionMethodPaths.Values, path => path == "System.Linq.Enumerable.Average");
    }

    [Fact]
    public void TestReportsNoMatchingLinqAggregateOverloadsForInvalidArguments()
    {
        var source = "use System.Linq let left: int[] = [1, 2] let right: int[] = [2, 3] left.Union(right, 1)";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
    }

    [Fact]
    public void TestReportsNoMatchingLinqAverageOverloadForNonNumericSequence()
    {
        var source = "use System.Linq let flags: bool[] = [true, false] flags.Average()";
        var (_, _, result) = ParseResolveAndCheck(source);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.All, d => d.Code == "T113");
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
