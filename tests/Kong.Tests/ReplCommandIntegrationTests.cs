using ReplCommand = Kong.Cli.Commands.Repl;

namespace Kong.Tests;

public class ReplCommandIntegrationTests
{
    [Fact]
    public void TestReplCommandUsesClrBackendByDefault()
    {
        var output = ExecuteRepl("1 + 1;\n");

        Assert.Contains("2", output);
    }

    [Fact]
    public void TestReplCommandDoesNotFallbackToVmByDefault()
    {
        var output = ExecuteRepl("\"hello\";\n");

        Assert.Contains("[IR001]", output);
    }

    private static string ExecuteRepl(string input)
    {
        var command = new ReplCommand();
        var originalIn = Console.In;
        var originalOut = Console.Out;
        var reader = new StringReader(input);
        var writer = new StringWriter();

        try
        {
            Console.SetIn(reader);
            Console.SetOut(writer);
            command.Run(null!);
            return writer.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
