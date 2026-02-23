# Todo Scanner

This sample scans the current directory (and all subdirectories) for `.kg` files and reports lines where `todo` appears after `//`.

## Run

```sh
dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- run samples/todo-scanner/Main.kg
```

## What it demonstrates

- Multi-file program structure
- Multiple namespaces with imports
- Namespace-private (`fn`) and exported (`public fn`) visibility
- Recursive directory traversal
- `for ... in` array iteration
- BCL interop (`System.IO.Directory`, `System.IO.File`, `System.IO.Path`, `System.Console`)
