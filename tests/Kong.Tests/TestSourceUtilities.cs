namespace Kong.Tests;

public static class TestSourceUtilities
{
    public static string EnsureFileScopedNamespace(string source, string ns = "Test")
    {
        if (source.Contains("namespace ", StringComparison.Ordinal))
        {
            return source;
        }

        var insertIndex = 0;
        while (true)
        {
            var remainder = source[insertIndex..].TrimStart();
            var skipped = source[insertIndex..].Length - remainder.Length;
            insertIndex += skipped;

            if (!source[insertIndex..].StartsWith("import ", StringComparison.Ordinal))
            {
                break;
            }

            var semicolonIndex = source.IndexOf(';', insertIndex);
            if (semicolonIndex < 0)
            {
                break;
            }

            insertIndex = semicolonIndex + 1;
        }

        return source.Insert(insertIndex, $" namespace {ns}; ");
    }
}
