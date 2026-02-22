# Kong

Kong is a programming language implemented in C# targeting .NET 10, with a typed frontend and CLR code generation backend.

## Project Structure

```text
src/Kong/
  Lexing/          Tokenization
  Parsing/         Parser and AST nodes
  Semantic/        Name resolution and type checking
  CodeGeneration/  IR lowering and CLR emitter/runtime interop
  Common/          Shared diagnostics and source span types
src/Kong.Cli/      CLI entry point
tests/Kong.Tests/  xUnit v3 tests organized by phase
```

The primary execution pipeline flows: **Lexer -> Parser -> AST -> NameResolver -> TypeChecker -> IR Lowering -> CLR Emission/Execution**.

## Key Commands

```sh
dotnet build              # Build the full solution
dotnet test               # Run all tests
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- build <file>  # Build runnable CLR artifact into dist/<file-name>/
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
```

## Verifying Changes

Always run `dotnet build` and `dotnet test` after making changes. All tests must pass. Treat test failures as blocking.

When implementing or changing a language feature, also update the integration scenario suite under `tests/Kong.IntegrationPrograms/` (or add a new scenario) so real multi-file programs exercise the new behavior.

## Architecture Notes

- Nullable reference types are enabled project-wide. Do not suppress nullable warnings without justification.
- The library is marked `IsAotCompatible`. Avoid reflection or patterns that break Native AOT.
- Preserve phase boundaries when adding code:
  - `Lexing` must not depend on `Parsing`, `Semantic`, or `CodeGeneration`.
  - `Parsing` may depend on `Lexing` and `Common`, but not `Semantic` or `CodeGeneration`.
  - `Semantic` may depend on `Parsing` and `Common`, but not `CodeGeneration`.
  - `CodeGeneration` may depend on `Semantic`, `Parsing`, and `Common`.
  - `Common` should stay dependency-light and avoid phase-specific behavior.
