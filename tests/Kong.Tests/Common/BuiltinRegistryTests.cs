using Kong.Common;
using Kong.Semantic;

namespace Kong.Tests.Common;

public class BuiltinRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersAllStandardBuiltins()
    {
        var registry = BuiltinRegistry.CreateDefault();

        Assert.True(registry.IsDefined("puts"));
        Assert.True(registry.IsDefined("len"));
        Assert.True(registry.IsDefined("first"));
        Assert.True(registry.IsDefined("last"));
        Assert.True(registry.IsDefined("rest"));
        Assert.True(registry.IsDefined("push"));
    }

    [Fact]
    public void ResolveByTypeSignature_PutsInt_ResolvesCorrectBuiltin()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("puts", [TypeSymbols.Int]);

        Assert.NotNull(binding);
        Assert.Equal("__builtin_puts_int", binding.Signature.IrFunctionName);
        Assert.Equal(TypeSymbols.Void, binding.Signature.ReturnType);
    }

    [Fact]
    public void ResolveByTypeSignature_PutsString_ResolvesCorrectBuiltin()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("puts", [TypeSymbols.String]);

        Assert.NotNull(binding);
        Assert.Equal("__builtin_puts_string", binding.Signature.IrFunctionName);
    }

    [Fact]
    public void ResolveByTypeSignature_PutsBool_ResolvesCorrectBuiltin()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("puts", [TypeSymbols.Bool]);

        Assert.NotNull(binding);
        Assert.Equal("__builtin_puts_bool", binding.Signature.IrFunctionName);
    }

    [Fact]
    public void ResolveByTypeSignature_LenString_ResolvesCorrectBuiltin()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("len", [TypeSymbols.String]);

        Assert.NotNull(binding);
        Assert.Equal("__builtin_len_string", binding.Signature.IrFunctionName);
        Assert.Equal(TypeSymbols.Int, binding.Signature.ReturnType);
    }

    [Fact]
    public void ResolveByTypeSignature_FirstIntArray_ResolvesCorrectBuiltin()
    {
        var registry = BuiltinRegistry.CreateDefault();
        var intArrayType = new ArrayTypeSymbol(TypeSymbols.Int);

        var binding = registry.ResolveByTypeSignature("first", [intArrayType]);

        Assert.NotNull(binding);
        Assert.Equal("__builtin_first_int_array", binding.Signature.IrFunctionName);
        Assert.Equal(TypeSymbols.Int, binding.Signature.ReturnType);
    }

    [Fact]
    public void ResolveByTypeSignature_InvalidSignature_ReturnsNull()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("puts", [TypeSymbols.Bool, TypeSymbols.Int]);

        Assert.Null(binding);
    }

    [Fact]
    public void ResolveByTypeSignature_UnknownBuiltin_ReturnsNull()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var binding = registry.ResolveByTypeSignature("unknown_func", [TypeSymbols.Int]);

        Assert.Null(binding);
    }

    [Fact]
    public void Register_CustomBuiltin_CanBeResolved()
    {
        var registry = new BuiltinRegistry();
        var signature = new BuiltinSignature("custom", "__builtin_custom", [TypeSymbols.Int], TypeSymbols.String);
        var binding = new BuiltinBinding(signature, "CustomMethod", typeof(object));

        registry.Register(binding);
        var resolved = registry.ResolveByTypeSignature("custom", [TypeSymbols.Int]);

        Assert.NotNull(resolved);
        Assert.Equal("__builtin_custom", resolved.Signature.IrFunctionName);
    }

    [Fact]
    public void GetAllPublicNames_ReturnsAllRegisteredNames()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var names = registry.GetAllPublicNames().ToList();

        Assert.Contains("puts", names);
        Assert.Contains("len", names);
        Assert.Contains("first", names);
        Assert.Contains("last", names);
        Assert.Contains("rest", names);
        Assert.Contains("push", names);
    }

    [Fact]
    public void GetBindingsForName_ReturnsAllOverloads()
    {
        var registry = BuiltinRegistry.CreateDefault();

        var bindings = registry.GetBindingsForName("puts").ToList();

        Assert.Equal(3, bindings.Count);
        Assert.Contains(bindings, b => b.Signature.IrFunctionName == "__builtin_puts_int");
        Assert.Contains(bindings, b => b.Signature.IrFunctionName == "__builtin_puts_string");
        Assert.Contains(bindings, b => b.Signature.IrFunctionName == "__builtin_puts_bool");
    }
}
