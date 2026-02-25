using System.Text;

namespace Kong.CodeGeneration;

public enum ObjectType
{
    Integer,
    Boolean,
    Null,
    ReturnValue,
    Error,
    String,
    Builtin,
    Array,
    Hash,
    CompiledFunction,
    Closure,
}

public interface IObject
{
    ObjectType Type();
    string Inspect();
}

public interface IHashable
{
    HashKey HashKey();
}

public readonly record struct HashKey(ObjectType Type, ulong Value);

// --- Object types ---

public class NullObj : IObject
{
    public ObjectType Type() => ObjectType.Null;
    public string Inspect() => "null";
}

public class IntegerObj : IObject, IHashable
{
    public long Value { get; set; }

    public ObjectType Type() => ObjectType.Integer;
    public string Inspect() => Value.ToString();

    public HashKey HashKey() => new(ObjectType.Integer, (ulong)Value);
}

public class BooleanObj : IObject, IHashable
{
    public bool Value { get; set; }

    public ObjectType Type() => ObjectType.Boolean;
    public string Inspect() => Value ? "true" : "false";

    public HashKey HashKey() => new(ObjectType.Boolean, Value ? 1UL : 0UL);
}

public class ReturnValueObj : IObject
{
    public IObject Value { get; set; } = null!;

    public ObjectType Type() => ObjectType.ReturnValue;
    public string Inspect() => Value.Inspect();
}

public class ErrorObj : IObject
{
    public string Message { get; set; } = "";

    public ObjectType Type() => ObjectType.Error;
    public string Inspect() => $"ERROR: {Message}";
}

public class StringObj : IObject, IHashable
{
    public string Value { get; set; } = "";

    public ObjectType Type() => ObjectType.String;
    public string Inspect() => Value;

    public HashKey HashKey()
    {
        // FNV-1a 64-bit hash to match Go's hash/fnv.New64a()
        const ulong fnvOffsetBasis = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        var hash = fnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(Value))
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        return new HashKey(ObjectType.String, hash);
    }
}

public delegate IObject? BuiltinFunction(params IObject[] args);

public class BuiltinObj : IObject
{
    public BuiltinFunction Fn { get; set; } = null!;

    public ObjectType Type() => ObjectType.Builtin;
    public string Inspect() => "builtin function";
}

public class ArrayObj : IObject
{
    public List<IObject> Elements { get; set; } = [];

    public ObjectType Type() => ObjectType.Array;
    public string Inspect()
    {
        var elements = Elements.Select(e => e.Inspect());
        return $"[{string.Join(", ", elements)}]";
    }
}

public class HashPair
{
    public IObject Key { get; set; } = null!;
    public IObject Value { get; set; } = null!;
}

public class HashObj : IObject
{
    public Dictionary<HashKey, HashPair> Pairs { get; set; } = [];

    public ObjectType Type() => ObjectType.Hash;
    public string Inspect()
    {
        var pairs = Pairs.Values.Select(p => $"{p.Key.Inspect()}: {p.Value.Inspect()}");
        return $"{{{string.Join(", ", pairs)}}}";
    }
}

public class CompiledFunctionObj : IObject
{
    public Instructions Instructions { get; set; } = new();
    public int NumLocals { get; set; }
    public int NumParameters { get; set; }

    public ObjectType Type() => ObjectType.CompiledFunction;
    public string Inspect() => $"CompiledFunction[{GetHashCode()}]";
}

public class ClosureObj : IObject
{
    public CompiledFunctionObj Fn { get; set; } = null!;
    public List<IObject> Free { get; set; } = [];

    public ObjectType Type() => ObjectType.Closure;
    public string Inspect() => $"Closure[{GetHashCode()}]";
}

// --- Builtins (index-sensitive! compiler uses index for OpGetBuiltin) ---

public static class Builtins
{
    private static ErrorObj NewError(string message) => new() { Message = message };

    public static readonly (string Name, BuiltinObj Builtin)[] All =
    [
        // 0: len
        ("len", new BuiltinObj
        {
            Fn = args =>
            {
                if (args.Length != 1)
                    return NewError($"wrong number of arguments. got={args.Length}, want=1");

                return args[0] switch
                {
                    ArrayObj arr => new IntegerObj { Value = arr.Elements.Count },
                    StringObj str => new IntegerObj { Value = str.Value.Length },
                    _ => NewError($"argument to `len` not supported, got {args[0].Type()}"),
                };
            }
        }),

        // 1: puts
        ("puts", new BuiltinObj
        {
            Fn = args =>
            {
                foreach (var arg in args)
                {
                    Console.WriteLine(arg.Inspect());
                }
                return null;
            }
        }),

        // 2: first
        ("first", new BuiltinObj
        {
            Fn = args =>
            {
                if (args.Length != 1)
                    return NewError($"wrong number of arguments. got={args.Length}, want=1");

                if (args[0] is not ArrayObj arr)
                    return NewError($"argument to `first` must be ARRAY, got {args[0].Type()}");

                if (arr.Elements.Count > 0)
                    return arr.Elements[0];

                return null;
            }
        }),

        // 3: last
        ("last", new BuiltinObj
        {
            Fn = args =>
            {
                if (args.Length != 1)
                    return NewError($"wrong number of arguments. got={args.Length}, want=1");

                if (args[0] is not ArrayObj arr)
                    return NewError($"argument to `last` must be ARRAY, got {args[0].Type()}");

                if (arr.Elements.Count > 0)
                    return arr.Elements[^1];

                return null;
            }
        }),

        // 4: rest
        ("rest", new BuiltinObj
        {
            Fn = args =>
            {
                if (args.Length != 1)
                    return NewError($"wrong number of arguments. got={args.Length}, want=1");

                if (args[0] is not ArrayObj arr)
                    return NewError($"argument to `rest` must be ARRAY, got {args[0].Type()}");

                if (arr.Elements.Count > 0)
                {
                    var newElements = arr.Elements.Skip(1).ToList();
                    return new ArrayObj { Elements = newElements };
                }

                return null;
            }
        }),

        // 5: push
        ("push", new BuiltinObj
        {
            Fn = args =>
            {
                if (args.Length != 2)
                    return NewError($"wrong number of arguments. got={args.Length}, want=2");

                if (args[0] is not ArrayObj arr)
                    return NewError($"argument to `push` must be ARRAY, got {args[0].Type()}");

                var newElements = new List<IObject>(arr.Elements) { args[1] };
                return new ArrayObj { Elements = newElements };
            }
        }),
    ];

    public static BuiltinObj? GetBuiltinByName(string name)
    {
        foreach (var (n, builtin) in All)
        {
            if (n == name) return builtin;
        }
        return null;
    }
}
