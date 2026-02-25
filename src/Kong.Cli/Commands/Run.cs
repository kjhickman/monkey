using DotMake.CommandLine;
using Kong.CodeGeneration;

namespace Kong.Cli.Commands;

[CliCommand(Name = "run", Description = "Run a file", Parent = typeof(Root))]
public class Run
{
    [CliArgument(Description = "File to run", Required = true)]
    public string File { get; set; } = null!;

    public async Task RunAsync()
    {
        if (!System.IO.File.Exists(File))
        {
            Console.Error.WriteLine($"File not found: {File}");
            return;
        }

        var source = await System.IO.File.ReadAllTextAsync(File);
        var lexer = new Lexing.Lexer(source);
        var parser = new Parsing.Parser(lexer);

        var program = parser.ParseProgram();
        var parserErrors = parser.Errors();
        if (parserErrors.Count != 0)
        {
            PrintParserErrors(parserErrors);
            return;
        }

        var symbolTable = SymbolTable.NewSymbolTable();
        for (var i = 0; i < Builtins.All.Length; i++)
        {
            symbolTable.DefineBuiltin(i, Builtins.All[i].Name);
        }

        var compiler = Compiler.NewWithState(symbolTable, []);
        var compileError = compiler.Compile(program);
        if (compileError != null)
        {
            Console.Error.WriteLine($"Compilation failed:\n {compileError}");
            return;
        }

        var bytecode = compiler.GetBytecode();
        var vm = new Evaluating.Vm(bytecode);
        var runtimeError = vm.Run();
        if (runtimeError != null)
        {
            Console.Error.WriteLine($"Executing bytecode failed:\n {runtimeError}");
            return;
        }

        Console.WriteLine(vm.LastPoppedStackElem().Inspect());
    }

    private static void PrintParserErrors(List<string> errors)
    {
        Console.Error.WriteLine(" parser errors:");
        foreach (var msg in errors)
        {
            Console.Error.WriteLine($"\t{msg}");
        }
    }
}
