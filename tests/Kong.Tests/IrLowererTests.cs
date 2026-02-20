namespace Kong.Tests;

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
    public void TestLowererRejectsTopLevelLetStatement()
    {
        var (unit, typeCheck) = ParseAndTypeCheck("let x = 1;");
        var lowerer = new IrLowerer();

        var lowering = lowerer.Lower(unit, typeCheck);

        Assert.Null(lowering.Program);
        Assert.Contains(lowering.Diagnostics.All, d => d.Code == "IR001");
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

        return (unit, typeCheck);
    }
}
