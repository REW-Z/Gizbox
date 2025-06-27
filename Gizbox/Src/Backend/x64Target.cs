using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;


namespace Gizbox.Src.Backend
{
    public enum InstructionType
    {
        mov,

        add,
        sub,
        mul,
        div,

        jmp,
        jnz,
    }

    public static class x64Target
    {
        public static void CodeGen(ILUnit ir)
        {
            x64CodeGenContext context = new x64CodeGenContext(ir);

            context.StartCodeGen();
        }
    }


    public class BasicBlock
    {
        public int startIdx;
        public int endIdx;
    }

    public class X64
    {
        public static X64Instruction jmp(string labelName)
        {
            return new X64Instruction()
            {
                type = InstructionType.jmp,
                opand1 = labelName
            };
        }
        public static X64Instruction mov(string opand1, string opand2)
        {
            return new X64Instruction()
            {
                type = InstructionType.mov,
                opand1 = opand1,
                opand2 = opand2
            };
        }
        public static X64Instruction add(string opand1, string opand2)
        {
            return new X64Instruction()
            {
                type = InstructionType.add,
                opand1 = opand1,
                opand2 = opand2
            };
        }
        public static X64Instruction sub(string opand1, string opand2)
        {
            return new X64Instruction()
            {
                type = InstructionType.sub,
                opand1 = opand1,
                opand2 = opand2
            };
        }
        public static X64Instruction mul(string opand1, string opand2)
        {
            return new X64Instruction()
            {
                type = InstructionType.mul,
                opand1 = opand1,
                opand2 = opand2
            };
        }
        public static X64Instruction div(string opand1, string opand2)
        {
            return new X64Instruction()
            {
                type = InstructionType.div,
                opand1 = opand1,
                opand2 = opand2
            };
        }
        public static X64Instruction jnz(string labelName)
        {
            return new X64Instruction()
            {
                type = InstructionType.jnz,
                opand1 = labelName
            };
        }
    }


    public class X64Instruction
    {
        public InstructionType type;
        public string opand1;
        public string opand2;
        public string opand3;
    }

    public class x64CodeGenContext
    {
        private ILUnit ir;

        private List<BasicBlock> blocks;

        private List<X64Instruction> instructions = new();//1-n  1-1  n-1

        public x64CodeGenContext(ILUnit ir)
        {
            this.ir = ir;
        }

        public void StartCodeGen()
        {
            Pass0();
            Pass1();
            Pass2();
        }


        /// <summary> ？ </summary>
        private void Pass0()
        {
        }

        /// <summary> 划分基本块 </summary>
        private void Pass1()
        {
            blocks = new List<BasicBlock>();

            int blockstart = 0;
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];
                var nextTac = (i + 1 < ir.codes.Count) ? ir.codes[i + 1] : null;

                if(XUtils.IsJump(tac) ||
                    (nextTac != null &&  XUtils.HasLabel(tac)))
                {
                    //Block End
                    BasicBlock b = new BasicBlock() { startIdx = blockstart, endIdx = i };
                    blocks.Add(b);
                    blockstart = i + 1;
                }
            }


            for(int i = 0; i < blocks.Count; ++i)
            {
                var b = blocks[i];
                Gizbox.Debug.Log("---------------------------------------------------------");
                for (int j = b.startIdx; j <= b.endIdx; ++j)
                {
                    var tac = ir.codes[j];
                    Gizbox.Debug.Log($" { (string.IsNullOrEmpty(tac.label) ? "    " : (tac.label + ":")) } \t\t  {tac.op} {tac.arg1} {tac.arg2} {tac.arg3} ");
                }
                Gizbox.Debug.Log("---------------------------------------------------------");
                Gizbox.Debug.Log("\n\n");
            }
        }

        /// <summary> 指令选择 </summary>
        private void Pass2()
        {
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];


                switch(tac.op)
                {
                    case "":
                        break;
                    case "JUMP":
                        {
                            X64Instruction instruction = new X64Instruction()
                            {
                                type = InstructionType.jmp,
                                opand1 = tac.arg1
                            };
                            instructions.Add(instruction);
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            
                        }
                        break;
                    case "FUNC_END":
                        {
                        }
                        break;
                    case "RETURN":
                        {
                        }
                        break;
                    case "EXTERN_IMPL":
                        {
                        }
                        break;
                    case "PARAM":
                        {
                        }
                        break;
                    case "CALL":
                        {
                        }
                        break;
                    case "MCALL":
                        {
                        }
                        break;
                    case "ALLOC":
                        {
                        }
                        break;
                    case "DEL":
                        {
                        }
                        break;
                    case "ALLOC_ARRAY":
                        {
                        }
                        break;
                    case "IF_FALSE_JUMP":
                        {
                        }
                        break;
                    case "=":
                        {
                        }
                        break;
                    case "+=":
                        {
                        }
                        break;
                    case "-=":
                        {
                        }
                        break;
                    case "*=":
                        {
                        }
                        break;
                    case "/=":
                        {
                        }
                        break;
                    case "%=":
                        {
                        }
                        break;
                    case "+":
                        {
                        }
                        break;
                    case "-":
                        {
                        }
                        break;
                    case "*":
                        {
                        }
                        break;
                    case "/":
                        {
                        }
                        break;
                    case "%":
                        {
                        }
                        break;
                    case "NEG":
                        {
                        }
                        break;
                    case "!":
                        {
                        }
                        break;
                    case "<":
                        {
                        }
                        break;
                    case "<=":
                        {
                        }
                        break;
                    case ">":
                        {
                        }
                        break;
                    case ">=":
                        {
                        }
                        break;
                    case "==":
                        {
                        }
                        break;
                    case "!=":
                        {
                        }
                        break;
                    case "++":
                        {
                        }
                        break;
                    case "--":
                        {
                        }
                        break;
                    case "CAST":
                        {
                        }
                        break;
                    default:
                        break;
                }
            }
        }







        private class XUtils
        {
            public static bool IsJump(TAC tac)
            {
                return tac.op == "IF_FALSE_JUMP" || tac.op == "JUMP";
            }
            public static bool HasLabel(TAC tac)
            {
                return !string.IsNullOrEmpty(tac.label);
            }
        }
    }
}
