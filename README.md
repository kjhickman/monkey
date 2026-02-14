# Kong

Kong is a programming language with a bytecode compiler and stack-based virtual machine, implemented in C# targeting .NET 10.

## Running

```bash
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- repl        # Start the REPL
```

## Building

```bash
dotnet build
dotnet test
```
