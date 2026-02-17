cli := "src/Kong.Cli/Kong.Cli.csproj"

kong *args:
	dotnet run --project {{cli}} -- {{args}}
