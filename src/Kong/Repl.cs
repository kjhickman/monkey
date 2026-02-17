namespace Kong;

public static class Repl
{
    private const string Prompt = ">> ";

    public static void Start(TextReader input, TextWriter output)
    {
        var constants = new List<IObject>();
        var globals = new IObject[Vm.GlobalsSize];
        var symbolTable = SymbolTable.NewWithBuiltins();

        while (true)
        {
            output.Write(Prompt);
            output.Flush();

            var line = input.ReadLine();
            if (line == null)
                return;

            var l = new Lexer(line);
            var p = new Parser(l);

            var unit = p.ParseCompilationUnit();
            if (p.Diagnostics.HasErrors)
            {
                PrintDiagnostics(output, p.Diagnostics);
                continue;
            }

            var comp = Compiler.NewWithState(symbolTable, constants);
            comp.Compile(unit);
            if (comp.Diagnostics.HasErrors)
            {
                PrintDiagnostics(output, comp.Diagnostics);
                continue;
            }

            var code = comp.GetBytecode();
            constants = code.Constants;

            var machine = Vm.NewWithGlobalsStore(code, globals);
            machine.Run();
            if (machine.Diagnostics.HasErrors)
            {
                PrintDiagnostics(output, machine.Diagnostics);
                continue;
            }

            var stackTop = machine.LastPoppedStackElem();
            output.WriteLine(stackTop.Inspect());
        }
    }

    private static void PrintDiagnostics(TextWriter output, DiagnosticBag diagnostics)
    {
        output.WriteLine("Whoops! We ran into some monkey business here!");
        foreach (var d in diagnostics.All)
        {
            output.WriteLine($"\t{d}");
        }
    }
}
