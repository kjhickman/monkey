using Kong.CodeGeneration;
using Kong.Lexing;
using Kong.Parsing;

namespace Kong;

public class KongCompiler
{
    public string? CompileToAssembly(string source, string assemblyName, string outputAssembly)
    {
        var lexer = new Lexer(source);
        var parser = new Parser(lexer);
        var program = parser.ParseProgram();

        var parserErrors = parser.Errors();
        if (parserErrors.Count > 0)
        {
            return string.Join(Environment.NewLine, parserErrors);
        }

        var builder = new ClrArtifactBuilder();
        return builder.Build(program, assemblyName, outputAssembly);
    }
}
