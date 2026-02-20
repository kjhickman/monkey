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
let age = 1;
let message = "Hello, world!";
let result = [1, 2, 3];

let add = fn(a: int, b: int) -> int {
    a + b;
};

let multiply = fn(x: int, y: int) -> int {
    return x * y;
};

add(10, multiply(2, 2)); // Returns 14
```

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

## Acknowledgements

The initial implementation of this language started as a follow-along project creating the "Monkey" language from Thorsten Ball's books *Writing An Interpreter In Go* and *Writing A Compiler In Go* (hence the name "Kong").
