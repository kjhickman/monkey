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
            insertIndex = SkipWhitespace(source, insertIndex);
            if (!IsImportAt(source, insertIndex))
            {
                break;
            }

            insertIndex += "import".Length;
            insertIndex = SkipWhitespace(source, insertIndex);

            if (!TryConsumeQualifiedName(source, ref insertIndex))
            {
                break;
            }
        }

        return source.Insert(insertIndex, $" namespace {ns} ");
    }

    private static int SkipWhitespace(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsImportAt(string source, int index)
    {
        if (index < 0 || index + "import".Length > source.Length)
        {
            return false;
        }

        if (!source.AsSpan(index, "import".Length).SequenceEqual("import"))
        {
            return false;
        }

        return index + "import".Length < source.Length && char.IsWhiteSpace(source[index + "import".Length]);
    }

    private static bool TryConsumeQualifiedName(string source, ref int index)
    {
        if (!TryConsumeIdentifier(source, ref index))
        {
            return false;
        }

        while (index < source.Length && source[index] == '.')
        {
            index++;
            if (!TryConsumeIdentifier(source, ref index))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryConsumeIdentifier(string source, ref int index)
    {
        if (index >= source.Length || !IsIdentifierStart(source[index]))
        {
            return false;
        }

        index++;
        while (index < source.Length && IsIdentifierPart(source[index]))
        {
            index++;
        }

        return true;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return ch == '_' || char.IsLetter(ch);
    }

    private static bool IsIdentifierPart(char ch)
    {
        return ch == '_' || char.IsLetterOrDigit(ch);
    }
}
