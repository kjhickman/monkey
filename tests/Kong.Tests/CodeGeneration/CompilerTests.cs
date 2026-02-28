using Kong.CodeGeneration;

namespace Kong.Tests.CodeGeneration;

public class CompilerTests
{
    [Fact]
    public void TestIntegerArithmetic()
    {
        var tests = new CompilerTestCase[]
        {
            new("1 + 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpAdd),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 - 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpSub),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 * 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpMul),
                Code.Make(Opcode.OpPop),
            ]),
            new("2 / 1", [2, 1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpDiv),
                Code.Make(Opcode.OpPop),
            ]),
            new("1; 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpPop),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpPop),
            ]),
            new("-1", [1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpMinus),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestBooleanExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("!true", Array.Empty<object>(),
            [
                Code.Make(Opcode.OpTrue),
                Code.Make(Opcode.OpBang),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 > 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpGreaterThan),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 < 2", [2, 1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpGreaterThan),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 == 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpEqual),
                Code.Make(Opcode.OpPop),
            ]),
            new("1 != 2", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpNotEqual),
                Code.Make(Opcode.OpPop),
            ]),
            new("true == false", Array.Empty<object>(),
            [
                Code.Make(Opcode.OpTrue),
                Code.Make(Opcode.OpFalse),
                Code.Make(Opcode.OpEqual),
                Code.Make(Opcode.OpPop),
            ]),
            new("true != false", Array.Empty<object>(),
            [
                Code.Make(Opcode.OpTrue),
                Code.Make(Opcode.OpFalse),
                Code.Make(Opcode.OpNotEqual),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestConditionals()
    {
        var tests = new CompilerTestCase[]
        {
            new("if (true) { 10 }; 3333;", [10, 3333],
            [
                // 0000
                Code.Make(Opcode.OpTrue),
                // 0001
                Code.Make(Opcode.OpJumpNotTruthy, 10),
                // 0004
                Code.Make(Opcode.OpConstant, 0),
                // 0007
                Code.Make(Opcode.OpJump, 11),
                // 0010
                Code.Make(Opcode.OpNull),
                // 0011
                Code.Make(Opcode.OpPop),
                // 0012
                Code.Make(Opcode.OpConstant, 1),
                // 0015
                Code.Make(Opcode.OpPop),
            ]),
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
            ", [1, 2],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpSetGlobal, 1),
            ]),
            new(@"
            let one = 1;
            one;
            ", [1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            let one = 1;
            let two = one;
            two;
            ", [1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpSetGlobal, 1),
                Code.Make(Opcode.OpGetGlobal, 1),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestStringExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("\"monkey\"", ["monkey"],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new("\"mon\" + \"key\"", ["mon", "key"],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpAdd),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestArrayLiterals()
    {
        var tests = new CompilerTestCase[]
        {
            new("[]", Array.Empty<object>(),
            [
                Code.Make(Opcode.OpArray, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new("[1, 2, 3]", [1, 2, 3],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpArray, 3),
                Code.Make(Opcode.OpPop),
            ]),
            new("[1 + 2, 3 - 4, 5 * 6]", [1, 2, 3, 4, 5, 6],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpAdd),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpSub),
                Code.Make(Opcode.OpConstant, 4),
                Code.Make(Opcode.OpConstant, 5),
                Code.Make(Opcode.OpMul),
                Code.Make(Opcode.OpArray, 3),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestHashLiterals()
    {
        var tests = new CompilerTestCase[]
        {
            new("{}", Array.Empty<object>(),
            [
                Code.Make(Opcode.OpHash, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new("{1: 2, 3: 4, 5: 6}", [1, 2, 3, 4, 5, 6],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpConstant, 4),
                Code.Make(Opcode.OpConstant, 5),
                Code.Make(Opcode.OpHash, 6),
                Code.Make(Opcode.OpPop),
            ]),
            new("{1: 2 + 3, 4: 5 * 6}", [1, 2, 3, 4, 5, 6],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpAdd),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpConstant, 4),
                Code.Make(Opcode.OpConstant, 5),
                Code.Make(Opcode.OpMul),
                Code.Make(Opcode.OpHash, 4),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestIndexExpressions()
    {
        var tests = new CompilerTestCase[]
        {
            new("[1, 2, 3][1 + 1]", [1, 2, 3, 1, 1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpArray, 3),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpConstant, 4),
                Code.Make(Opcode.OpAdd),
                Code.Make(Opcode.OpIndex),
                Code.Make(Opcode.OpPop),
            ]),
            new("{1: 2}[2 - 1]", [1, 2, 2, 1],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpHash, 2),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpSub),
                Code.Make(Opcode.OpIndex),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctions()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { return 5 + 10 }",
            [
                5, 10,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpConstant, 1),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 2, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new("fn() { 5 + 10 }",
            [
                5, 10,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpConstant, 1),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 2, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new("fn() { 1; 2 }",
            [
                1, 2,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpPop),
                    Code.Make(Opcode.OpConstant, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 2, 0),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctionsWithoutReturnValue()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { }",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpReturn),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 0, 0),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctionCalls()
    {
        var tests = new CompilerTestCase[]
        {
            new("fn() { 24 }();",
            [
                24,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpCall, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            let noArg = fn() { 24 };
            noArg();
            ",
            [
                24,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpCall, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            let oneArg = fn(a: int) { a };
            oneArg(24);
            ",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpReturnValue),
                },
                24,
            ],
            [
                Code.Make(Opcode.OpClosure, 0, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpCall, 1),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            let manyArg = fn(a: int, b: int, c: int) { a; b; c };
            manyArg(24, 25, 26);
            ",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpPop),
                    Code.Make(Opcode.OpGetLocal, 1),
                    Code.Make(Opcode.OpPop),
                    Code.Make(Opcode.OpGetLocal, 2),
                    Code.Make(Opcode.OpReturnValue),
                },
                24, 25, 26,
            ],
            [
                Code.Make(Opcode.OpClosure, 0, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpConstant, 1),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpConstant, 3),
                Code.Make(Opcode.OpCall, 3),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestFunctionCallTypeAnnotations()
    {
        var okProgram = Parse(@"
            let id = fn(x: int) { x };
            id(24);
        ");
        var okCompiler = new Compiler();
        var okErr = okCompiler.Compile(okProgram);
        Assert.Null(okErr);

        var wrongTypeProgram = Parse(@"
            let id = fn(x: int) { x };
            id(true);
        ");
        var wrongTypeCompiler = new Compiler();
        var wrongTypeErr = wrongTypeCompiler.Compile(wrongTypeProgram);
        Assert.Equal("type mismatch for argument 1 in call to id: expected int, got bool", wrongTypeErr);

        var mapProgram = Parse(@"
            let get = fn(m: map[string]int) { m[""a""] };
            get({""a"": 1});
        ");
        var mapCompiler = new Compiler();
        var mapErr = mapCompiler.Compile(mapProgram);
        Assert.Null(mapErr);

        var unknownTypeProgram = Parse(@"
            let id = fn(x: integer) { x };
            id(24);
        ");
        var unknownTypeCompiler = new Compiler();
        var unknownTypeErr = unknownTypeCompiler.Compile(unknownTypeProgram);
        Assert.Equal("invalid type annotation for parameter 'x': unknown type 'integer'", unknownTypeErr);

        var arrayMismatchProgram = Parse(@"
            let sumFirst = fn(values: int[]) { values[0] };
            sumFirst([true]);
        ");
        var arrayMismatchCompiler = new Compiler();
        var arrayMismatchErr = arrayMismatchCompiler.Compile(arrayMismatchProgram);
        Assert.Equal("type mismatch for argument 1 in call to sumFirst: expected int[], got bool[]", arrayMismatchErr);

        var mapMismatchProgram = Parse(@"
            let get = fn(m: map[string]int) { m[""a""] };
            get({""a"": true});
        ");
        var mapMismatchCompiler = new Compiler();
        var mapMismatchErr = mapMismatchCompiler.Compile(mapMismatchProgram);
        Assert.Equal("type mismatch for argument 1 in call to get: expected map[string]int, got map[string]bool", mapMismatchErr);
    }

    [Fact]
    public void TestLetStatementScopes()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            let num = 55;
            fn() { num }
            ",
            [
                55,
                new byte[][]
                {
                    Code.Make(Opcode.OpGetGlobal, 0),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            fn() {
                let num = 55;
                num
            }
            ",
            [
                55,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            fn() {
                let a = 55;
                let b = 77;
                a + b
            }
            ",
            [
                55, 77,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpConstant, 1),
                    Code.Make(Opcode.OpSetLocal, 1),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpGetLocal, 1),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 2, 0),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestCompilerScopes()
    {
        var compiler = new Compiler();
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
            ", [1],
            [
                Code.Make(Opcode.OpGetBuiltin, 0),
                Code.Make(Opcode.OpArray, 0),
                Code.Make(Opcode.OpCall, 1),
                Code.Make(Opcode.OpPop),
                Code.Make(Opcode.OpGetBuiltin, 5),
                Code.Make(Opcode.OpArray, 0),
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpCall, 2),
                Code.Make(Opcode.OpPop),
            ]),
            new("fn() { len([]) }",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpGetBuiltin, 0),
                    Code.Make(Opcode.OpArray, 0),
                    Code.Make(Opcode.OpCall, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 0, 0),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestClosures()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            fn(a: int) {
                fn(b: int) {
                    a + b
                }
            }
            ",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpGetFree, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpClosure, 0, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            fn(a: int) {
                fn(b: int) {
                    fn(c: int) {
                        a + b + c
                    }
                }
            };
            ",
            [
                new byte[][]
                {
                    Code.Make(Opcode.OpGetFree, 0),
                    Code.Make(Opcode.OpGetFree, 1),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Make(Opcode.OpGetFree, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpClosure, 0, 2),
                    Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpClosure, 1, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 2, 0),
                Code.Make(Opcode.OpPop),
            ]),
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
            ",
            [
                55, 66, 77, 88,
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 3),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpGetGlobal, 0),
                    Code.Make(Opcode.OpGetFree, 0),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpGetFree, 1),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpAdd),
                    Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 2),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpGetFree, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpClosure, 4, 2),
                    Code.Make(Opcode.OpReturnValue),
                },
                new byte[][]
                {
                    Code.Make(Opcode.OpConstant, 1),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpClosure, 5, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpConstant, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpClosure, 6, 0),
                Code.Make(Opcode.OpPop),
            ]),
        };

        RunCompilerTests(tests);
    }

    [Fact]
    public void TestRecursiveFunctions()
    {
        var tests = new CompilerTestCase[]
        {
            new(@"
            let countDown = fn(x: int) { countDown(x - 1); };
            countDown(1);
            ",
            [
                1,
                new byte[][]
                {
                    Code.Make(Opcode.OpCurrentClosure),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpSub),
                    Code.Make(Opcode.OpCall, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
                1,
            ],
            [
                Code.Make(Opcode.OpClosure, 1, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpConstant, 2),
                Code.Make(Opcode.OpCall, 1),
                Code.Make(Opcode.OpPop),
            ]),
            new(@"
            let wrapper = fn() {
                let countDown = fn(x: int) { countDown(x - 1); };
                countDown(1);
            };
            wrapper();
            ",
            [
                1,
                new byte[][]
                {
                    Code.Make(Opcode.OpCurrentClosure),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpConstant, 0),
                    Code.Make(Opcode.OpSub),
                    Code.Make(Opcode.OpCall, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
                1,
                new byte[][]
                {
                    Code.Make(Opcode.OpClosure, 1, 0),
                    Code.Make(Opcode.OpSetLocal, 0),
                    Code.Make(Opcode.OpGetLocal, 0),
                    Code.Make(Opcode.OpConstant, 2),
                    Code.Make(Opcode.OpCall, 1),
                    Code.Make(Opcode.OpReturnValue),
                },
            ],
            [
                Code.Make(Opcode.OpClosure, 3, 0),
                Code.Make(Opcode.OpSetGlobal, 0),
                Code.Make(Opcode.OpGetGlobal, 0),
                Code.Make(Opcode.OpCall, 0),
                Code.Make(Opcode.OpPop),
            ]),
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
            var compiler = new Compiler();
            var err = compiler.Compile(program);
            Assert.Null(err);

            var bytecode = compiler.GetBytecode();

            TestInstructions(tt.ExpectedInstructions, bytecode.Instructions);
            TestConstants(tt.ExpectedConstants, bytecode.Constants);
        }
    }

    private static Kong.Parsing.Program Parse(string input)
    {
        var l = new Kong.Lexing.Lexer(input);
        var p = new Kong.Parsing.Parser(l);
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
