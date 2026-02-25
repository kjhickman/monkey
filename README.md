# Kong

Kong is a small, for-fun programming language implemented in C# that runs on the .NET CLR

## Example

```text
let age = 10;
let isAdult = fn(x) { x > 18 };

let multiply = fn(a, b) { a * b };
let double = fn(x) { multiply(x, 2) };

let result = if (isAdult(age)) {
    double(age)
} else {
    age + 5
};

puts(result); // Outputs: 15
```

## Running

### Requirements

- .NET 10
- just (optional, but makes testing the cli easier)

### Run CLI via `just`

```bash
just kong --help
just kong run <file>
```

Any arguments passed after `just kong` are forwarded to the Kong CLI.

## Build & Test

```bash
dotnet build
dotnet test
```

## Acknowledgements

The initial implementation of this language started as a follow-along project creating the "Monkey" language from Thorsten Ball's books *Writing An Interpreter In Go* and *Writing A Compiler In Go* (hence the name "Kong").
