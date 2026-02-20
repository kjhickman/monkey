namespace Kong;

public static class BuiltinNames
{
    public static IEnumerable<string> All => BuiltinRegistry.Default.GetAllPublicNames();
}
