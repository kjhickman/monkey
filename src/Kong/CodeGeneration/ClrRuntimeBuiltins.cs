namespace Kong.CodeGeneration;

public static class ClrRuntimeBuiltins
{
    public static void PutsInt(long value)
    {
        Console.WriteLine(value);
    }

    public static void PutsString(string value)
    {
        Console.WriteLine(value);
    }

    public static void PutsBool(bool value)
    {
        Console.WriteLine(value);
    }

    public static long LenString(string value)
    {
        return value.Length;
    }

    public static long FirstIntArray(long[] values)
    {
        if (values.Length == 0)
        {
            throw new InvalidOperationException("first() called on empty array");
        }

        return values[0];
    }

    public static long LastIntArray(long[] values)
    {
        if (values.Length == 0)
        {
            throw new InvalidOperationException("last() called on empty array");
        }

        return values[^1];
    }

    public static long[] RestIntArray(long[] values)
    {
        if (values.Length <= 1)
        {
            return [];
        }

        var rest = new long[values.Length - 1];
        Array.Copy(values, 1, rest, 0, rest.Length);
        return rest;
    }

    public static long[] PushIntArray(long[] values, long item)
    {
        var result = new long[values.Length + 1];
        Array.Copy(values, result, values.Length);
        result[^1] = item;
        return result;
    }
}
