# Kong

Kong is a programming language that compiles to .NET CLR IL, implemented in C# targeting .NET 10.

## Project Structure

```text
src/Kong/          Core library: Lexer, Parser, AST, Semantics, CLR code generation
src/Kong.Cli/      CLI entry point using DotMake.CommandLine
tests/Kong.Tests/  xUnit v3 tests covering all core components
```

The compilation pipeline flows: **Lexer -> Parser -> AST -> Type Inference -> IL Compiler -> CLR Assembly**.

## Key Commands

```sh
dotnet build              # Build the full solution
dotnet test               # Run all tests
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
```

## Verifying Changes

Always run `dotnet build` and `dotnet test` after making changes. All tests must pass. The test suite covers lexer, parser, AST, semantic type inference, and CLR code generation/execution -- treat test failures as blocking.

## Architecture Notes

- Nullable reference types are enabled project-wide. Do not suppress nullable warnings without justification.
- The library is marked `IsAotCompatible`. Avoid emitting IL or any pattern that breaks Native AOT.
