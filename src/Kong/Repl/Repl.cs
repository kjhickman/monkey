using Kong.Compiler;
using Kong.Object;

namespace Kong.Repl;

public static class Repl
{
    private const string Prompt = ">> ";

    public static void Start(TextReader input, TextWriter output)
    {
        var constants = new List<IObject>();
        var globals = new IObject[Vm.Vm.GlobalsSize];
        var symbolTable = SymbolTable.NewSymbolTable();

        for (var i = 0; i < Builtins.All.Length; i++)
        {
            symbolTable.DefineBuiltin(i, Builtins.All[i].Name);
        }

        while (true)
        {
            output.Write(Prompt);
            output.Flush();

            var line = input.ReadLine();
            if (line == null)
                return;

            var l = new Lexer.Lexer(line);
            var p = new Parser.Parser(l);

            var program = p.ParseProgram();
            if (p.Errors().Count != 0)
            {
                PrintParserErrors(output, p.Errors());
                continue;
            }

            var comp = Compiler.Compiler.NewWithState(symbolTable, constants);
            var err = comp.Compile(program);
            if (err != null)
            {
                output.WriteLine($"Woops! Compilation failed:\n {err}");
                continue;
            }

            var code = comp.GetBytecode();
            constants = code.Constants;

            var machine = Vm.Vm.NewWithGlobalsStore(code, globals);
            err = machine.Run();
            if (err != null)
            {
                output.WriteLine($"Woops! Executing bytecode failed:\n {err}");
                continue;
            }

            var stackTop = machine.LastPoppedStackElem();
            output.WriteLine(stackTop.Inspect());
        }
    }

    private static void PrintParserErrors(TextWriter output, List<string> errors)
    {
        output.WriteLine("Whoops! We ran into some monkey business here!");
        output.WriteLine(" parser errors:");
        foreach (var msg in errors)
        {
            output.WriteLine($"\t{msg}");
        }
    }
}
