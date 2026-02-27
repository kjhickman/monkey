set quiet := true

# Show available just recipes
default:
    @just --list --unsorted

# Run Kong CLI and forward all args
kong *args:
    dotnet run --project src/Kong.Cli/Kong.Cli.csproj -- {{args}}
