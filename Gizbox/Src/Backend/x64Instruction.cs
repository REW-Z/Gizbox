using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.Src.Backend
{
    public enum RegisterEnum
    {
        RAX = 0,
        RBX = 1,
        RCX = 2,
        RDX = 3,
        RSI = 4,
        RDI = 5,
        RBP = 6,
        RSP = 7,
        R8 = 8,
        R9 = 9,
        R10 = 10,
        R11 = 11,
        R12 = 12,
        R13 = 13,
        R14 = 14,
        R15 = 15,

        XMM0 = 100,
        XMM1 = 101,
        XMM2 = 102,
        XMM3 = 103,
        XMM4 = 104,
        XMM5 = 105,
        XMM6 = 106,
        XMM7 = 107,
        XMM8 = 108,
        XMM9 = 109,
        XMM10 = 110,
        XMM11 = 111,
        XMM12 = 112,
        XMM13 = 113,
        XMM14 = 114,
        XMM15 = 115,
    }
    public enum InstructionType
    {
        mov,

        push,
        pop,

        add,
        sub,
        mul,
        div,

        jmp,
        jnz,
        je,

        cmp,
        test,

        call,

        leave,

        ret,
    }

    public class X64Instruction
    {
        public InstructionType type;
        public string opand1;
        public string opand2;
        public string opand3;
    }


    public static class X64
    {
        public static X64Instruction jmp(string labelname) => new X64Instruction() { type = InstructionType.jmp, opand1 = labelname };
        public static X64Instruction jnz(string labelname) => new X64Instruction() { type = InstructionType.jnz, opand1 = labelname };
        public static X64Instruction je(string labelname) => new X64Instruction() { type = InstructionType.je, opand1 = labelname };

        public static X64Instruction mov(string opand1, string opand2) => new X64Instruction() { type = InstructionType.mov, opand1 = opand1, opand2 = opand2 };

        public static X64Instruction push(string opand1) => new X64Instruction() { type = InstructionType.push, opand1 = opand1 };
        public static X64Instruction pop(string opand1) => new X64Instruction() { type = InstructionType.pop, opand1 = opand1 };

        public static X64Instruction add(string opand1, string opand2) => new X64Instruction() { type = InstructionType.add, opand1 = opand1, opand2 = opand2 };
        public static X64Instruction sub(string opand1, string opand2) => new X64Instruction() { type = InstructionType.sub, opand1 = opand1, opand2 = opand2 };

        public static X64Instruction cmp(string opand1, string opand2) => new X64Instruction() { type = InstructionType.cmp, opand1 = opand1, opand2 = opand2 };
        public static X64Instruction test(string opand1, string opand2) => new X64Instruction() { type = InstructionType.test, opand1 = opand1, opand2 = opand2 };

        public static X64Instruction call(string labelname) => new X64Instruction() { type = InstructionType.call, opand1 = labelname };

        public static X64Instruction leave() => new X64Instruction() { type = InstructionType.leave };

        public static X64Instruction ret() => new X64Instruction() { type = InstructionType.ret };
    }
}
