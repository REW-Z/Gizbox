using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.Src.Backend
{
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
        public static X64Instruction jmp(string labelname) => new X64Instruction(){ type = InstructionType.jmp, opand1 = labelname };
        public static X64Instruction jnz(string labelname) => new X64Instruction() { type = InstructionType.jnz, opand1 = labelname };
        
        public static X64Instruction mov(string opand1, string opand2) => new X64Instruction() { type = InstructionType.mov, opand1 = opand1, opand2 = opand2 };
        
        public static X64Instruction push(string opand1) => new X64Instruction() { type = InstructionType.push, opand1 = opand1 };
        public static X64Instruction pop(string opand1) => new X64Instruction() { type = InstructionType.pop, opand1 = opand1 };

        public static X64Instruction add(string opand1, string opand2) => new X64Instruction() { type = InstructionType.add, opand1 = opand1, opand2 = opand2 };
        public static X64Instruction sub(string opand1, string opand2) => new X64Instruction() { type = InstructionType.sub, opand1 = opand1, opand2 = opand2 };

        public static X64Instruction call(string labelname) => new X64Instruction() { type = InstructionType.call, opand1 = labelname };

        public static X64Instruction leave() => new X64Instruction() { type = InstructionType.leave };

        public static X64Instruction ret() => new X64Instruction() { type = InstructionType.ret };
    }
}
