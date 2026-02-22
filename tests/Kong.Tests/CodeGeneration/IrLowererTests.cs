using Kong.CodeGeneration;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;
using Kong.Tests;

namespace Kong.Tests.CodeGeneration;

public class IrLowererTests
{
    [Fact]
    public void TestLowersSimpleAdditionExpression()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("1 + 1;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);

        var entry = lowering.Program!.EntryPoint;
        Assert.Equal("__main", entry.Name);
        Assert.Equal(TypeSymbols.Int, entry.ReturnType);
        Assert.Single(entry.Blocks);

        var block = entry.Blocks[0];
        Assert.Collection(block.Instructions,
            i => Assert.IsType<IrConstInt>(i),
            i => Assert.IsType<IrConstInt>(i),
            i => Assert.IsType<IrBinary>(i));
        Assert.IsType<IrReturn>(block.Terminator);
    }

    [Fact]
    public void TestLowersLetBindingAndIdentifierUsage()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let x = 2; x + 3;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);

        var entry = lowering.Program!.EntryPoint;
        Assert.Single(entry.LocalTypes);
        Assert.Single(entry.Blocks);
        Assert.Contains(entry.Blocks[0].Instructions, i => i is IrStoreLocal);
        Assert.Contains(entry.Blocks[0].Instructions, i => i is IrLoadLocal);
    }

    [Fact]
    public void TestLowersIfExpressionIntoBranchBlocks()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("if (true) { 10 } else { 20 };");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);

        var entry = lowering.Program!.EntryPoint;
        Assert.True(entry.Blocks.Count >= 4);
        Assert.Contains(entry.Blocks, b => b.Terminator is IrBranch);
    }

    [Fact]
    public void TestLowersLogicalOperatorsWithShortCircuitBranches()
    {
        var source = "let a: bool = false && true; let b: bool = true || false; if (a == b) { 1 } else { 0 };";
        var (unit, typeCheck) = ParseAndTypeCheck(source);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var branches = lowering.Program!.EntryPoint.Blocks.Count(b => b.Terminator is IrBranch);
        Assert.True(branches >= 3);
    }

    [Fact]
    public void TestLowersFunctionLiteralCall()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("fn(x: int) -> int { return x + 1; }(2);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        Assert.Single(lowering.Program!.Functions);
        Assert.Contains(lowering.Program.EntryPoint.Blocks[0].Instructions, i => i is IrInvokeClosure);
    }

    [Fact]
    public void TestLowersArrayLiteralAndIndexExpression()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let xs: int[] = [1, 2, 3]; xs[1];");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var instructions = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instructions, i => i is IrNewArray { ElementType: IntTypeSymbol });
        Assert.Contains(instructions, i => i is IrArrayIndex { ElementType: IntTypeSymbol });
    }

    [Fact]
    public void TestLowersStringArrayLiteralAndIndexExpression()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let xs: string[] = [\"a\", \"b\"]; let second: string = xs[1]; 1;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var instructions = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instructions, i => i is IrNewArray { ElementType: StringTypeSymbol });
        Assert.Contains(instructions, i => i is IrArrayIndex { ElementType: StringTypeSymbol });
    }

    [Fact]
    public void TestLowersNestedIntArrayLiteralAndIndexExpression()
    {
        var source = "let xss: int[][] = [[1, 2], [3, 4]]; let ys: int[] = xss[1]; ys[0];";
        var (unit, typeCheck) = ParseAndTypeCheck(source);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var instructions = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instructions, i => i is IrNewArray { ElementType: ArrayTypeSymbol { ElementType: IntTypeSymbol } });
        Assert.Contains(instructions, i => i is IrArrayIndex { ElementType: ArrayTypeSymbol { ElementType: IntTypeSymbol } });
        Assert.Contains(instructions, i => i is IrArrayIndex { ElementType: IntTypeSymbol });
    }

    [Fact]
    public void TestLowersStaticClrCallVoid()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("System.Console.WriteLine(1);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrStaticCallVoid>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticCallVoid));
        Assert.Equal("System.Console.WriteLine", call.MethodPath);
    }

    [Fact]
    public void TestLowersStaticClrMethodCall()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("System.Math.Abs(-42);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrStaticCall>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticCall));
        Assert.Equal("System.Math.Abs", call.MethodPath);
        Assert.Single(call.Arguments);
        Assert.Single(call.ArgumentTypes);
        Assert.Equal(TypeSymbols.Int, call.ArgumentTypes[0]);
    }

    [Fact]
    public void TestLowersImportedStaticClrMethodCallToQualifiedPath()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("import System.Math; Math.Abs(-42);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrStaticCall>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticCall));
        Assert.Equal("System.Math.Abs", call.MethodPath);
    }

    [Fact]
    public void TestLowersNamespaceImportedStaticClrCallToQualifiedPath()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("import System; Console.WriteLine(1);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrStaticCallVoid>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticCallVoid));
        Assert.Equal("System.Console.WriteLine", call.MethodPath);
    }

    [Fact]
    public void TestLowersTwoArgumentStaticClrMethodCall()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("System.Math.Max(10, 3);");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrStaticCall>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticCall));
        Assert.Equal("System.Math.Max", call.MethodPath);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(2, call.ArgumentTypes.Count);
        Assert.All(call.ArgumentTypes, t => Assert.Equal(TypeSymbols.Int, t));
    }

    [Fact]
    public void TestLowersStaticClrPropertyAccess()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("import System; if (Environment.NewLine != \"\") { 1; } else { 0; }");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var get = Assert.IsType<IrStaticValueGet>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrStaticValueGet));
        Assert.Equal("System.Environment.NewLine", get.MemberPath);
    }

    [Fact]
    public void TestLowersInstanceClrMethodCall()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let s: string = \" hello \"; s.Trim(); 1;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var call = Assert.IsType<IrInstanceCall>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrInstanceCall));
        Assert.Equal(TypeSymbols.String, call.ReceiverType);
        Assert.Equal("Trim", call.MemberName);
    }

    [Fact]
    public void TestLowersInstanceClrPropertyAccess()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let s: string = \"abc\"; s.Length; 1;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var get = Assert.IsType<IrInstanceValueGet>(lowering.Program!.EntryPoint.Blocks[0].Instructions.Last(i => i is IrInstanceValueGet));
        Assert.Equal(TypeSymbols.String, get.ReceiverType);
        Assert.Equal("Length", get.MemberName);
    }

    [Fact]
    public void TestLowersStaticClrDoubleCharAndByteCalls()
    {
        var source = "let d: double = System.Convert.ToDouble(\"4\"); let c: char = System.Char.Parse(\"A\"); let b: byte = System.Byte.Parse(\"42\"); 1;";
        var (unit, typeCheck) = ParseAndTypeCheck(source);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var staticCalls = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).OfType<IrStaticCall>().ToList();
        Assert.Contains(staticCalls, c => c.MethodPath == "System.Convert.ToDouble");
        Assert.Contains(staticCalls, c => c.MethodPath == "System.Char.Parse");
        Assert.Contains(staticCalls, c => c.MethodPath == "System.Byte.Parse");
    }

    [Fact]
    public void TestLowersDoubleCharAndByteLiterals()
    {
        var source = "let d: double = 1.5; let c: char = 'a'; let b: byte = 42b; 1;";
        var (unit, typeCheck) = ParseAndTypeCheck(source);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        var instructions = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instructions, i => i is IrConstDouble);
        Assert.Contains(instructions, i => i is IrConstInt);
    }

    [Fact]
    public void TestLowersClosureCallWithCapturedVariable()
    {
        var input = "let f = fn(outer: int) -> int { let g = fn(x: int) -> int { x + outer }; g(5); }; f(10);";
        var (unit, typeCheck, names) = ParseAndTypeCheckWithNames(input);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck, names);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);

        Assert.Equal(2, lowering.Program!.Functions.Count);
        var loweredFunction = Assert.Single(lowering.Program.Functions, f => f.Parameters.Count == 2);
        Assert.Equal(2, loweredFunction.Parameters.Count);
        Assert.Equal("outer", loweredFunction.Parameters[0].Name);
        Assert.Equal("x", loweredFunction.Parameters[1].Name);
    }

    [Fact]
    public void TestLowersNestedClosureCalls()
    {
        var input = "let f = fn(x: int) -> int { let g = fn(y: int) -> int { x + y }; g(7); }; f(5);";
        var (unit, typeCheck, names) = ParseAndTypeCheckWithNames(input);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck, names);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        Assert.Equal(2, lowering.Program!.Functions.Count);
    }

    [Fact]
    public void TestLowersEscapingClosureValueAndInvoke()
    {
        var input = "let makeAdder: fn(int) -> fn(int) -> int = fn(x: int) -> fn(int) -> int { fn(y: int) -> int { x + y } }; let addTwo: fn(int) -> int = makeAdder(2); addTwo(40);";
        var (unit, typeCheck, names) = ParseAndTypeCheckWithNames(input);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck, names);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);

        var instructions = lowering.Program!.EntryPoint.Blocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instructions, i => i is IrCreateClosure);
        Assert.Contains(instructions, i => i is IrInvokeClosure);
    }

    [Fact]
    public void TestLowersNamedFunctionDeclarationAndCall()
    {
        var input = "fn Add(x: int, y: int) -> int { x + y; } Add(1, 2);";
        var (unit, typeCheck, names) = ParseAndTypeCheckWithNames(input);
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck, names);

        Assert.NotNull(lowering.Program);
        Assert.False(lowering.Diagnostics.HasErrors);
        Assert.Single(lowering.Program!.Functions);
        Assert.Contains(lowering.Program.EntryPoint.Blocks.SelectMany(b => b.Instructions), i => i is IrCreateClosure);
        Assert.Contains(lowering.Program.EntryPoint.Blocks.SelectMany(b => b.Instructions), i => i is IrCall { FunctionName: "Add" });
    }

    [Fact]
    public void TestLowererRejectsNonIntTopLevelExpression()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("true;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.Null(lowering.Program);
        Assert.Contains(lowering.Diagnostics.All, d => d.Code == "IR001");
    }

    private static (CompilationUnit Unit, TypeCheckResult TypeCheck) ParseAndTypeCheck(string input)
    {
        var (unit, typeCheck, _) = ParseAndTypeCheckWithNames(input);
        return (unit, typeCheck);
    }

    private static (CompilationUnit Unit, TypeCheckResult TypeCheck, NameResolution Names) ParseAndTypeCheckWithNames(string input)
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
        var typeCheck = checker.Check(unit, names);

        if (typeCheck.Diagnostics.HasErrors)
        {
            var message = $"type checker has {typeCheck.Diagnostics.Count} errors\n";
            foreach (var diagnostic in typeCheck.Diagnostics.All)
            {
                message += $"type checker error: \"{diagnostic.Message}\" [{diagnostic.Code}]\n";
            }

            Assert.Fail(message);
        }

        return (unit, typeCheck, names);
    }

}
