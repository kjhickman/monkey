# Kong

Kong is a small, for-fun programming language implemented in C# with static semantic checking and CLR code generation.

## Features

- C-like syntax
- Statically-typed
- Core types: `int`, `bool`, `string`, and arrays (e.g. `int[]`)
- Control flow with `if` / `else` expressions and `return`
- First-class functions
- Most constructs are expressions that return a value

## Example

```text
import System;

namespace Demo;

fn Main() {
    let sum = Add(20, 22);
    Environment.SetEnvironmentVariable("KONG_SUM", "ok");
    if (Environment.NewLine != "") {
        Console.WriteLine(sum);
    } else {
        Console.WriteLine(0);
    }
}

fn Add(a: int, b: int) -> int {
    a + b;
}
```

## Running

```bash
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- build <file>  # Build runnable CLR artifact into dist/<file-name>/
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run <file>  # Execute a .kg file
```

## Building

```bash
dotnet build
dotnet test
```

## Acknowledgements

The initial implementation of this language started as a follow-along project creating the "Monkey" language from Thorsten Ball's books *Writing An Interpreter In Go* and *Writing A Compiler In Go* (hence the name "Kong").
