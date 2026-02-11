using System.Buffers.Binary;
using System.Text;

namespace Kong.Code;

public enum Opcode : byte
{
    OpConstant = 0,
    OpAdd,
    OpSub,
    OpMul,
    OpDiv,
    OpTrue,
    OpFalse,
    OpPop,
    OpEqual,
    OpNotEqual,
    OpGreaterThan,
    OpMinus,
    OpBang,
    OpJumpNotTruthy,
    OpJump,
    OpNull,
    OpGetGlobal,
    OpSetGlobal,
    OpCall,
    OpReturnValue,
    OpReturn,
    OpGetLocal,
    OpSetLocal,
    OpGetBuiltin,
    OpClosure,
    OpGetFree,
    OpCurrentClosure,
    OpArray,
    OpHash,
    OpIndex,
}

public class Definition
{
    public string Name { get; }
    public int[] OperandWidths { get; }

    public Definition(string name, params int[] operandWidths)
    {
        Name = name;
        OperandWidths = operandWidths;
    }
}

public class Instructions
{
    public List<byte> Bytes { get; }

    public Instructions() => Bytes = [];
    public Instructions(byte[] bytes) => Bytes = [.. bytes];
    public Instructions(List<byte> bytes) => Bytes = bytes;

    public int Count => Bytes.Count;
    public byte this[int index]
    {
        get => Bytes[index];
        set => Bytes[index] = value;
    }

    public void AddRange(byte[] bytes) => Bytes.AddRange(bytes);
    public byte[] ToArray() => [.. Bytes];

    public override string ToString()
    {
        var sb = new StringBuilder();

        var i = 0;
        while (i < Bytes.Count)
        {
            var def = Code.Lookup(Bytes[i]);
            if (def == null)
            {
                sb.AppendLine($"ERROR: opcode {Bytes[i]} undefined");
                i++;
                continue;
            }

            var (operands, read) = Code.ReadOperands(def, Bytes, i + 1);
            sb.AppendLine($"{i:D4} {FmtInstruction(def, operands)}");
            i += 1 + read;
        }

        return sb.ToString();
    }

    private static string FmtInstruction(Definition def, int[] operands)
    {
        var operandCount = def.OperandWidths.Length;

        if (operands.Length != operandCount)
        {
            return $"ERROR: operand len {operands.Length} does not match defined {operandCount}\n";
        }

        return operandCount switch
        {
            0 => def.Name,
            1 => $"{def.Name} {operands[0]}",
            2 => $"{def.Name} {operands[0]} {operands[1]}",
            _ => $"ERROR: unhandled operandCount for {def.Name}\n",
        };
    }
}

public static class Code
{
    private static readonly Dictionary<Opcode, Definition> Definitions = new()
    {
        { Opcode.OpConstant,       new Definition("OpConstant", 2) },
        { Opcode.OpAdd,            new Definition("OpAdd") },
        { Opcode.OpSub,            new Definition("OpSub") },
        { Opcode.OpMul,            new Definition("OpMul") },
        { Opcode.OpDiv,            new Definition("OpDiv") },
        { Opcode.OpTrue,           new Definition("OpTrue") },
        { Opcode.OpFalse,          new Definition("OpFalse") },
        { Opcode.OpPop,            new Definition("OpPop") },
        { Opcode.OpEqual,          new Definition("OpEqual") },
        { Opcode.OpNotEqual,       new Definition("OpNotEqual") },
        { Opcode.OpGreaterThan,    new Definition("OpGreaterThan") },
        { Opcode.OpMinus,          new Definition("OpMinus") },
        { Opcode.OpBang,           new Definition("OpBang") },
        { Opcode.OpJumpNotTruthy,  new Definition("OpJumpNotTruthy", 2) },
        { Opcode.OpJump,           new Definition("OpJump", 2) },
        { Opcode.OpNull,           new Definition("OpNull") },
        { Opcode.OpGetGlobal,      new Definition("OpGetGlobal", 2) },
        { Opcode.OpSetGlobal,      new Definition("OpSetGlobal", 2) },
        { Opcode.OpCall,           new Definition("OpCall", 1) },
        { Opcode.OpReturnValue,    new Definition("OpReturnValue") },
        { Opcode.OpReturn,         new Definition("OpReturn") },
        { Opcode.OpGetLocal,       new Definition("OpGetLocal", 1) },
        { Opcode.OpSetLocal,       new Definition("OpSetLocal", 1) },
        { Opcode.OpGetBuiltin,     new Definition("OpGetBuiltin", 1) },
        { Opcode.OpClosure,        new Definition("OpClosure", 2, 1) },
        { Opcode.OpGetFree,        new Definition("OpGetFree", 1) },
        { Opcode.OpCurrentClosure, new Definition("OpCurrentClosure") },
        { Opcode.OpArray,          new Definition("OpArray", 2) },
        { Opcode.OpHash,           new Definition("OpHash", 2) },
        { Opcode.OpIndex,          new Definition("OpIndex") },
    };

    public static Definition? Lookup(byte op)
    {
        return Definitions.TryGetValue((Opcode)op, out var def) ? def : null;
    }

    public static byte[] Make(Opcode op, params int[] operands)
    {
        if (!Definitions.TryGetValue(op, out var def))
        {
            return [];
        }

        var instructionLen = 1;
        foreach (var w in def.OperandWidths)
        {
            instructionLen += w;
        }

        var instruction = new byte[instructionLen];
        instruction[0] = (byte)op;

        var offset = 1;
        for (var i = 0; i < operands.Length; i++)
        {
            var width = def.OperandWidths[i];
            switch (width)
            {
                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(instruction.AsSpan(offset), (ushort)operands[i]);
                    break;
                case 1:
                    instruction[offset] = (byte)operands[i];
                    break;
            }
            offset += width;
        }

        return instruction;
    }

    public static (int[] operands, int bytesRead) ReadOperands(Definition def, List<byte> bytes, int startOffset)
    {
        var operands = new int[def.OperandWidths.Length];
        var offset = 0;

        for (var i = 0; i < def.OperandWidths.Length; i++)
        {
            var width = def.OperandWidths[i];
            switch (width)
            {
                case 2:
                    operands[i] = ReadUint16(bytes, startOffset + offset);
                    break;
                case 1:
                    operands[i] = bytes[startOffset + offset];
                    break;
            }
            offset += width;
        }

        return (operands, offset);
    }

    public static int ReadUint16(List<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(
            new ReadOnlySpan<byte>([bytes[offset], bytes[offset + 1]]));
    }

    public static byte ReadUint8(List<byte> bytes, int offset)
    {
        return bytes[offset];
    }
}
