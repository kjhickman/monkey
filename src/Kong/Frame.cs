namespace Kong;

public class Frame(ClosureObj closure, int basePointer)
{
    public ClosureObj Closure { get; } = closure;
    public int InstructionPointer { get; set; } = -1;
    public int BasePointer { get; } = basePointer;

    public Instructions Instructions() => Closure.Function.Instructions;
}
