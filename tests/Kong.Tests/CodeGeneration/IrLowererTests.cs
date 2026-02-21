using Kong.CodeGeneration;
using Kong.Lexing;
using Kong.Parsing;
using Kong.Semantic;

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
        Assert.Contains(instructions, i => i is IrNewIntArray);
        Assert.Contains(instructions, i => i is IrIntArrayIndex);
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
        Assert.Contains(lowering.Program.EntryPoint.Blocks.SelectMany(b => b.Instructions), i => i is IrInvokeClosure);
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
        input = EnsureFileScopedNamespace(input);
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

    private static string EnsureFileScopedNamespace(string source)
    {
        if (source.Contains("namespace "))
        {
            return source;
        }

        var insertIndex = 0;
        while (true)
        {
            var remainder = source[insertIndex..].TrimStart();
            var skipped = source[insertIndex..].Length - remainder.Length;
            insertIndex += skipped;

            if (!source[insertIndex..].StartsWith("import "))
            {
                break;
            }

            var semicolonIndex = source.IndexOf(';', insertIndex);
            if (semicolonIndex < 0)
            {
                break;
            }

            insertIndex = semicolonIndex + 1;
        }

        return source.Insert(insertIndex, " namespace Test; ");
    }
}
