# Kong

Kong is a programming language implemented in C# targeting .NET 10, with a typed frontend and CLR code generation backend.

## Project Structure

```text
src/Kong/          Core library: Lexer, Parser, AST, Resolver, TypeChecker, IR, CLR emitter/runtime
src/Kong.Cli/      CLI entry point using DotMake.CommandLine
tests/Kong.Tests/  xUnit v3 tests covering all core components
```

The primary execution pipeline flows: **Lexer -> Parser -> AST -> NameResolver -> TypeChecker -> IR Lowering -> CLR Emission/Execution**.

## Key Commands

```sh
dotnet build              # Build the full solution
dotnet test               # Run all tests
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- repl        # Start the REPL
```

## Verifying Changes

Always run `dotnet build` and `dotnet test` after making changes. All tests must pass. Treat test failures as blocking.

## Architecture Notes

- Nullable reference types are enabled project-wide. Do not suppress nullable warnings without justification.
- The library is marked `IsAotCompatible`. Avoid reflection or patterns that break Native AOT.
