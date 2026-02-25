using Kong.CodeGeneration;

namespace Kong.Tests;

public class SymbolTableTests
{
    [Fact]
    public void TestDefine()
    {
        var expected = new Dictionary<string, Symbol>
        {
            { "a", new Symbol("a", SymbolScope.Global, 0) },
            { "b", new Symbol("b", SymbolScope.Global, 1) },
            { "c", new Symbol("c", SymbolScope.Local, 0) },
            { "d", new Symbol("d", SymbolScope.Local, 1) },
            { "e", new Symbol("e", SymbolScope.Local, 0) },
            { "f", new Symbol("f", SymbolScope.Local, 1) },
        };

        var global = SymbolTable.NewSymbolTable();

        var a = global.Define("a");
        Assert.Equal(expected["a"], a);

        var b = global.Define("b");
        Assert.Equal(expected["b"], b);

        var firstLocal = SymbolTable.NewEnclosedSymbolTable(global);

        var c = firstLocal.Define("c");
        Assert.Equal(expected["c"], c);

        var d = firstLocal.Define("d");
        Assert.Equal(expected["d"], d);

        var secondLocal = SymbolTable.NewEnclosedSymbolTable(firstLocal);

        var e = secondLocal.Define("e");
        Assert.Equal(expected["e"], e);

        var f = secondLocal.Define("f");
        Assert.Equal(expected["f"], f);
    }

    [Fact]
    public void TestResolveGlobal()
    {
        var global = SymbolTable.NewSymbolTable();
        global.Define("a");
        global.Define("b");

        var expected = new Symbol[]
        {
            new("a", SymbolScope.Global, 0),
            new("b", SymbolScope.Global, 1),
        };

        foreach (var sym in expected)
        {
            var (result, ok) = global.Resolve(sym.Name);
            Assert.True(ok, $"name {sym.Name} not resolvable");
            Assert.Equal(sym, result);
        }
    }

    [Fact]
    public void TestResolveLocal()
    {
        var global = SymbolTable.NewSymbolTable();
        global.Define("a");
        global.Define("b");

        var local = SymbolTable.NewEnclosedSymbolTable(global);
        local.Define("c");
        local.Define("d");

        var expected = new Symbol[]
        {
            new("a", SymbolScope.Global, 0),
            new("b", SymbolScope.Global, 1),
            new("c", SymbolScope.Local, 0),
            new("d", SymbolScope.Local, 1),
        };

        foreach (var sym in expected)
        {
            var (result, ok) = local.Resolve(sym.Name);
            Assert.True(ok, $"name {sym.Name} not resolvable");
            Assert.Equal(sym, result);
        }
    }

    [Fact]
    public void TestResolveNestedLocal()
    {
        var global = SymbolTable.NewSymbolTable();
        global.Define("a");
        global.Define("b");

        var firstLocal = SymbolTable.NewEnclosedSymbolTable(global);
        firstLocal.Define("c");
        firstLocal.Define("d");

        var secondLocal = SymbolTable.NewEnclosedSymbolTable(firstLocal);
        secondLocal.Define("e");
        secondLocal.Define("f");

        var tests = new (SymbolTable table, Symbol[] expectedSymbols)[]
        {
            (firstLocal, [
                new("a", SymbolScope.Global, 0),
                new("b", SymbolScope.Global, 1),
                new("c", SymbolScope.Local, 0),
                new("d", SymbolScope.Local, 1),
            ]),
            (secondLocal, [
                new("a", SymbolScope.Global, 0),
                new("b", SymbolScope.Global, 1),
                new("e", SymbolScope.Local, 0),
                new("f", SymbolScope.Local, 1),
            ]),
        };

        foreach (var (table, expectedSymbols) in tests)
        {
            foreach (var sym in expectedSymbols)
            {
                var (result, ok) = table.Resolve(sym.Name);
                Assert.True(ok, $"name {sym.Name} not resolvable");
                Assert.Equal(sym, result);
            }
        }
    }

    [Fact]
    public void TestDefineResolveBuiltins()
    {
        var global = SymbolTable.NewSymbolTable();
        var firstLocal = SymbolTable.NewEnclosedSymbolTable(global);
        var secondLocal = SymbolTable.NewEnclosedSymbolTable(firstLocal);

        var expected = new Symbol[]
        {
            new("a", SymbolScope.Builtin, 0),
            new("c", SymbolScope.Builtin, 1),
            new("e", SymbolScope.Builtin, 2),
            new("f", SymbolScope.Builtin, 3),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            global.DefineBuiltin(i, expected[i].Name);
        }

        foreach (var table in new[] { global, firstLocal, secondLocal })
        {
            foreach (var sym in expected)
            {
                var (result, ok) = table.Resolve(sym.Name);
                Assert.True(ok, $"name {sym.Name} not resolvable");
                Assert.Equal(sym, result);
            }
        }
    }

    [Fact]
    public void TestResolveFree()
    {
        var global = SymbolTable.NewSymbolTable();
        global.Define("a");
        global.Define("b");

        var firstLocal = SymbolTable.NewEnclosedSymbolTable(global);
        firstLocal.Define("c");
        firstLocal.Define("d");

        var secondLocal = SymbolTable.NewEnclosedSymbolTable(firstLocal);
        secondLocal.Define("e");
        secondLocal.Define("f");

        var tests = new (SymbolTable table, Symbol[] expectedSymbols, Symbol[] expectedFreeSymbols)[]
        {
            (firstLocal, [
                new("a", SymbolScope.Global, 0),
                new("b", SymbolScope.Global, 1),
                new("c", SymbolScope.Local, 0),
                new("d", SymbolScope.Local, 1),
            ], []),
            (secondLocal, [
                new("a", SymbolScope.Global, 0),
                new("b", SymbolScope.Global, 1),
                new("c", SymbolScope.Free, 0),
                new("d", SymbolScope.Free, 1),
                new("e", SymbolScope.Local, 0),
                new("f", SymbolScope.Local, 1),
            ], [
                new("c", SymbolScope.Local, 0),
                new("d", SymbolScope.Local, 1),
            ]),
        };

        foreach (var (table, expectedSymbols, expectedFreeSymbols) in tests)
        {
            foreach (var sym in expectedSymbols)
            {
                var (result, ok) = table.Resolve(sym.Name);
                Assert.True(ok, $"name {sym.Name} not resolvable");
                Assert.Equal(sym, result);
            }

            Assert.Equal(expectedFreeSymbols.Length, table.FreeSymbols.Count);

            for (var i = 0; i < expectedFreeSymbols.Length; i++)
            {
                Assert.Equal(expectedFreeSymbols[i], table.FreeSymbols[i]);
            }
        }
    }

    [Fact]
    public void TestResolveUnresolvableFree()
    {
        var global = SymbolTable.NewSymbolTable();
        global.Define("a");

        var firstLocal = SymbolTable.NewEnclosedSymbolTable(global);
        firstLocal.Define("c");

        var secondLocal = SymbolTable.NewEnclosedSymbolTable(firstLocal);
        secondLocal.Define("e");
        secondLocal.Define("f");

        var expected = new Symbol[]
        {
            new("a", SymbolScope.Global, 0),
            new("c", SymbolScope.Free, 0),
            new("e", SymbolScope.Local, 0),
            new("f", SymbolScope.Local, 1),
        };

        foreach (var sym in expected)
        {
            var (result, ok) = secondLocal.Resolve(sym.Name);
            Assert.True(ok, $"name {sym.Name} not resolvable");
            Assert.Equal(sym, result);
        }

        var expectedUnresolvable = new[] { "b", "d" };

        foreach (var name in expectedUnresolvable)
        {
            var (_, ok) = secondLocal.Resolve(name);
            Assert.False(ok, $"name {name} resolved, but was expected not to");
        }
    }

    [Fact]
    public void TestDefineAndResolveFunctionName()
    {
        var global = SymbolTable.NewSymbolTable();
        global.DefineFunctionName("a");

        var expected = new Symbol("a", SymbolScope.Function, 0);

        var (result, ok) = global.Resolve(expected.Name);
        Assert.True(ok, $"function name {expected.Name} not resolvable");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TestShadowingFunctionName()
    {
        var global = SymbolTable.NewSymbolTable();
        global.DefineFunctionName("a");
        global.Define("a");

        var expected = new Symbol("a", SymbolScope.Global, 0);

        var (result, ok) = global.Resolve(expected.Name);
        Assert.True(ok, $"function name {expected.Name} not resolvable");
        Assert.Equal(expected, result);
    }
}
