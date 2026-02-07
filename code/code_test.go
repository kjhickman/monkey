package code

import "testing"

func TestMake(t *testing.T) {
	tests := []struct {
		op       Opcode
		operands []int
		expected []byte
	}{
		{OpConstant, []int{65534}, []byte{byte(OpConstant), 0xFF, 0xFE}},
		{OpAdd, []int{}, []byte{byte(OpAdd)}},
	}

	for _, tt := range tests {
		instruction := Make(tt.op, tt.operands...)
		if len(instruction) != len(tt.expected) {
			t.Fatalf("instruction has wrong length. want=%d, got=%d", len(tt.expected), len(instruction))
		}

		for i, b := range tt.expected {
			if instruction[i] != b {
				t.Fatalf("wrong byte at pos %d. want=%d, got=%d", i, b, instruction[i])
			}
		}
	}
}

func TestReadOperands(t *testing.T) {
	instruction := Make(OpConstant, 65535)
	def, err := Lookup(byte(OpConstant))
	if err != nil {
		t.Fatalf("definition not found: %q", err)
	}

	operands, n := ReadOperands(def, instruction[1:])
	if n != 2 {
		t.Fatalf("n wrong. want=%d, got=%d", 2, n)
	}

	if operands[0] != 65535 {
		t.Fatalf("operand wrong. want=%d, got=%d", 65535, operands[0])
	}
}

func TestInstructionsString(t *testing.T) {
	instructions := []Instructions{
		Make(OpAdd),
		Make(OpConstant, 2),
		Make(OpConstant, 65535),
	}

	expected := `0000 OpAdd
0001 OpConstant 2
0004 OpConstant 65535
`

	concatted := Instructions{}
	for _, ins := range instructions {
		concatted = append(concatted, ins...)
	}

	if concatted.String() != expected {
		t.Errorf("instructions wrongly formatted. want=%q, got=%q", expected, concatted.String())
	}
}
