using Kong.CodeGeneration;

namespace Kong.Tests.Evaluating;

public class VmTests
{
    [Fact]
    public void TestIntegerArithmetic()
    {
        var tests = new VmTestCase[]
        {
            new("1", 1),
            new("2", 2),
            new("1 + 2", 3),
            new("1 - 2", -1),
            new("1 * 2", 2),
            new("4 / 2", 2),
            new("50 / 2 * 2 + 10 - 5", 55),
            new("5 + 5 + 5 + 5 - 10", 10),
            new("2 * 2 * 2 * 2 * 2", 32),
            new("5 * 2 + 10", 20),
            new("5 + 2 * 10", 25),
            new("5 * (2 + 10)", 60),
            new("-5", -5),
            new("-10", -10),
            new("-50 + 100 + -50", 0),
            new("(5 + 10 * 2 + 15 / 3) * 2 + -10", 50),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestBooleanExpressions()
    {
        var tests = new VmTestCase[]
        {
            new("1 < 2", true),
            new("1 > 2", false),
            new("1 < 1", false),
            new("1 > 1", false),
            new("1 == 1", true),
            new("1 != 1", false),
            new("1 == 2", false),
            new("1 != 2", true),
            new("true == true", true),
            new("false == false", true),
            new("true == false", false),
            new("true != false", true),
            new("false != true", true),
            new("(1 < 2) == true", true),
            new("(1 < 2) == false", false),
            new("(1 > 2) == true", false),
            new("(1 > 2) == false", true),
            new("!true", false),
            new("!false", true),
            new("!5", false),
            new("!!true", true),
            new("!!false", false),
            new("!!5", true),
            new("!(if (false) { 5; })", true),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestConditionals()
    {
        var tests = new VmTestCase[]
        {
            new("if (true) { 10 }", 10),
            new("if (true) { 10 } else { 20 }", 10),
            new("if (false) { 10 } else { 20 }", 20),
            new("if (1) { 10 }", 10),
            new("if (1 < 2) { 10 }", 10),
            new("if (1 < 2) { 10 } else { 20 }", 10),
            new("if (1 > 2) { 10 } else { 20 }", 20),
            new("if (1 > 2) { 10 }", NullValue),
            new("if (false) { 10 }", NullValue),
            new("if ((if (false) { 10 })) { 10 } else { 20 }", 20),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestGlobalLetStatements()
    {
        var tests = new VmTestCase[]
        {
            new("let one = 1; one", 1),
            new("let one = 1; let two = 2; one + two", 3),
            new("let one = 1; let two = one + one; one + two", 3),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestStringExpressions()
    {
        var tests = new VmTestCase[]
        {
            new("\"monkey\"", "monkey"),
            new("\"mon\" + \"key\"", "monkey"),
            new("\"mon\" + \"key\" + \"banana\"", "monkeybanana"),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestArrayLiterals()
    {
        var tests = new VmTestCase[]
        {
            new("[]", Array.Empty<int>()),
            new("[1, 2, 3]", new[] { 1, 2, 3 }),
            new("[1 + 2, 3 * 4, 5 + 6]", new[] { 3, 12, 11 }),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestHashLiterals()
    {
        var tests = new VmTestCase[]
        {
            new("{}", new Dictionary<HashKey, long>()),
            new("{1: 2, 2: 3}", new Dictionary<HashKey, long>
            {
                { new IntegerObj { Value = 1 }.HashKey(), 2 },
                { new IntegerObj { Value = 2 }.HashKey(), 3 },
            }),
            new("{1 + 1: 2 * 2, 3 + 3: 4 * 4}", new Dictionary<HashKey, long>
            {
                { new IntegerObj { Value = 2 }.HashKey(), 4 },
                { new IntegerObj { Value = 6 }.HashKey(), 16 },
            }),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestIndexExpressions()
    {
        var tests = new VmTestCase[]
        {
            new("[1, 2, 3][1]", 2),
            new("[1, 2, 3][0 + 2]", 3),
            new("[[1, 1, 1]][0][0]", 1),
            new("[][0]", NullValue),
            new("[1, 2, 3][99]", NullValue),
            new("[1][-1]", NullValue),
            new("{1: 1, 2: 2}[1]", 1),
            new("{1: 1, 2: 2}[2]", 2),
            new("{1: 1}[0]", NullValue),
            new("{}[0]", NullValue),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestCallingFunctionsWithoutArguments()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let fivePlusTen = fn() { 5 + 10; };
            fivePlusTen();
            ", 15),
            new(@"
            let one = fn() { 1; };
            let two = fn() { 2; };
            one() + two()
            ", 3),
            new(@"
            let a = fn() { 1 };
            let b = fn() { a() + 1 };
            let c = fn() { b() + 1 };
            c();
            ", 3),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestFunctionsWithReturnStatement()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let earlyExit = fn() { return 99; 100; };
            earlyExit();
            ", 99),
            new(@"
            let earlyExit = fn() { return 99; return 100; };
            earlyExit();
            ", 99),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestFunctionsWithoutReturnValue()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let noReturn = fn() { };
            noReturn();
            ", NullValue),
            new(@"
            let noReturn = fn() { };
            let noReturnTwo = fn() { noReturn(); };
            noReturn();
            noReturnTwo();
            ", NullValue),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestFirstClassFunctions()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let returnsOne = fn() { 1; };
            let returnsOneReturner = fn() { returnsOne; };
            returnsOneReturner()();
            ", 1),
            new(@"
            let returnsOneReturner = fn() {
                let returnsOne = fn() { 1; };
                returnsOne;
            };
            returnsOneReturner()();
            ", 1),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestCallingFunctionsWithBindings()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let one = fn() { let one = 1; one };
            one();
            ", 1),
            new(@"
            let oneAndTwo = fn() { let one = 1; let two = 2; one + two; };
            oneAndTwo();
            ", 3),
            new(@"
            let oneAndTwo = fn() { let one = 1; let two = 2; one + two; };
            let threeAndFour = fn() { let three = 3; let four = 4; three + four; };
            oneAndTwo() + threeAndFour();
            ", 10),
            new(@"
            let firstFoobar = fn() { let foobar = 50; foobar; };
            let secondFoobar = fn() { let foobar = 100; foobar; };
            firstFoobar() + secondFoobar();
            ", 150),
            new(@"
            let globalSeed = 50;
            let minusOne = fn() {
                let num = 1;
                globalSeed - num;
            }
            let minusTwo = fn() {
                let num = 2;
                globalSeed - num;
            }
            minusOne() + minusTwo();
            ", 97),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestCallingFunctionsWithArgumentsAndBindings()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let identity = fn(a) { a; };
            identity(4);
            ", 4),
            new(@"
            let sum = fn(a, b) { a + b; };
            sum(1, 2);
            ", 3),
            new(@"
            let sum = fn(a, b) {
                let c = a + b;
                c;
            };
            sum(1, 2);
            ", 3),
            new(@"
            let sum = fn(a, b) {
                let c = a + b;
                c;
            };
            sum(1, 2) + sum(3, 4);
            ", 10),
            new(@"
            let sum = fn(a, b) {
                let c = a + b;
                c;
            };
            let outer = fn() {
                sum(1, 2) + sum(3, 4);
            };
            outer();
            ", 10),
            new(@"
            let globalNum = 10;

            let sum = fn(a, b) {
                let c = a + b;
                c + globalNum;
            };

            let outer = fn() {
                sum(1, 2) + sum(3, 4) + globalNum;
            };

            outer() + globalNum;
            ", 50),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestCallingFunctionsWithWrongArguments()
    {
        var tests = new (string Input, string ExpectedError)[]
        {
            ("fn() { 1; }(1);", "wrong number of arguments: want=0, got=1"),
            ("fn(a) { a; }();", "wrong number of arguments: want=1, got=0"),
            ("fn(a, b) { a + b; }(1);", "wrong number of arguments: want=2, got=1"),
        };

        foreach (var tt in tests)
        {
            var program = Parse(tt.Input);
            var compiler = new Compiler();
            var compErr = compiler.Compile(program);
            Assert.Null(compErr);

            var vm = new Kong.Evaluating.Vm(compiler.GetBytecode());
            var err = vm.Run();
            Assert.NotNull(err);
            Assert.Equal(tt.ExpectedError, err);
        }
    }

    [Fact]
    public void TestBuiltinFunctions()
    {
        var tests = new VmTestCase[]
        {
            new("len(\"\")", 0),
            new("len(\"four\")", 4),
            new("len(\"hello world\")", 11),
            new("len(1)", new ExpectedError("argument to `len` not supported, got Integer")),
            new("len(\"one\", \"two\")", new ExpectedError("wrong number of arguments. got=2, want=1")),
            new("len([1, 2, 3])", 3),
            new("len([])", 0),
            new("puts(\"hello\", \"world!\")", NullValue),
            new("first([1, 2, 3])", 1),
            new("first([])", NullValue),
            new("first(1)", new ExpectedError("argument to `first` must be ARRAY, got Integer")),
            new("last([1, 2, 3])", 3),
            new("last([])", NullValue),
            new("last(1)", new ExpectedError("argument to `last` must be ARRAY, got Integer")),
            new("rest([1, 2, 3])", new[] { 2, 3 }),
            new("rest([])", NullValue),
            new("push([], 1)", new[] { 1 }),
            new("push(1, 1)", new ExpectedError("argument to `push` must be ARRAY, got Integer")),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestClosures()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let newClosure = fn(a) {
                fn() { a; };
            };
            let closure = newClosure(99);
            closure();
            ", 99),
            new(@"
            let newAdder = fn(a, b) {
                fn(c) { a + b + c };
            };
            let adder = newAdder(1, 2);
            adder(8);
            ", 11),
            new(@"
            let newAdder = fn(a, b) {
                let c = a + b;
                fn(d) { c + d };
            };
            let adder = newAdder(1, 2);
            adder(8);
            ", 11),
            new(@"
            let newAdderOuter = fn(a, b) {
                let c = a + b;
                fn(d) {
                    let e = d + c;
                    fn(f) { e + f; };
                };
            };
            let newAdderInner = newAdderOuter(1, 2)
            let adder = newAdderInner(3);
            adder(8);
            ", 14),
            new(@"
            let a = 1;
            let newAdderOuter = fn(b) {
                fn(c) {
                    fn(d) { a + b + c + d };
                };
            };
            let newAdderInner = newAdderOuter(2)
            let adder = newAdderInner(3);
            adder(8);
            ", 14),
            new(@"
            let newClosure = fn(a, b) {
                let one = fn() { a; };
                let two = fn() { b; };
                fn() { one() + two(); };
            };
            let closure = newClosure(9, 90);
            closure();
            ", 99),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestRecursiveFunctions()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let countDown = fn(x) {
                if (x == 0) {
                    return 0;
                } else {
                    countDown(x - 1);
                }
            };
            countDown(1);
            ", 0),
            new(@"
            let countDown = fn(x) {
                if (x == 0) {
                    return 0;
                } else {
                    countDown(x - 1);
                }
            };
            let wrapper = fn() {
                countDown(1);
            };
            wrapper();
            ", 0),
            new(@"
            let wrapper = fn() {
                let countDown = fn(x) {
                    if (x == 0) {
                        return 0;
                    } else {
                        countDown(x - 1);
                    }
                };
                countDown(1);
            };
            wrapper();
            ", 0),
        };

        RunVmTests(tests);
    }

    [Fact]
    public void TestRecursiveFibonacci()
    {
        var tests = new VmTestCase[]
        {
            new(@"
            let fibonacci = fn(x) {
                if (x == 0) {
                    return 0;
                } else {
                    if (x == 1) {
                        return 1;
                    } else {
                        fibonacci(x - 1) + fibonacci(x - 2);
                    }
                }
            };
            fibonacci(15);
            ", 610),
        };

        RunVmTests(tests);
    }

    // --- Test infrastructure ---

    // Sentinel to represent expected null
    private static readonly object NullValue = new();

    // Wrapper for expected error messages
    private record ExpectedError(string Message);

    private record VmTestCase(string Input, object Expected);

    private static void RunVmTests(VmTestCase[] tests)
    {
        foreach (var tt in tests)
        {
            var program = Parse(tt.Input);
            var compiler = new Compiler();
            var compErr = compiler.Compile(program);
            Assert.Null(compErr);

            var vm = new Kong.Evaluating.Vm(compiler.GetBytecode());
            var err = vm.Run();
            Assert.Null(err);

            var stackElem = vm.LastPoppedStackElem();
            TestExpectedObject(tt.Expected, stackElem);
        }
    }

    private static Kong.Parsing.Program Parse(string input)
    {
        var l = new Kong.Lexing.Lexer(input);
        var p = new Kong.Parsing.Parser(l);
        return p.ParseProgram();
    }

    private static void TestExpectedObject(object expected, IObject actual)
    {
        switch (expected)
        {
            case int intVal:
                TestIntegerObject((long)intVal, actual);
                break;
            case long longVal:
                TestIntegerObject(longVal, actual);
                break;
            case bool boolVal:
                TestBooleanObject(boolVal, actual);
                break;
            case string strVal:
                TestStringObject(strVal, actual);
                break;
            case int[] intArr:
                var array = Assert.IsType<ArrayObj>(actual);
                Assert.Equal(intArr.Length, array.Elements.Count);
                for (var i = 0; i < intArr.Length; i++)
                {
                    TestIntegerObject(intArr[i], array.Elements[i]);
                }
                break;
            case Dictionary<HashKey, long> hashMap:
                var hash = Assert.IsType<HashObj>(actual);
                Assert.Equal(hashMap.Count, hash.Pairs.Count);
                foreach (var (expectedKey, expectedValue) in hashMap)
                {
                    Assert.True(hash.Pairs.ContainsKey(expectedKey), "no pair for given key in Pairs");
                    TestIntegerObject(expectedValue, hash.Pairs[expectedKey].Value);
                }
                break;
            case ExpectedError expectedError:
                var errObj = Assert.IsType<ErrorObj>(actual);
                Assert.Equal(expectedError.Message, errObj.Message);
                break;
            default:
                // NullValue sentinel
                if (ReferenceEquals(expected, NullValue))
                {
                    Assert.IsType<NullObj>(actual);
                }
                else
                {
                    Assert.Fail($"Unexpected expected type: {expected.GetType()}");
                }
                break;
        }
    }

    private static void TestIntegerObject(long expected, IObject actual)
    {
        var result = Assert.IsType<IntegerObj>(actual);
        Assert.Equal(expected, result.Value);
    }

    private static void TestBooleanObject(bool expected, IObject actual)
    {
        var result = Assert.IsType<BooleanObj>(actual);
        Assert.Equal(expected, result.Value);
    }

    private static void TestStringObject(string expected, IObject actual)
    {
        var result = Assert.IsType<StringObj>(actual);
        Assert.Equal(expected, result.Value);
    }
}
