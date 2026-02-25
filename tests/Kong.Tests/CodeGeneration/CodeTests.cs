using Kong.CodeGeneration;

namespace Kong.Tests;

public class CodeTests
{
    [Fact]
    public void TestMake()
    {
        var tests = new (Opcode op, int[] operands, byte[] expected)[]
        {
            (Opcode.OpConstant, [65534], [(byte)Opcode.OpConstant, 0xFF, 0xFE]),
            (Opcode.OpAdd, [], [(byte)Opcode.OpAdd]),
            (Opcode.OpGetLocal, [255], [(byte)Opcode.OpGetLocal, 255]),
            (Opcode.OpClosure, [65534, 255], [(byte)Opcode.OpClosure, 255, 254, 255]),
        };

        foreach (var tt in tests)
        {
            var instruction = Code.Make(tt.op, tt.operands);

            Assert.Equal(tt.expected.Length, instruction.Length);

            for (var i = 0; i < tt.expected.Length; i++)
            {
                Assert.Equal(tt.expected[i], instruction[i]);
            }
        }
    }

    [Fact]
    public void TestReadOperands()
    {
        var tests = new (Opcode op, int[] operands, int bytesRead)[]
        {
            (Opcode.OpConstant, [65535], 2),
            (Opcode.OpGetLocal, [255], 1),
            (Opcode.OpClosure, [65535, 255], 3),
        };

        foreach (var tt in tests)
        {
            var instruction = Code.Make(tt.op, tt.operands);

            var def = Code.Lookup(instruction[0]);
            Assert.NotNull(def);

            var (operandsRead, n) = Code.ReadOperands(def, [.. instruction], 1);
            Assert.Equal(tt.bytesRead, n);

            for (var i = 0; i < tt.operands.Length; i++)
            {
                Assert.Equal(tt.operands[i], operandsRead[i]);
            }
        }
    }

    [Fact]
    public void TestInstructionsString()
    {
        var instructions = new byte[][]
        {
            Code.Make(Opcode.OpAdd),
            Code.Make(Opcode.OpGetLocal, 1),
            Code.Make(Opcode.OpConstant, 2),
            Code.Make(Opcode.OpConstant, 65535),
            Code.Make(Opcode.OpClosure, 65535, 255),
        };

        var expected = """
            0000 OpAdd
            0001 OpGetLocal 1
            0003 OpConstant 2
            0006 OpConstant 65535
            0009 OpClosure 65535 255

            """;
        // Normalize: remove leading whitespace from each line
        expected = string.Join("\n",
            expected.Split('\n').Select(line => line.TrimStart())) ;

        var concatted = new Instructions();
        foreach (var ins in instructions)
        {
            concatted.AddRange(ins);
        }

        Assert.Equal(expected, concatted.ToString());
    }
}
