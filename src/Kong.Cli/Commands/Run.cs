using DotMake.CommandLine;
using Kong.Compiler;
using Kong.Object;
using Kong.Symbols;

namespace Kong.Cli.Commands;

[CliCommand(Name = "run", Description = "Run a file", Parent = typeof(Root))]
public class RunFile
{
    [CliArgument(Description = "File to run", Required = true)]
    public string File { get; set; } = null!;

    public void Run(CliContext context)
    {
        if (!System.IO.File.Exists(File))
        {
            Console.Error.WriteLine($"File not found: {File}");
            return;
        }

        var source = System.IO.File.ReadAllText(File);
        var lexer = new Lexer.Lexer(source);
        var parser = new Parser.Parser(lexer);

        var program = parser.ParseProgram();
        var parserErrors = parser.Errors();
        if (parserErrors.Count != 0)
        {
            PrintParserErrors(parserErrors);
            return;
        }

        var symbolTable = SymbolTable.NewWithBuiltins();

        var compiler = Compiler.Compiler.NewWithState(symbolTable, new List<IObject>());
        var compileError = compiler.Compile(program);
        if (compileError != null)
        {
            Console.Error.WriteLine($"Woops! Compilation failed:\n {compileError}");
            return;
        }

        var bytecode = compiler.GetBytecode();
        var vm = new Vm.Vm(bytecode);
        var runtimeError = vm.Run();
        if (runtimeError != null)
        {
            Console.Error.WriteLine($"Woops! Executing bytecode failed:\n {runtimeError}");
            return;
        }

        Console.WriteLine(vm.LastPoppedStackElem().Inspect());
    }

    private static void PrintParserErrors(List<string> errors)
    {
        Console.Error.WriteLine("Whoops! We ran into some monkey business here!");
        Console.Error.WriteLine(" parser errors:");
        foreach (var msg in errors)
        {
            Console.Error.WriteLine($"\t{msg}");
        }
    }
}
