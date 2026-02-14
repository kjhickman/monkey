using Kong.Code;
using Kong.Object;

namespace Kong.Tests;

public class CompilerTests
{
    [Fact]
    public void TestIntegerArithmetic()
    {
        var tests = new CompilerTestCase[]
        {
            new("1 + 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpAdd),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 - 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpSub),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 * 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpMul),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("2 / 1", new object[] { 2, 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpDiv),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1; 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpPop),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("-1", new object[] { 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpMinus),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestBooleanExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("!true", Array.Empty<object>(), new[]
            {
                Code.Code.Make(Opcode.OpTrue),
                Code.Code.Make(Opcode.OpBang),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 > 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpGreaterThan),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 < 2", new object[] { 2, 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpGreaterThan),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 == 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpEqual),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("1 != 2", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpNotEqual),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("true == false", Array.Empty<object>(), new[]
            {
                Code.Code.Make(Opcode.OpTrue),
                Code.Code.Make(Opcode.OpFalse),
                Code.Code.Make(Opcode.OpEqual),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("true != false", Array.Empty<object>(), new[]
            {
                Code.Code.Make(Opcode.OpTrue),
                Code.Code.Make(Opcode.OpFalse),
                Code.Code.Make(Opcode.OpNotEqual),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestConditionals()
    {
        var tests = new CompilerTestCase[]
        {
            new("if (true) { 10 }; 3333;", new object[] { 10, 3333 }, new[]
            {
                // 0000
                Code.Code.Make(Opcode.OpTrue),
                // 0001
                Code.Code.Make(Opcode.OpJumpNotTruthy, 10),
                // 0004
                Code.Code.Make(Opcode.OpConstant, 0),
                // 0007
                Code.Code.Make(Opcode.OpJump, 11),
                // 0010
                Code.Code.Make(Opcode.OpNull),
                // 0011
                Code.Code.Make(Opcode.OpPop),
                // 0012
                Code.Code.Make(Opcode.OpConstant, 1),
                // 0015
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestGlobalLetStatements()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            let one = 1;
            let two = 2;
            ", new object[] { 1, 2 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpSetGlobal, 1),
            }),
            new(@"
            let one = 1;
            one;
            ", new object[] { 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let one = 1;
            let two = one;
            two;
            ", new object[] { 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 1),
                Code.Code.Make(Opcode.OpGetGlobal, 1),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestStringExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("\"monkey\"", new object[] { "monkey" }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("\"mon\" + \"key\"", new object[] { "mon", "key" }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpAdd),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestArrayLiterals()
    {
        var tests = new CompilerTestCase[]
        {
            new("[]", Array.Empty<object>(), new[]
            {
                Code.Code.Make(Opcode.OpArray, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("[1, 2, 3]", new object[] { 1, 2, 3 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpArray, 3),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("[1 + 2, 3 - 4, 5 * 6]", new object[] { 1, 2, 3, 4, 5, 6 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpAdd),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpSub),
                Code.Code.Make(Opcode.OpConstant, 4),
                Code.Code.Make(Opcode.OpConstant, 5),
                Code.Code.Make(Opcode.OpMul),
                Code.Code.Make(Opcode.OpArray, 3),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestHashLiterals()
    {
        var tests = new CompilerTestCase[]
        {
            new("{}", Array.Empty<object>(), new[]
            {
                Code.Code.Make(Opcode.OpHash, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("{1: 2, 3: 4, 5: 6}", new object[] { 1, 2, 3, 4, 5, 6 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpConstant, 4),
                Code.Code.Make(Opcode.OpConstant, 5),
                Code.Code.Make(Opcode.OpHash, 6),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("{1: 2 + 3, 4: 5 * 6}", new object[] { 1, 2, 3, 4, 5, 6 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpAdd),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpConstant, 4),
                Code.Code.Make(Opcode.OpConstant, 5),
                Code.Code.Make(Opcode.OpMul),
                Code.Code.Make(Opcode.OpHash, 4),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestIndexExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("[1, 2, 3][1 + 1]", new object[] { 1, 2, 3, 1, 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpArray, 3),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpConstant, 4),
                Code.Code.Make(Opcode.OpAdd),
                Code.Code.Make(Opcode.OpIndex),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("{1: 2}[2 - 1]", new object[] { 1, 2, 2, 1 }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpHash, 2),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpSub),
                Code.Code.Make(Opcode.OpIndex),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctions()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { return 5 + 10 }", new object[]
            {
                5, 10,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpConstant, 1),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 2, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("fn() { 5 + 10 }", new object[]
            {
                5, 10,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpConstant, 1),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 2, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("fn() { 1; 2 }", new object[]
            {
                1, 2,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpPop),
                    Code.Code.Make(Opcode.OpConstant, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 2, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctionsWithoutReturnValue()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { }", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpReturn),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 0, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctionCalls()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { 24 }();", new object[]
            {
                24,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpCall, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let noArg = fn() { 24 };
            noArg();
            ", new object[]
            {
                24,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpCall, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let oneArg = fn(a) { a };
            oneArg(24);
            ", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                24,
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 0, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpCall, 1),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let manyArg = fn(a, b, c) { a; b; c };
            manyArg(24, 25, 26);
            ", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpPop),
                    Code.Code.Make(Opcode.OpGetLocal, 1),
                    Code.Code.Make(Opcode.OpPop),
                    Code.Code.Make(Opcode.OpGetLocal, 2),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                24, 25, 26,
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 0, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpConstant, 1),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpConstant, 3),
                Code.Code.Make(Opcode.OpCall, 3),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestLetStatementScopes()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            let num = 55;
            fn() { num }
            ", new object[]
            {
                55,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetGlobal, 0),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            fn() {
                let num = 55;
                num
            }
            ", new object[]
            {
                55,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            fn() {
                let a = 55;
                let b = 77;
                a + b
            }
            ", new object[]
            {
                55, 77,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpConstant, 1),
                    Code.Code.Make(Opcode.OpSetLocal, 1),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 1),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 2, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestCompilerScopes()
    {
        var compiler = new Compiler.Compiler();
        Assert.Equal(0, compiler.ScopeIndex);

        var globalSymbolTable = compiler.SymbolTable;

        compiler.Emit(Opcode.OpMul);

        compiler.EnterScope();
        Assert.Equal(1, compiler.ScopeIndex);

        compiler.Emit(Opcode.OpSub);

        Assert.Single(compiler.CurrentScope.Instructions.Bytes);

        var last = compiler.CurrentScope.LastInstruction;
        Assert.Equal(Opcode.OpSub, last.Opcode);

        Assert.Same(globalSymbolTable, compiler.SymbolTable.Outer);

        compiler.LeaveScope();
        Assert.Equal(0, compiler.ScopeIndex);

        Assert.Same(globalSymbolTable, compiler.SymbolTable);
        Assert.Null(compiler.SymbolTable.Outer);

        compiler.Emit(Opcode.OpAdd);

        Assert.Equal(2, compiler.CurrentScope.Instructions.Count);

        last = compiler.CurrentScope.LastInstruction;
        Assert.Equal(Opcode.OpAdd, last.Opcode);

        var previous = compiler.CurrentScope.PreviousInstruction;
        Assert.Equal(Opcode.OpMul, previous.Opcode);
    }

    [Fact]
    public void TestBuiltins()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            len([]);
            push([], 1);
            ", new object[] { 1 }, new[]
            {
                Code.Code.Make(Opcode.OpGetBuiltin, 0),
                Code.Code.Make(Opcode.OpArray, 0),
                Code.Code.Make(Opcode.OpCall, 1),
                Code.Code.Make(Opcode.OpPop),
                Code.Code.Make(Opcode.OpGetBuiltin, 5),
                Code.Code.Make(Opcode.OpArray, 0),
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpCall, 2),
                Code.Code.Make(Opcode.OpPop),
            }),
            new("fn() { len([]) }", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetBuiltin, 0),
                    Code.Code.Make(Opcode.OpArray, 0),
                    Code.Code.Make(Opcode.OpCall, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 0, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestClosures()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            fn(a) {
                fn(b) {
                    a + b
                }
            }
            ", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetFree, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpClosure, 0, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            fn(a) {
                fn(b) {
                    fn(c) {
                        a + b + c
                    }
                }
            };
            ", new object[]
            {
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetFree, 0),
                    Code.Code.Make(Opcode.OpGetFree, 1),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetFree, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpClosure, 0, 2),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpClosure, 1, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 2, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let global = 55;

            fn() {
                let a = 66;

                fn() {
                    let b = 77;

                    fn() {
                        let c = 88;

                        global + a + b + c;
                    }
                }
            }
            ", new object[]
            {
                55, 66, 77, 88,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 3),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpGetGlobal, 0),
                    Code.Code.Make(Opcode.OpGetFree, 0),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpGetFree, 1),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpAdd),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 2),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpGetFree, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpClosure, 4, 2),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpConstant, 1),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpClosure, 5, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpConstant, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpClosure, 6, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestRecursiveFunctions()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            let countDown = fn(x) { countDown(x - 1); };
            countDown(1);
            ", new object[]
            {
                1,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpCurrentClosure),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpSub),
                    Code.Code.Make(Opcode.OpCall, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                1,
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 1, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpConstant, 2),
                Code.Code.Make(Opcode.OpCall, 1),
                Code.Code.Make(Opcode.OpPop),
            }),
            new(@"
            let wrapper = fn() {
                let countDown = fn(x) { countDown(x - 1); };
                countDown(1);
            };
            wrapper();
            ", new object[]
            {
                1,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpCurrentClosure),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpConstant, 0),
                    Code.Code.Make(Opcode.OpSub),
                    Code.Code.Make(Opcode.OpCall, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
                1,
                new byte[][]
                {
                    Code.Code.Make(Opcode.OpClosure, 1, 0),
                    Code.Code.Make(Opcode.OpSetLocal, 0),
                    Code.Code.Make(Opcode.OpGetLocal, 0),
                    Code.Code.Make(Opcode.OpConstant, 2),
                    Code.Code.Make(Opcode.OpCall, 1),
                    Code.Code.Make(Opcode.OpReturnValue),
                },
            }, new[]
            {
                Code.Code.Make(Opcode.OpClosure, 3, 0),
                Code.Code.Make(Opcode.OpSetGlobal, 0),
                Code.Code.Make(Opcode.OpGetGlobal, 0),
                Code.Code.Make(Opcode.OpCall, 0),
                Code.Code.Make(Opcode.OpPop),
            }),
        };

        RunCompilerTests(tests);
    }

    // --- Test infrastructure ---

    private record CompilerTestCase(string Input, object[] ExpectedConstants, byte[][] ExpectedInstructions);

    private static void RunCompilerTests(CompilerTestCase[] tests)
    {
        foreach (var tt in tests)
        {
            var program = Parse(tt.Input);
            var compiler = new Compiler.Compiler();
            compiler.Compile(program);
            Assert.False(compiler.Diagnostics.HasErrors,
                $"compiler had errors: {string.Join(", ", compiler.Diagnostics.All)}");

            var bytecode = compiler.GetBytecode();

            TestInstructions(tt.ExpectedInstructions, bytecode.Instructions);
            TestConstants(tt.ExpectedConstants, bytecode.Constants);
        }
    }

    private static Ast.Program Parse(string input)
    {
        var l = new Lexer.Lexer(input);
        var p = new Parser.Parser(l);
        return p.ParseProgram();
    }

    private static void TestInstructions(byte[][] expected, Instructions actual)
    {
        var concatted = ConcatInstructions(expected);

        Assert.Equal(concatted.Length, actual.Count);

        for (var i = 0; i < concatted.Length; i++)
        {
            Assert.Equal(concatted[i], actual[i]);
        }
    }

    private static byte[] ConcatInstructions(byte[][] s)
    {
        var result = new List<byte>();
        foreach (var ins in s)
        {
            result.AddRange(ins);
        }
        return result.ToArray();
    }

    private static void TestConstants(object[] expected, List<IObject> actual)
    {
        Assert.Equal(expected.Length, actual.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            switch (expected[i])
            {
                case int intVal:
                    TestIntegerObject((long)intVal, actual[i]);
                    break;
                case long longVal:
                    TestIntegerObject(longVal, actual[i]);
                    break;
                case string strVal:
                    TestStringObject(strVal, actual[i]);
                    break;
                case byte[][] instructions:
                    var fn = Assert.IsType<CompiledFunctionObj>(actual[i]);
                    TestInstructions(instructions, fn.Instructions);
                    break;
            }
        }
    }

    private static void TestIntegerObject(long expected, IObject actual)
    {
        var result = Assert.IsType<IntegerObj>(actual);
        Assert.Equal(expected, result.Value);
    }

    private static void TestStringObject(string expected, IObject actual)
    {
        var result = Assert.IsType<StringObj>(actual);
        Assert.Equal(expected, result.Value);
    }
}
