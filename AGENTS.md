# Kong

Kong is a programming language with a bytecode compiler and stack-based virtual machine, implemented in C# targeting .NET 10.

## Project Structure

```text
src/Kong/          Core library: Lexer, Parser, AST, Compiler, Code, VM, Object system
src/Kong.Cli/      CLI entry point using DotMake.CommandLine
tests/Kong.Tests/  xUnit v3 tests covering all core components
```

The interpreter pipeline flows: **Lexer -> Parser -> AST -> Compiler -> Bytecode -> VM**.

## Key Commands

```sh
dotnet build              # Build the full solution
dotnet test               # Run all tests
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- repl        # Start the REPL
```

## Verifying Changes

Always run `dotnet build` and `dotnet test` after making changes. All tests must pass. The test suite covers the lexer, parser, AST, compiler, code/opcodes, VM execution, symbol table, and object system -- treat test failures as blocking.

## Architecture Notes

- Nullable reference types are enabled project-wide. Do not suppress nullable warnings without justification.
- The library is marked `IsAotCompatible`. Avoid emitting IL or any pattern that breaks Native AOT.
