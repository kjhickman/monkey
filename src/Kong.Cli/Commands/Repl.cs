using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Name = "repl", Description = "Start the Kong REPL", Parent = typeof(Root))]
public class Repl
{
    public void Run(CliContext context)
    {
        var username = Environment.UserName;
        Console.WriteLine($"Hello {username}! This is the Kong programming language!");
        Console.WriteLine("Feel free to type in commands");
        Kong.Repl.Repl.Start(Console.In, Console.Out);
    }
}
