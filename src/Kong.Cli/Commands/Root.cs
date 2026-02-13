using DotMake.CommandLine;

namespace Kong.Cli.Commands;

[CliCommand(Description = "Kong CLI", ShortFormAutoGenerate = CliNameAutoGenerate.Options)]
public class Root
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
