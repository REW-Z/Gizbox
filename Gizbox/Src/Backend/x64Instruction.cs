using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.Src.Backend
{
    public enum RegisterEnum
    {
        Undefined = -1,

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
        imul,
        idiv,

        inc,
        dec,
        neg,
        not,

        and,
        or,
        xor,

        cmp,
        test,

        jmp,
        jnz,
        je,
        jne,
        jl,
        jle,
        jg,
        jge,

        call,
        leave,
        ret,

        lea,

        setl,
        setle,
        setg,
        setge,
        sete,
        setne,

        cqo, // 符号扩展用于idiv
    }

    public class X64Instruction
    {
        public InstructionType type;
        public X64Operand operand1;
        public X64Operand operand2;
    }



    /// <summary>
    /// X64操作数基类
    /// </summary>
    public abstract class X64Operand
    {
        public enum OperandType
        {
            Label,// 标签
            Immediate,// 立即数
            Reg,// 寄存器
            VReg,// 虚拟寄存器
            Mem,// 内存
            Rel,// RIP相对寻址
        }
        public abstract OperandType Type { get; }
    }
    public class X64Label : X64Operand
    {
        public override OperandType Type => OperandType.Label;
        public string name;
        public X64Label(string label)
        {
            name = label;
        }
    }
    public class X64Immediate : X64Operand
    {
        public override OperandType Type => OperandType.Immediate;
        public long value;
    }
    public class X64Reg : X64Operand
    {
        public override OperandType Type => isVirtual ? OperandType.VReg : OperandType.Reg;

        public bool isVirtual;
        public RegisterEnum physReg;
        public SymbolTable.Record vRegVar;

        public X64Reg(RegisterEnum reg)
        {
            this.physReg = reg;
            this.isVirtual = false;
            this.vRegVar = null;
        }
        public X64Reg(SymbolTable.Record varRec)
        {
            this.vRegVar = varRec;
            this.isVirtual = true;
            this.physReg = default;
        }
        public void AllocPhysReg(RegisterEnum reg)
        {
            isVirtual = false;
            physReg = reg;
            vRegVar = null;
        }
    }
    public class X64Mem : X64Operand
    {
        public override OperandType Type => OperandType.Mem;
        public X64Reg baseReg; // 基址寄存器
        public X64Reg indexReg; // 索引寄存器
        public int scale; // 缩放因子
        public long displacement; // 偏移量
        //x64寻址：[base + index * scale + displacement]
        public X64Mem(X64Reg baseReg = null, X64Reg indexReg = null, int scale = 1, long displacement = 0)
        {
            this.baseReg = baseReg;
            this.indexReg = indexReg;
            this.scale = scale;
            this.displacement = displacement;
        }
    }
    public class X64Rel : X64Operand
    {
        public override OperandType Type => OperandType.Rel;
        public string symbolName; // 符号名称
        public long displacement; // 可选的偏移量

        public X64Rel(string symbolName, long displacement = 0)
        {
            this.symbolName = symbolName;
            this.displacement = displacement;
        }
    }


    public static class X64
    {
        // 跳转指令
        public static X64Instruction jmp(string labelname) => new() { type = InstructionType.jmp, operand1 = new X64Label(labelname) };
        public static X64Instruction jnz(string labelname) => new() { type = InstructionType.jnz, operand1 = new X64Label(labelname) };
        public static X64Instruction je(string labelname) => new() { type = InstructionType.je, operand1 = new X64Label(labelname) };
        public static X64Instruction jne(string labelname) => new() { type = InstructionType.jne, operand1 = new X64Label(labelname) };
        public static X64Instruction jl(string labelname) => new() { type = InstructionType.jl, operand1 = new X64Label(labelname) };
        public static X64Instruction jle(string labelname) => new() { type = InstructionType.jle, operand1 = new X64Label(labelname) };
        public static X64Instruction jg(string labelname) => new() { type = InstructionType.jg, operand1 = new X64Label(labelname) };
        public static X64Instruction jge(string labelname) => new() { type = InstructionType.jge, operand1 = new X64Label(labelname) };

        // 数据移动
        public static X64Instruction mov(X64Operand dest, X64Operand src) => new() { type = InstructionType.mov, operand1 = dest, operand2 = src };

        // 栈操作
        public static X64Instruction push(X64Operand operand) => new() { type = InstructionType.push, operand1 = operand };
        public static X64Instruction pop(X64Operand operand) => new() { type = InstructionType.pop, operand1 = operand };

        // 算术运算
        public static X64Instruction add(X64Operand dest, X64Operand src) => new() { type = InstructionType.add, operand1 = dest, operand2 = src };
        public static X64Instruction sub(X64Operand dest, X64Operand src) => new() { type = InstructionType.sub, operand1 = dest, operand2 = src };
        public static X64Instruction imul(X64Operand dest, X64Operand src) => new() { type = InstructionType.imul, operand1 = dest, operand2 = src };
        public static X64Instruction idiv(X64Operand operand) => new() { type = InstructionType.idiv, operand1 = operand };

        public static X64Instruction inc(X64Operand operand) => new() { type = InstructionType.inc, operand1 = operand };
        public static X64Instruction dec(X64Operand operand) => new() { type = InstructionType.dec, operand1 = operand };
        public static X64Instruction neg(X64Operand operand) => new() { type = InstructionType.neg, operand1 = operand };
        public static X64Instruction not(X64Operand operand) => new() { type = InstructionType.not, operand1 = operand };

        // 逻辑运算
        public static X64Instruction and(X64Operand dest, X64Operand src) => new() { type = InstructionType.and, operand1 = dest, operand2 = src };
        public static X64Instruction or(X64Operand dest, X64Operand src) => new() { type = InstructionType.or, operand1 = dest, operand2 = src };
        public static X64Instruction xor(X64Operand dest, X64Operand src) => new() { type = InstructionType.xor, operand1 = dest, operand2 = src };

        // 比较和测试
        public static X64Instruction cmp(X64Operand op1, X64Operand op2) => new() { type = InstructionType.cmp, operand1 = op1, operand2 = op2 };
        public static X64Instruction test(X64Operand op1, X64Operand op2) => new() { type = InstructionType.test, operand1 = op1, operand2 = op2 };

        // 函数调用
        public static X64Instruction call(string labelname) => new() { type = InstructionType.call, operand1 = new X64Label(labelname) };

        // 其他
        public static X64Instruction leave() => new() { type = InstructionType.leave };
        public static X64Instruction ret() => new() { type = InstructionType.ret };
        public static X64Instruction lea(X64Operand dest, X64Operand src) => new() { type = InstructionType.lea, operand1 = dest, operand2 = src };

        // 条件设置
        public static X64Instruction setl(X64Operand operand) => new() { type = InstructionType.setl, operand1 = operand };
        public static X64Instruction setle(X64Operand operand) => new() { type = InstructionType.setle, operand1 = operand };
        public static X64Instruction setg(X64Operand operand) => new() { type = InstructionType.setg, operand1 = operand };
        public static X64Instruction setge(X64Operand operand) => new() { type = InstructionType.setge, operand1 = operand };
        public static X64Instruction sete(X64Operand operand) => new() { type = InstructionType.sete, operand1 = operand };
        public static X64Instruction setne(X64Operand operand) => new() { type = InstructionType.setne, operand1 = operand };

        public static X64Instruction cqo() => new() { type = InstructionType.cqo };



        //常用属性  
        public static X64Reg rax => new(RegisterEnum.RAX);
        public static X64Reg rbx => new(RegisterEnum.RBX);
        public static X64Reg rcx => new(RegisterEnum.RCX);
        public static X64Reg rdx => new(RegisterEnum.RDX);
        public static X64Reg rsi => new(RegisterEnum.RSI);
        public static X64Reg rdi => new(RegisterEnum.RDI);

        public static X64Reg r8 => new(RegisterEnum.R8);
        public static X64Reg r9 => new(RegisterEnum.R9);
        public static X64Reg r10 => new(RegisterEnum.R10);
        public static X64Reg r11 => new(RegisterEnum.R11);
        public static X64Reg r12 => new(RegisterEnum.R12);
        public static X64Reg r13 => new(RegisterEnum.R13);
        public static X64Reg r14 => new(RegisterEnum.R14);
        public static X64Reg r15 => new(RegisterEnum.R15);


        public static X64Reg rsp => new(RegisterEnum.RSP);
        public static X64Reg rbp => new(RegisterEnum.RBP);

        public static X64Reg xmm0 => new(RegisterEnum.XMM0);
        public static X64Reg xmm1 => new(RegisterEnum.XMM1);
        public static X64Reg xmm2 => new(RegisterEnum.XMM2);
        public static X64Reg xmm3 => new(RegisterEnum.XMM3);
        public static X64Reg xmm4 => new(RegisterEnum.XMM4);
        public static X64Reg xmm5 => new(RegisterEnum.XMM5);
        public static X64Reg xmm6 => new(RegisterEnum.XMM6);
        public static X64Reg xmm7 => new(RegisterEnum.XMM7);


        public static X64Immediate imm(long val) => new() { value = val };
        
        public static X64Label label(string name) => new(name);
        public static X64Rel rel(string symbolName, long displacement = 0) => new X64Rel(symbolName, displacement);



        public static X64Reg vreg(SymbolTable.Record varrec)
        {
            var xoperand = new X64Reg(varrec);
            return xoperand;
        }

        public static X64Mem mem(RegisterEnum baseReg, RegisterEnum indexReg, int scale, long displacement = 0)
        {
            return new X64Mem(new X64Reg(baseReg), new X64Reg(indexReg), scale, displacement);
        }
        public static X64Mem mem(X64Reg baseVReg, X64Reg indexVReg = null, int scale = 1, long displacement = 0)
        {
            return new X64Mem(baseVReg, indexVReg, scale, displacement);
        }

    }
}
