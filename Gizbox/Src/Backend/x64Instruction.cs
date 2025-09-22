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

    public static class X64RegisterUtility
    {
        public static readonly RegisterEnum[] GPCalleeSaveRegs = new RegisterEnum[]
        {
            RegisterEnum.RBX,
            RegisterEnum.RBP,
            RegisterEnum.RSI,
            RegisterEnum.RDI,
            RegisterEnum.R12,
            RegisterEnum.R13,
            RegisterEnum.R14,
            RegisterEnum.R15,
        };
        public static readonly RegisterEnum[] GPCallerSaveRegs = new RegisterEnum[]
        {
            RegisterEnum.RAX,
            RegisterEnum.RCX,
            RegisterEnum.RDX,
            RegisterEnum.R8,
            RegisterEnum.R9,
            RegisterEnum.R10,
            RegisterEnum.R11,
        };
        public static readonly RegisterEnum[] XMMCalleeSaveRegs = new RegisterEnum[]
        {
            RegisterEnum.XMM6,
            RegisterEnum.XMM7,
            RegisterEnum.XMM8,
            RegisterEnum.XMM9,
            RegisterEnum.XMM10,
            RegisterEnum.XMM11,
            RegisterEnum.XMM12,
            RegisterEnum.XMM13,
            RegisterEnum.XMM14,
            RegisterEnum.XMM15,
        };
        public static readonly RegisterEnum[] XMMCallerSaveRegs = new RegisterEnum[]
        {
            RegisterEnum.XMM0,
            RegisterEnum.XMM1,
            RegisterEnum.XMM2,
            RegisterEnum.XMM3,
            RegisterEnum.XMM4,
            RegisterEnum.XMM5,
        };
    }


    public enum X64DefSize : int
    {
        undefined = 0,
        db = 1,
        dw = 2,
        dd = 4,
        dq = 8,
    }
    public enum X64ResSize : int
    {
        undefined = 0,
        resb = 1,
        resw = 2,
        resd = 4,
        resq = 8,
    }
    public enum X64Size : int
    {
        undefined = 0,
        @byte = 1,
        word = 2,
        dword = 4,
        qword = 8,
        oword = 16,
    }
    public enum InstructionKind
    {
        placeholder = -1, // 生成阶段的占位伪指令（仅用于中间拼接/回填，不输出真实机器指令）
        emptyline,        // 空行（可用于带标签的占位行或格式化）

        mov,              // 通用数据移动（GPR/XMM/内存，宽度由 sizeMark 决定）

        movd,             // SSE2：32位整数与XMM之间或XMM<->m32的移动（可在XMM与GPR间互通，低32位）
        movq,             // SSE2：64位整数与XMM之间或XMM<->m64/XMM<->XMM的移动（XMM与GPR可互通，64位）

        movss,            // SSE：标量单精度浮点移动（XMM<->XMM/m32；不直接与GPR互通）
        movsd,            // SSE2：标量双精度浮点移动（XMM<->XMM/m64；不直接与GPR互通；非字符串指令的movsd）

        // 128-bit packed moves
        movaps,           // 对齐打包单精度移动（XMM<->XMM/m128，packed single，要求内存16字节对齐）
        movapd,           // 对齐打包双精度移动（XMM<->XMM/m128, packed double，要求内存16字节对齐）
        movups,           // 非对齐打包单精度移动（XMM<->XMM/m128，packed single）
        movupd,           // 非对齐打包双精度移动（XMM<->XMM/m128，packed double）
        movdqa,           // 对齐打包整数移动（XMM<->XMM/m128，packed integer，要求内存16字节对齐）
        movdqu,           // 非对齐打包整数移动（XMM<->XMM/m128，packed integer）

        movzx,            // 零扩展移动（srcSize -> dstSize，仅整数/GPR；sizeMark=dst，sizeMarkAdditional=src）
        movsx,            // 符号扩展移动（srcSize -> dstSize，仅整数/GPR；sizeMark=dst，sizeMarkAdditional=src）

        push,             // 入栈（隐式使用RSP，按操作数宽度压栈）
        pop,              // 出栈（隐式使用RSP，按操作数宽度出栈）

        add,              // 整数/指针加法（按 sizeMark 宽度）
        addss,            // SSE：标量单精度浮点加法（XMM）
        addsd,            // SSE2：标量双精度浮点加法（XMM）

        sub,              // 整数/指针减法（按 sizeMark 宽度）
        subss,            // SSE：标量单精度浮点减法（XMM）
        subsd,            // SSE2：标量双精度浮点减法（XMM）

        mul,              // 无符号/隐式乘法：RAX*src -> RDX:RAX（单操作数形式，按 sizeMark）
        imul,             // 有符号乘法（两操作数形式常用：dest = dest * src，按 sizeMark）
        mulss,            // SSE：标量单精度浮点乘法（XMM）
        mulsd,            // SSE2：标量双精度浮点乘法（XMM）

        div,              // 无符号除法：RDX:RAX / src，商->RAX，余->RDX（单操作数形式，按 sizeMark）
        idiv,             // 有符号除法：RDX:RAX / src，商->RAX，余->RDX（单操作数形式，按 sizeMark）
        divss,            // SSE：标量单精度浮点除法（XMM）
        divsd,            // SSE2：标量双精度浮点除法（XMM）

        inc,              // 自增（整数，按 sizeMark）
        dec,              // 自减（整数，按 sizeMark）
        neg,              // 取负（整数，二补码，按 sizeMark）
        not,              // 按位取反（整数，按 sizeMark）

        and,              // 按位与（整数/位运算，不改变操作数宽度）
        or,               // 按位或（整数/位运算，不改变操作数宽度）
        xor,              // 按位异或（整数/位运算，不改变操作数宽度）

        cmp,              // 比较：op1 - op2，只影响标志位（不写回结果）
        test,             // 按位测试：op1 & op2，只影响标志位（不写回结果）

        jmp,              // 无条件跳转
        jz,               // ZF=1 跳转（等于/为零）
        jnz,              // ZF=0 跳转（不等/非零）
        je,               // 等价于 jz（ZF=1）
        jne,              // 等价于 jnz（ZF=0）
        jl,               // 有符号小于跳转（SF!=OF）
        jle,              // 有符号小于等于跳转（ZF=1 或 SF!=OF）
        jg,               // 有符号大于跳转（ZF=0 且 SF=OF）
        jge,              // 有符号大于等于跳转（SF=OF）

        call,             // 近调用（push 返回地址并跳转）
        leave,            // 离开栈帧（mov rsp, rbp; pop rbp）
        ret,              // 返回（从栈顶弹出返回地址并跳转）

        lea,              // 加载有效地址（不触发内存访问，常用于地址计算）

        setl,             // 有符号小于 -> 置1，否则置0（ZF=0 且 SF!=OF，写1字节）
        setle,            // 有符号小于等于 -> 置1，否则置0（ZF=1 或 SF!=OF，写1字节）
        setg,             // 有符号大于 -> 置1，否则置0（ZF=0 且 SF=OF，写1字节）
        setge,            // 有符号大于等于 -> 置1，否则置0（SF=OF，写1字节）
        sete,             // 等于 -> 置1，否则置0（ZF=1，写1字节）
        setne,            // 不等 -> 置1，否则置0（ZF=0，写1字节）
        setb,             // 无符号小于(below) -> 置1，否则置0（CF=1，写1字节）
        setbe,            // 无符号小于等于 -> 置1，否则置0（CF=1 或 ZF=1，写1字节）
        seta,             // 无符号大于(above) -> 置1，否则置0（CF=0 且 ZF=0，写1字节）
        setae,            // 无符号大于等于 -> 置1，否则置0（CF=0，写1字节）

        ucomiss,          // 无序比较标量单精度（XMM/m32 与 XMM；仅影响 ZF/PF/CF；NaN 置 PF=1）
        ucomisd,          // 无序比较标量双精度（XMM/m64 与 XMM；仅影响 ZF/PF/CF；NaN 置 PF=1）

        cqo,              // 符号扩展 RAX -> RDX:RAX（64->128位，常在 idiv 前使用）

        cvtsi2ss,         // 整数(GPR 32/64) -> 标量单精度（写 XMM）
        cvtsi2sd,         // 整数(GPR 32/64) -> 标量双精度（写 XMM）
        cvttss2si,        // 标量单精度 -> 32位整数（截断，写GPR）
        cvttss2siq,       // 标量单精度 -> 64位整数（截断，写GPR）
        cvttsd2si,        // 标量双精度 -> 32位整数（截断，写GPR）
        cvttsd2siq,       // 标量双精度 -> 64位整数（截断，写GPR）
        cvtss2sd,         // 单精度 -> 双精度（XMM -> XMM）
        cvtsd2ss,         // 双精度 -> 单精度（XMM -> XMM）
    }

    public class X64Instruction
    {
        public InstructionKind type;
        public string label;//标签
        public X64Operand operand0;
        public X64Operand operand1;
        public X64Size sizeMark = X64Size.undefined;
        public X64Size sizeMarkAdditional = X64Size.undefined;
        public string mark;//占位标记  
        public string comment = string.Empty;//指令注释
    }


    public class UtilityNASM
    {
        public static string Emit(Win64CodeGenContext context,
            LList<X64Instruction> instructions,
             Dictionary<string, List<(GType typeExpr, string valExpr)>> section_data,
              Dictionary<string, List<(GType typeExpr, string valExpr)>> section_rdata,
              Dictionary<string, List<GType>>  section_bss)
        {
            StringBuilder strb = new StringBuilder();

            //defines  
            strb.AppendLine("bits 64");

            //global and extern  
            strb.AppendLine("\n");
            foreach(var g in context.globalVarInfos)
            {
                strb.AppendLine($"global  {UtilsW64.LegalizeName(g.Key)}");
            }
            foreach(var g in context.globalFuncsInfos)
            {
                strb.AppendLine($"global  {UtilsW64.LegalizeName(g.Key)}");
            }
            foreach(var g in context.externVars)
            {
                strb.AppendLine($"extern  {UtilsW64.LegalizeName(g.Key)}");
            }
            foreach(var g in context.externFuncs)
            {
                if(g.Key == "Main")
                    continue;
                strb.AppendLine($"extern  {UtilsW64.LegalizeName(g.Key)}");
            }

            //.rdata
            strb.AppendLine("\n");
            strb.AppendLine("section .rdata");
            foreach(var rodata in section_rdata)
            {
                if(rodata.Value.Count == 1)
                {
                    var (typeExpr, valExpr) = rodata.Value[0];
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(rodata.Key)}  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(rodata.Key)}:");
                    foreach(var (typeExpr, valExpr) in rodata.Value)
                    {
                        strb.AppendLine($"\t\t  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                    }
                }
            }
            //.data
            strb.AppendLine("\n");
            strb.AppendLine("section .data");
            foreach(var data in section_data)
            {
                if(data.Value.Count == 1)
                {
                    var (typeExpr, valExpr) = data.Value[0];
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(data.Key)}  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(data.Key)}:");
                    foreach(var (typeExpr, valExpr) in data.Value)
                    {
                        strb.AppendLine($"\t\t  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                    }
                }
            }
            strb.AppendLine("\n");
            strb.AppendLine("section .bss");
            foreach(var bss in section_bss)
            {
                if(bss.Value.Count == 1)
                {
                    var typeExpr = bss.Value[0];
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(bss.Key)}  {UtilsW64.GetX64ReserveDefineType(typeExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{UtilsW64.LegalizeName(bss.Key)}:");
                    foreach(var typeExpr in bss.Value)
                    {
                        strb.AppendLine($"\t\t  {UtilsW64.GetX64ReserveDefineType(typeExpr)}");
                    }
                }
            }

            //.text  
            strb.AppendLine();
            strb.AppendLine("section .text");
            strb.AppendLine();
            foreach(var instruction in instructions)
            {
                UtilityNASM.SerializeInstruction(instruction, strb);
            }
            return strb.ToString();
        }
        public static void SerializeInstruction(X64Instruction instr, StringBuilder strb)
        {
            if(string.IsNullOrEmpty(instr.label) == false)
            {
                strb.AppendLine(UtilsW64.LegalizeName(instr.label) + ":");
            }

            if(instr.type == InstructionKind.placeholder)
            {
                strb.Append($"\t <{instr.mark}>".PadRight(50));
                if(string.IsNullOrEmpty(instr.comment) == false)
                {
                    strb.Append($" ; {instr.comment}");
                }
                    
                strb.Append('\n');
            }
            else if(instr.type != InstructionKind.emptyline)
            {
                int lineStartIdx = strb.Length;

                strb.Append("\t");
                strb.Append(instr.type.ToString());
                strb.Append("  ");

                if(instr.operand0 != null)
                {
                    X64Size size0 = X64Size.qword;
                    if(instr.sizeMark != X64Size.undefined)
                        size0 = instr.sizeMark;
                        

                    SerializeOprand(instr.operand0, size0, strb);
                }
                if(instr.operand1 != null)
                {
                    X64Size size1 = X64Size.qword;
                    if(instr.sizeMark != X64Size.undefined)
                    {
                        size1 = instr.sizeMark;
                        if(instr.sizeMarkAdditional != X64Size.undefined && instr.sizeMarkAdditional != instr.sizeMark)
                            size1 = instr.sizeMarkAdditional;
                    }

                    strb.Append(",  ");
                    SerializeOprand(instr.operand1, size1, strb);
                }

                if(string.IsNullOrEmpty(instr.mark) == false)
                {
                    strb.Append($" <{instr.mark}> ");
                }


                //PadRight(50)
                int lineLen = strb.Length - lineStartIdx;
                if(lineLen < 50)
                {
                    int padLen = 50 - lineLen;
                    strb.Append(new string(' ', padLen));
                }


                if(string.IsNullOrEmpty(instr.comment) == false)
                {
                    strb.Append($" ; {instr.comment}");
                }

                strb.Append('\n');
            }
        }
        public static void SerializeOprand(X64Operand operand, X64Size size, StringBuilder strb)
        {
            switch(operand)
            {
                case X64Reg reg:
                    {
                        strb.Append(reg.GetName(size));
                    }
                    break;
                case X64Rel rel:
                    {
                        strb.Append("[rel " + UtilsW64.LegalizeName(rel.symbolName) + (rel.displacement != 0 ? ((rel.displacement > 0 ? "+" : "-") + Math.Abs(rel.displacement).ToString()) : "") + "]");
                    }
                    break;
                case X64Immediate im:
                    {
                        strb.Append((im).value.ToString());
                    }
                    break;
                case X64Label lb:
                    {
                        strb.Append(UtilsW64.LegalizeName((lb).name));
                    }
                    break;
                case X64Mem mem:
                    {
                        strb.Append(size.ToString() + " [" +
                            (mem.baseReg != null ? mem.baseReg.GetName(X64Size.qword) : "") +
                            (mem.indexReg != null ? ("+" + mem.indexReg.GetName(X64Size.qword) + (mem.scale != 1 ? ("*" + mem.scale.ToString()) : "")) : "") +
                            (mem.displacement != 0 ? ((mem.displacement > 0 ? "+" : "-") + Math.Abs(mem.displacement).ToString()) : "")
                            + "]");
                    }
                    break;
                default:
                    strb.Append(operand.Kind.ToString());
                    break;
            }
        }
    }



    /// <summary>
    /// X64操作数基类
    /// </summary>
    public abstract class X64Operand
    {
        public enum OperandKind
        {
            Label,// 标签
            Immediate,// 立即数
            Reg,// 寄存器
            VReg,// 虚拟寄存器
            Mem,// 内存
            Rel,// RIP相对寻址
        }
        public abstract OperandKind Kind { get; }
    }
    public class X64Label : X64Operand
    {
        public override OperandKind Kind => OperandKind.Label;
        public string name;
        public X64Label(string label)
        {
            name = label;
        }
    }
    public class X64Immediate : X64Operand
    {
        public override OperandKind Kind => OperandKind.Immediate;
        public long value;
    }
    public class X64Reg : X64Operand
    {
        public override OperandKind Kind => isVirtual ? OperandKind.VReg : OperandKind.Reg;

        public bool isVirtual;
        public RegisterEnum physReg;
        public SymbolTable.Record vRegVar;

        public X64Reg(RegisterEnum reg)
        {
            this.physReg = reg;
            this.isVirtual = false;
            this.vRegVar = null;

            if(reg != RegisterEnum.Undefined)
            {
                X64.physRegisterUseTemp.Add(reg);
            }
        }
        public X64Reg(SymbolTable.Record varRec)
        {
            this.vRegVar = varRec;
            this.isVirtual = true;
            this.physReg = RegisterEnum.Undefined;
        }
        public string GetName(X64Size size)
        {
            if(isVirtual)
                return $"vreg%{vRegVar.name}";

            string name = physReg.ToString();

            // XMM 寄存器名不随 size 变化
            if(physReg >= RegisterEnum.XMM0 && physReg <= RegisterEnum.XMM15)
                return name;

            if(size == X64Size.qword || size == X64Size.undefined)
                return name;

            bool isR8Plus = physReg >= RegisterEnum.R8 && physReg <= RegisterEnum.R15;

            switch(size)
            {
                case X64Size.dword:
                    return isR8Plus ? (name + "D") : ("E" + name.Substring(1));

                case X64Size.word:
                    return isR8Plus ? (name + "W") : name.Substring(1);

                case X64Size.@byte:
                    if(isR8Plus)
                        return name + "B";
                    switch(physReg)
                    {
                        case RegisterEnum.RAX:
                            return "AL";
                        case RegisterEnum.RBX:
                            return "BL";
                        case RegisterEnum.RCX:
                            return "CL";
                        case RegisterEnum.RDX:
                            return "DL";
                        case RegisterEnum.RSP:
                            return "SPL";
                        case RegisterEnum.RBP:
                            return "BPL";
                        case RegisterEnum.RSI:
                            return "SIL";
                        case RegisterEnum.RDI:
                            return "DIL";
                        default:
                            return name;
                    }

                default:
                    return name;
            }
        }
        public bool IsXXM()
        {
            if(isVirtual)
                throw new GizboxException(ExceptioName.Undefine, "vreg not alloc physics reg.");

            if(physReg >= RegisterEnum.XMM0 && physReg <= RegisterEnum.XMM15)
                return true;
            else
                return false;
        }
        public void AllocPhysReg(RegisterEnum reg)
        {
            isVirtual = false;
            physReg = reg;
            //vRegVar = null;
        }
    }
    public class X64Mem : X64Operand
    {
        public override OperandKind Kind => OperandKind.Mem;
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
        public override OperandKind Kind => OperandKind.Rel;
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
        public static HashSet<RegisterEnum> physRegisterUseTemp = new();


        // 带标签的空行  
        public static X64Instruction emptyLine(string labelname) => new() { type = InstructionKind.emptyline, label = labelname };

        // 跳转指令
        public static X64Instruction jmp(string labelname) => new() { type = InstructionKind.jmp, operand0 = new X64Label(labelname) };
        public static X64Instruction jz(string labelname) => new() { type = InstructionKind.jz, operand0 = new X64Label(labelname) };
        public static X64Instruction jnz(string labelname) => new() { type = InstructionKind.jnz, operand0 = new X64Label(labelname) };
        public static X64Instruction je(string labelname) => new() { type = InstructionKind.je, operand0 = new X64Label(labelname) };
        public static X64Instruction jne(string labelname) => new() { type = InstructionKind.jne, operand0 = new X64Label(labelname) };
        public static X64Instruction jl(string labelname) => new() { type = InstructionKind.jl, operand0 = new X64Label(labelname) };
        public static X64Instruction jle(string labelname) => new() { type = InstructionKind.jle, operand0 = new X64Label(labelname) };
        public static X64Instruction jg(string labelname) => new() { type = InstructionKind.jg, operand0 = new X64Label(labelname) };
        public static X64Instruction jge(string labelname) => new() { type = InstructionKind.jge, operand0 = new X64Label(labelname) };

        // 数据移动
        public static X64Instruction mov(X64Operand dest, X64Operand src, X64Size size) => new() { type = InstructionKind.mov, operand0 = dest, operand1 = src, sizeMark = size };
        public static X64Instruction movd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movd, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction movq(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movq, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };

        public static X64Instruction movss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movss, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction movsd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movsd, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };

        // 128bit移动  
        public static X64Instruction movaps(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movaps, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };
        public static X64Instruction movapd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movapd, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };
        public static X64Instruction movups(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movups, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };
        public static X64Instruction movupd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movupd, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };
        public static X64Instruction movdqa(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movdqa, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };
        public static X64Instruction movdqu(X64Operand dest, X64Operand src) => new() { type = InstructionKind.movdqu, operand0 = dest, operand1 = src, sizeMark = X64Size.oword };

        public static X64Instruction movzx(X64Operand dest, X64Operand src, X64Size dstSize, X64Size srcSize) => new() { type = InstructionKind.movzx, operand0 = dest, operand1 = src, sizeMark = dstSize, sizeMarkAdditional = srcSize };
        public static X64Instruction movsx(X64Operand dest, X64Operand src, X64Size dstSize, X64Size srcSize) => new() { type = InstructionKind.movsx, operand0 = dest, operand1 = src, sizeMark = dstSize, sizeMarkAdditional = srcSize };

        // 栈操作
        public static X64Instruction push(X64Operand operand) => new() { type = InstructionKind.push, operand0 = operand };
        public static X64Instruction pop(X64Operand operand) => new() { type = InstructionKind.pop, operand0 = operand };

        // 算术运算 - 标量整数/指针
        public static X64Instruction add(X64Operand dest, X64Operand src, X64Size size) => new() { type = InstructionKind.add, operand0 = dest, operand1 = src, sizeMark = size };
        public static X64Instruction sub(X64Operand dest, X64Operand src, X64Size size) => new() { type = InstructionKind.sub, operand0 = dest, operand1 = src, sizeMark = size };

        // 乘除（使用显式两操作数形式/一操作数形式）
        public static X64Instruction imul_2(X64Operand dest, X64Operand src, X64Size size) => new() { type = InstructionKind.imul, operand0 = dest, operand1 = src, sizeMark = size };
        public static X64Instruction imul_1(X64Operand src, X64Size size) => new() { type = InstructionKind.imul, operand0 = src, sizeMark = size };
        public static X64Instruction mul(X64Operand src, X64Size size) => new() { type = InstructionKind.mul, operand0 = src, sizeMark = size };
        public static X64Instruction idiv(X64Operand src, X64Size size) => new() { type = InstructionKind.idiv, operand0 = src, sizeMark = size };
        public static X64Instruction div(X64Operand src, X64Size size) => new() { type = InstructionKind.div, operand0 = src, sizeMark = size };

        // SSE 标量浮点算术
        public static X64Instruction addss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.addss, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction addsd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.addsd, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };
        public static X64Instruction subss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.subss, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction subsd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.subsd, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };
        public static X64Instruction mulss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.mulss, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction mulsd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.mulsd, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };
        public static X64Instruction divss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.divss, operand0 = dest, operand1 = src, sizeMark = X64Size.dword };
        public static X64Instruction divsd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.divsd, operand0 = dest, operand1 = src, sizeMark = X64Size.qword };

        public static X64Instruction inc(X64Operand operand, X64Size size) => new() { type = InstructionKind.inc, operand0 = operand, sizeMark = size };
        public static X64Instruction dec(X64Operand operand, X64Size size) => new() { type = InstructionKind.dec, operand0 = operand, sizeMark = size };
        public static X64Instruction neg(X64Operand operand, X64Size size) => new() { type = InstructionKind.neg, operand0 = operand, sizeMark = size };
        public static X64Instruction not(X64Operand operand, X64Size size) => new() { type = InstructionKind.not, operand0 = operand, sizeMark = size };

        // 逻辑运算
        public static X64Instruction and(X64Operand dest, X64Operand src) => new() { type = InstructionKind.and, operand0 = dest, operand1 = src };
        public static X64Instruction or(X64Operand dest, X64Operand src) => new() { type = InstructionKind.or, operand0 = dest, operand1 = src };
        public static X64Instruction xor(X64Operand dest, X64Operand src) => new() { type = InstructionKind.xor, operand0 = dest, operand1 = src };

        // 比较和测试
        public static X64Instruction cmp(X64Operand op1, X64Operand op2) => new() { type = InstructionKind.cmp, operand0 = op1, operand1 = op2 };
        public static X64Instruction test(X64Operand op1, X64Operand op2) => new() { type = InstructionKind.test, operand0 = op1, operand1 = op2 };

        // 函数调用
        public static X64Instruction call(string labelname) => new() { type = InstructionKind.call, operand0 = new X64Label(labelname) };
        public static X64Instruction call(X64Operand method) => new() { type = InstructionKind.call, operand0 = method };

        // 其他
        public static X64Instruction leave() => new() { type = InstructionKind.leave };
        public static X64Instruction ret() => new() { type = InstructionKind.ret };
        public static X64Instruction lea(X64Operand dest, X64Operand src) => new() { type = InstructionKind.lea, operand0 = dest, operand1 = src };

        // 条件设置  
        public static X64Instruction setl(X64Operand operand) => new() { type = InstructionKind.setl, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setle(X64Operand operand) => new() { type = InstructionKind.setle, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setg(X64Operand operand) => new() { type = InstructionKind.setg, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setge(X64Operand operand) => new() { type = InstructionKind.setge, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction sete(X64Operand operand) => new() { type = InstructionKind.sete, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setne(X64Operand operand) => new() { type = InstructionKind.setne, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setb(X64Operand operand) => new() { type = InstructionKind.setb, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setbe(X64Operand operand) => new() { type = InstructionKind.setbe, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction seta(X64Operand operand) => new() { type = InstructionKind.seta, operand0 = operand, sizeMark = X64Size.@byte };
        public static X64Instruction setae(X64Operand operand) => new() { type = InstructionKind.setae, operand0 = operand, sizeMark = X64Size.@byte };

        // 浮点比较（不写目的，只影响标志位）
        public static X64Instruction ucomiss(X64Operand a, X64Operand b) => new() { type = InstructionKind.ucomiss, operand0 = a, operand1 = b };
        public static X64Instruction ucomisd(X64Operand a, X64Operand b) => new() { type = InstructionKind.ucomisd, operand0 = a, operand1 = b };
        public static X64Instruction cqo() => new() { type = InstructionKind.cqo };

        // 整数 -> 浮点
        public static X64Instruction cvtsi2ss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvtsi2ss, operand0 = dest, operand1 = src };
        public static X64Instruction cvtsi2sd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvtsi2sd, operand0 = dest, operand1 = src };
        // 浮点 -> 整数  
        public static X64Instruction cvttss2si(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvttss2si, operand0 = dest, operand1 = src };
        public static X64Instruction cvttss2siq(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvttss2siq, operand0 = dest, operand1 = src };
        public static X64Instruction cvttsd2si(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvttsd2si, operand0 = dest, operand1 = src };
        public static X64Instruction cvttsd2siq(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvttsd2siq, operand0 = dest, operand1 = src };
        // float <-> double
        public static X64Instruction cvtss2sd(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvtss2sd, operand0 = dest, operand1 = src };
        public static X64Instruction cvtsd2ss(X64Operand dest, X64Operand src) => new() { type = InstructionKind.cvtsd2ss, operand0 = dest, operand1 = src };


        // 占位  
        public static X64Instruction placehold(string mark)
        {
            return new X64Instruction() { type = InstructionKind.placeholder, operand0 = null, operand1 = null, mark = mark };
        }

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

        public static X64Mem mem(X64Reg baseVReg, X64Reg indexVReg = null, int scale = 1, long disp = 0)
        {
            return new X64Mem(baseVReg, indexVReg, scale, disp);
        }
    }
}
