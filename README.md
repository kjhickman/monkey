# Kong

Kong is a small, for-fun programming language implemented in C# that runs on the .NET CLR

## Example

```text
let baseScore = 18;
let hitTarget = fn(points: int) { points > 15 };

let makeMultiplier = fn(factor: int) {
    fn(points: int) { points * factor }
};
let bonus = makeMultiplier(2);

let result = if (hitTarget(baseScore)) {
    bonus(baseScore)
} else {
    baseScore + 5
};

puts(result); // Outputs: 36
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
