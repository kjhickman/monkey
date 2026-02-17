namespace Kong.Tests;

public class ObjectTests
{
    [Fact]
    public void TestStringHashKey()
    {
        var hello1 = new StringObj { Value = "Hello World" };
        var hello2 = new StringObj { Value = "Hello World" };
        var diff1 = new StringObj { Value = "My name is johnny" };
        var diff2 = new StringObj { Value = "My name is johnny" };

        Assert.Equal(hello1.HashKey(), hello2.HashKey());
        Assert.Equal(diff1.HashKey(), diff2.HashKey());
        Assert.NotEqual(hello1.HashKey(), diff1.HashKey());
    }

    [Fact]
    public void TestBooleanHashKey()
    {
        var true1 = new BooleanObj { Value = true };
        var true2 = new BooleanObj { Value = true };
        var false1 = new BooleanObj { Value = false };
        var false2 = new BooleanObj { Value = false };

        Assert.Equal(true1.HashKey(), true2.HashKey());
        Assert.Equal(false1.HashKey(), false2.HashKey());
        Assert.NotEqual(true1.HashKey(), false1.HashKey());
    }

    [Fact]
    public void TestIntegerHashKey()
    {
        var one1 = new IntegerObj { Value = 1 };
        var one2 = new IntegerObj { Value = 1 };
        var two1 = new IntegerObj { Value = 2 };
        var two2 = new IntegerObj { Value = 2 };

        Assert.Equal(one1.HashKey(), one2.HashKey());
        Assert.Equal(two1.HashKey(), two2.HashKey());
        Assert.NotEqual(one1.HashKey(), two1.HashKey());
    }
}
