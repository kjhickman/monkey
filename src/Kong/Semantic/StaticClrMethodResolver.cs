using System.Reflection;

namespace Kong.Semantic;

public sealed record StaticClrMethodBinding(
    string MethodPath,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType,
    MethodInfo MethodInfo);

public static class StaticClrMethodResolver
{
    private static readonly IReadOnlyList<StaticClrMethodBinding> Bindings =
    [
        new StaticClrMethodBinding(
            "System.Console.WriteLine",
            [],
            TypeSymbols.Void,
            typeof(Console).GetMethod(nameof(Console.WriteLine), Type.EmptyTypes)!),
        new StaticClrMethodBinding(
            "System.Console.WriteLine",
            [TypeSymbols.Int],
            TypeSymbols.Void,
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(long)])!),
        new StaticClrMethodBinding(
            "System.Console.WriteLine",
            [TypeSymbols.String],
            TypeSymbols.Void,
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.Console.WriteLine",
            [TypeSymbols.Bool],
            TypeSymbols.Void,
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(bool)])!),
        new StaticClrMethodBinding(
            "System.Math.Abs",
            [TypeSymbols.Int],
            TypeSymbols.Int,
            typeof(Math).GetMethod(nameof(Math.Abs), [typeof(long)])!),
        new StaticClrMethodBinding(
            "System.Math.Max",
            [TypeSymbols.Int, TypeSymbols.Int],
            TypeSymbols.Int,
            typeof(Math).GetMethod(nameof(Math.Max), [typeof(long), typeof(long)])!),
        new StaticClrMethodBinding(
            "System.Math.Min",
            [TypeSymbols.Int, TypeSymbols.Int],
            TypeSymbols.Int,
            typeof(Math).GetMethod(nameof(Math.Min), [typeof(long), typeof(long)])!),
        new StaticClrMethodBinding(
            "System.IO.File.ReadAllText",
            [TypeSymbols.String],
            TypeSymbols.String,
            typeof(File).GetMethod(nameof(File.ReadAllText), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.File.WriteAllText",
            [TypeSymbols.String, TypeSymbols.String],
            TypeSymbols.Void,
            typeof(File).GetMethod(nameof(File.WriteAllText), [typeof(string), typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.File.Exists",
            [TypeSymbols.String],
            TypeSymbols.Bool,
            typeof(File).GetMethod(nameof(File.Exists), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.File.Delete",
            [TypeSymbols.String],
            TypeSymbols.Void,
            typeof(File).GetMethod(nameof(File.Delete), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.Path.Combine",
            [TypeSymbols.String, TypeSymbols.String],
            TypeSymbols.String,
            typeof(Path).GetMethod(nameof(Path.Combine), [typeof(string), typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.Path.GetFileName",
            [TypeSymbols.String],
            TypeSymbols.String,
            typeof(Path).GetMethod(nameof(Path.GetFileName), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.Directory.Exists",
            [TypeSymbols.String],
            TypeSymbols.Bool,
            typeof(Directory).GetMethod(nameof(Directory.Exists), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.Directory.Delete",
            [TypeSymbols.String],
            TypeSymbols.Void,
            typeof(Directory).GetMethod(nameof(Directory.Delete), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.IO.Directory.GetCurrentDirectory",
            [],
            TypeSymbols.String,
            typeof(Directory).GetMethod(nameof(Directory.GetCurrentDirectory), Type.EmptyTypes)!),
        new StaticClrMethodBinding(
            "System.Environment.GetEnvironmentVariable",
            [TypeSymbols.String],
            TypeSymbols.String,
            typeof(Environment).GetMethod(nameof(Environment.GetEnvironmentVariable), [typeof(string)])!),
        new StaticClrMethodBinding(
            "System.Environment.SetEnvironmentVariable",
            [TypeSymbols.String, TypeSymbols.String],
            TypeSymbols.Void,
            typeof(Environment).GetMethod(nameof(Environment.SetEnvironmentVariable), [typeof(string), typeof(string)])!),
    ];

    public static bool IsKnownMethodPath(string methodPath)
    {
        return Bindings.Any(binding => binding.MethodPath == methodPath);
    }

    public static StaticClrMethodBinding? Resolve(
        string methodPath,
        IReadOnlyList<TypeSymbol> parameterTypes)
    {
        return Bindings.FirstOrDefault(binding =>
            binding.MethodPath == methodPath &&
            ParametersMatch(binding.ParameterTypes, parameterTypes));
    }

    private static bool ParametersMatch(
        IReadOnlyList<TypeSymbol> expected,
        IReadOnlyList<TypeSymbol> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (expected[i] != actual[i])
            {
                return false;
            }
        }

        return true;
    }
}
