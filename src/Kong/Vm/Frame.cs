using Kong.Code;
using Kong.Object;

namespace Kong.Vm;

public class Frame
{
    public ClosureObj Cl { get; }
    public int Ip { get; set; }
    public int BasePointer { get; }

    public Frame(ClosureObj cl, int basePointer)
    {
        Cl = cl;
        Ip = -1;
        BasePointer = basePointer;
    }

    public Instructions Instructions() => Cl.Fn.Instructions;
}
