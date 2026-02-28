using Kong.Parsing;

namespace Kong.CodeGeneration;

public class EmittedInstruction
{
    public Opcode Opcode { get; set; }
    public int Position { get; set; }
}

public class CompilationScope
{
    public Instructions Instructions { get; set; } = new();
    public EmittedInstruction LastInstruction { get; set; } = new();
    public EmittedInstruction PreviousInstruction { get; set; } = new();
}

public class Bytecode
{
    public Instructions Instructions { get; set; } = new();
    public List<IObject> Constants { get; set; } = [];
}

public class Compiler
{
    private List<IObject> _constants;
    public SymbolTable SymbolTable { get; private set; }

    private readonly List<CompilationScope> _scopes;
    private int _scopeIndex;

    // Exposed for tests
    public int ScopeIndex => _scopeIndex;
    public CompilationScope CurrentScope => _scopes[_scopeIndex];

    public Compiler()
    {
        var mainScope = new CompilationScope();

        SymbolTable = SymbolTable.NewSymbolTable();
        for (var i = 0; i < Builtins.All.Length; i++)
        {
            SymbolTable.DefineBuiltin(i, Builtins.All[i].Name);
        }

        _constants = [];
        _scopes = [mainScope];
        _scopeIndex = 0;
    }

    public static Compiler NewWithState(SymbolTable symbolTable, List<IObject> constants)
    {
        var compiler = new Compiler
        {
            SymbolTable = symbolTable,
            _constants = constants,
        };
        return compiler;
    }

    public Bytecode GetBytecode()
    {
        return new Bytecode
        {
            Instructions = CurrentInstructions(),
            Constants = _constants,
        };
    }

    public string? Compile(INode node)
    {
        switch (node)
        {
            case Program program:
                foreach (var s in program.Statements)
                {
                    var err = Compile(s);
                    if (err != null) return err;
                }
                break;

            case ExpressionStatement es:
                var exprErr = Compile(es.Expression!);
                if (exprErr != null) return exprErr;
                Emit(Opcode.OpPop);
                break;

            case BlockStatement bs:
                foreach (var s in bs.Statements)
                {
                    var err = Compile(s);
                    if (err != null) return err;
                }
                break;

            case LetStatement ls:
            {
                var symbol = SymbolTable.Define(ls.Name.Value);
                var err = Compile(ls.Value!);
                if (err != null) return err;

                if (symbol.Scope == SymbolScope.Global)
                    Emit(Opcode.OpSetGlobal, symbol.Index);
                else
                    Emit(Opcode.OpSetLocal, symbol.Index);
                break;
            }

            case ReturnStatement rs:
            {
                var err = Compile(rs.ReturnValue!);
                if (err != null) return err;
                Emit(Opcode.OpReturnValue);
                break;
            }

            case Identifier ident:
            {
                var (symbol, ok) = SymbolTable.Resolve(ident.Value);
                if (!ok) return $"undefined variable {ident.Value}";
                LoadSymbol(symbol);
                break;
            }

            case IfExpression ie:
            {
                var err = Compile(ie.Condition);
                if (err != null) return err;

                var jumpNotTruthyPos = Emit(Opcode.OpJumpNotTruthy, 9999);

                err = Compile(ie.Consequence);
                if (err != null) return err;

                if (LastInstructionIs(Opcode.OpPop))
                    RemoveLastPop();

                var jumpPos = Emit(Opcode.OpJump, 9999);

                var afterConsequencePos = CurrentInstructions().Count;
                ChangeOperand(jumpNotTruthyPos, afterConsequencePos);

                if (ie.Alternative == null)
                {
                    Emit(Opcode.OpNull);
                }
                else
                {
                    err = Compile(ie.Alternative);
                    if (err != null) return err;

                    if (LastInstructionIs(Opcode.OpPop))
                        RemoveLastPop();
                }

                var afterAlternativePos = CurrentInstructions().Count;
                ChangeOperand(jumpPos, afterAlternativePos);
                break;
            }

            case FunctionLiteral fl:
            {
                EnterScope();

                if (fl.Name != "")
                    SymbolTable.DefineFunctionName(fl.Name);

                foreach (var p in fl.Parameters)
                    SymbolTable.Define(p.Name.Value);

                var err = Compile(fl.Body);
                if (err != null) return err;

                if (LastInstructionIs(Opcode.OpPop))
                    ReplaceLastPopWithReturn();
                if (!LastInstructionIs(Opcode.OpReturnValue))
                    Emit(Opcode.OpReturn);

                var freeSymbols = SymbolTable.FreeSymbols;
                var numLocals = SymbolTable.NumDefinitions;
                var instructions = LeaveScope();

                foreach (var s in freeSymbols)
                    LoadSymbol(s);

                var compiledFn = new CompiledFunctionObj
                {
                    Instructions = instructions,
                    NumLocals = numLocals,
                    NumParameters = fl.Parameters.Count,
                };
                var fnIndex = AddConstant(compiledFn);
                Emit(Opcode.OpClosure, fnIndex, freeSymbols.Count);
                break;
            }

            case CallExpression ce:
            {
                var err = Compile(ce.Function);
                if (err != null) return err;

                foreach (var a in ce.Arguments)
                {
                    err = Compile(a);
                    if (err != null) return err;
                }

                Emit(Opcode.OpCall, ce.Arguments.Count);
                break;
            }

            case PrefixExpression pe:
            {
                var err = Compile(pe.Right);
                if (err != null) return err;

                switch (pe.Operator)
                {
                    case "!": Emit(Opcode.OpBang); break;
                    case "-": Emit(Opcode.OpMinus); break;
                    default: return $"unknown operator {pe.Operator}";
                }
                break;
            }

            case InfixExpression ie:
            {
                if (ie.Operator == "<")
                {
                    var err = Compile(ie.Right);
                    if (err != null) return err;
                    err = Compile(ie.Left);
                    if (err != null) return err;
                    Emit(Opcode.OpGreaterThan);
                    return null;
                }

                {
                    var err = Compile(ie.Left);
                    if (err != null) return err;
                    err = Compile(ie.Right);
                    if (err != null) return err;
                }

                switch (ie.Operator)
                {
                    case "+": Emit(Opcode.OpAdd); break;
                    case "-": Emit(Opcode.OpSub); break;
                    case "*": Emit(Opcode.OpMul); break;
                    case "/": Emit(Opcode.OpDiv); break;
                    case ">": Emit(Opcode.OpGreaterThan); break;
                    case "==": Emit(Opcode.OpEqual); break;
                    case "!=": Emit(Opcode.OpNotEqual); break;
                    default: return $"unknown operator {ie.Operator}";
                }
                break;
            }

            case BooleanLiteral bl:
                Emit(bl.Value ? Opcode.OpTrue : Opcode.OpFalse);
                break;

            case IntegerLiteral il:
            {
                var integer = new IntegerObj { Value = il.Value };
                Emit(Opcode.OpConstant, AddConstant(integer));
                break;
            }

            case StringLiteral sl:
            {
                var str = new StringObj { Value = sl.Value };
                Emit(Opcode.OpConstant, AddConstant(str));
                break;
            }

            case ArrayLiteral al:
            {
                foreach (var el in al.Elements)
                {
                    var err = Compile(el);
                    if (err != null) return err;
                }
                Emit(Opcode.OpArray, al.Elements.Count);
                break;
            }

            case HashLiteral hl:
            {
                // Sort keys by string representation for deterministic output
                var sortedPairs = hl.Pairs
                    .OrderBy(p => p.Key.String())
                    .ToList();

                foreach (var pair in sortedPairs)
                {
                    var err = Compile(pair.Key);
                    if (err != null) return err;
                    err = Compile(pair.Value);
                    if (err != null) return err;
                }

                Emit(Opcode.OpHash, hl.Pairs.Count * 2);
                break;
            }

            case IndexExpression idx:
            {
                var err = Compile(idx.Left);
                if (err != null) return err;
                err = Compile(idx.Index);
                if (err != null) return err;
                Emit(Opcode.OpIndex);
                break;
            }
        }

        return null;
    }

    private int AddConstant(IObject obj)
    {
        _constants.Add(obj);
        return _constants.Count - 1;
    }

    public int Emit(Opcode op, params int[] operands)
    {
        var ins = Code.Make(op, operands);
        var pos = AddInstruction(ins);
        SetLastInstruction(op, pos);
        return pos;
    }

    private int AddInstruction(byte[] ins)
    {
        var posNewInstruction = CurrentInstructions().Count;
        CurrentInstructions().AddRange(ins);
        return posNewInstruction;
    }

    private void SetLastInstruction(Opcode op, int pos)
    {
        var previous = _scopes[_scopeIndex].LastInstruction;
        var last = new EmittedInstruction { Opcode = op, Position = pos };

        _scopes[_scopeIndex].PreviousInstruction = previous;
        _scopes[_scopeIndex].LastInstruction = last;
    }

    private bool LastInstructionIs(Opcode op)
    {
        if (CurrentInstructions().Count == 0)
            return false;

        return _scopes[_scopeIndex].LastInstruction.Opcode == op;
    }

    private void RemoveLastPop()
    {
        var last = _scopes[_scopeIndex].LastInstruction;
        var previous = _scopes[_scopeIndex].PreviousInstruction;

        var ins = CurrentInstructions();
        ins.Bytes.RemoveRange(last.Position, ins.Count - last.Position);

        _scopes[_scopeIndex].LastInstruction = previous;
    }

    private void ReplaceInstruction(int pos, byte[] newInstruction)
    {
        var ins = CurrentInstructions();
        for (var i = 0; i < newInstruction.Length; i++)
        {
            ins[pos + i] = newInstruction[i];
        }
    }

    private void ReplaceLastPopWithReturn()
    {
        var lastPos = _scopes[_scopeIndex].LastInstruction.Position;
        ReplaceInstruction(lastPos, Code.Make(Opcode.OpReturnValue));
        _scopes[_scopeIndex].LastInstruction.Opcode = Opcode.OpReturnValue;
    }

    private void ChangeOperand(int opPos, int operand)
    {
        var op = (Opcode)CurrentInstructions()[opPos];
        var newInstruction = Code.Make(op, operand);
        ReplaceInstruction(opPos, newInstruction);
    }

    private Instructions CurrentInstructions()
    {
        return _scopes[_scopeIndex].Instructions;
    }

    public void EnterScope()
    {
        var scope = new CompilationScope();
        _scopes.Add(scope);
        _scopeIndex++;
        SymbolTable = SymbolTable.NewEnclosedSymbolTable(SymbolTable);
    }

    public Instructions LeaveScope()
    {
        var instructions = CurrentInstructions();
        _scopes.RemoveAt(_scopes.Count - 1);
        _scopeIndex--;
        SymbolTable = SymbolTable.Outer!;
        return instructions;
    }

    private void LoadSymbol(Symbol s)
    {
        switch (s.Scope)
        {
            case SymbolScope.Global:
                Emit(Opcode.OpGetGlobal, s.Index);
                break;
            case SymbolScope.Local:
                Emit(Opcode.OpGetLocal, s.Index);
                break;
            case SymbolScope.Builtin:
                Emit(Opcode.OpGetBuiltin, s.Index);
                break;
            case SymbolScope.Free:
                Emit(Opcode.OpGetFree, s.Index);
                break;
            case SymbolScope.Function:
                Emit(Opcode.OpCurrentClosure);
                break;
        }
    }
}
