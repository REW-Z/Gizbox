using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;


namespace Gizbox.Src.Backend
{
    public static class Win64Target
    {
        public static void CodeGen(ILUnit ir)
        {
            Win64CodeGenContext context = new Win64CodeGenContext(ir);

            context.StartCodeGen();
        }
    }


    public class Win64CodeGenContext
    {
        private ILUnit ir;

        private ControlFlowGraph cfg;

        private List<BasicBlock> blocks;

        private List<X64Instruction> instructions = new();//1-n  1-1  n-1

        public Win64CodeGenContext(ILUnit ir)
        {
            this.ir = ir;
        }

        public void StartCodeGen()
        {
            Pass1();//基本块和控制流图
            Pass2();//指令选择
            Pass3();//寄存器分配
        }


        /// <summary> 划分基本块 </summary>
        private void Pass1()
        {
            //基本块划分
            blocks = new List<BasicBlock>();
            int blockstart = 0;
            string currentLabel = null;
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];
                var nextTac = (i + 1 < ir.codes.Count) ? ir.codes[i + 1] : null;

                if(XUtils.IsJump(tac))
                {
                    //Block End
                    BasicBlock b = new BasicBlock() { startIdx = blockstart, endIdx = i };
                    if(currentLabel != null)
                    {
                        b.hasLabel = currentLabel;
                        currentLabel = null;
                    }


                    blocks.Add(b);
                    blockstart = i + 1;
                    continue;
                }

                if((nextTac != null &&  XUtils.HasLabel(nextTac)))
                {
                    //Block End
                    BasicBlock b = new BasicBlock() { startIdx = blockstart, endIdx = i };
                    if(currentLabel != null)
                    {
                        b.hasLabel = currentLabel;
                        currentLabel = null;
                    }

                    blocks.Add(b);
                    blockstart = i + 1;
                    currentLabel = nextTac.label;
                    continue;
                }
            }


            foreach(var b in blocks)
            {
                Console.WriteLine("\n\n---------block len" + (b.endIdx - b.startIdx) + "------------");
                for(int i = b.startIdx; i <= b.endIdx; ++i)
                {
                    Console.WriteLine(ir.codes[i].ToExpression());
                }
                Console.WriteLine("---------------------");
            }


            //控制流图  
            for(int i = 0; i < blocks.Count; ++i)
            {
                var currentBlock = blocks[i];
                var lastTac = ir.codes[currentBlock.endIdx];

                if(lastTac.op == "JUMP")
                {
                    string targetLabel = lastTac.arg1;
                    var targetBlock = QueryBlockByLabel(targetLabel);
                    if(targetBlock != null)
                    {
                        cfg.AddEdge(currentBlock, targetBlock);
                    }
                }
                else if(lastTac.op == "IF_FALSE_JUMP")
                {
                    string targetLabel = lastTac.arg2;
                    var targetBlock = QueryBlockByLabel(targetLabel);
                    if(targetBlock != null)
                    {
                        cfg.AddEdge(currentBlock, targetBlock);
                    }

                    // 条件跳转还有一个顺序执行的后继
                    if(i + 1 < blocks.Count)
                    {
                        cfg.AddEdge(currentBlock, blocks[i + 1]);
                    }
                }
                else if(lastTac.op == "RETURN" || lastTac.op == "FUNC_END")
                {
                    //todo
                }
                // 其他
                else if(i + 1 < blocks.Count)
                {
                    cfg.AddEdge(currentBlock, blocks[i + 1]);
                }
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
                            instructions.Add(X64.jmp(tac.arg1));
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            //函数序言
                            instructions.Add(X64.push("rbp"));
                            instructions.Add(X64.mov("rbp", "rsp"));
                        }
                        break;
                    case "FUNC_END":
                        {
                            //函数尾声
                            instructions.Add(X64.mov("rsp", "rbp"));
                            instructions.Add(X64.pop("rbp"));
                            instructions.Add(X64.ret());
                        }
                        break;
                    case "RETURN":
                        {
                            ////如果有返回值
                            //if(tac.arg1 != null)
                            //{
                            //    instructions.Add(X64.mov("rax", tac.arg1)); //假设rax是返回值寄存器
                            //}

                            ////函数尾声
                            //instructions.Add(X64.mov("rsp", "rbp"));
                            //instructions.Add(X64.pop("rbp"));
                            //instructions.Add(X64.ret());
                        }
                        break;
                    case "EXTERN_IMPL"://无需处理
                        { }
                        break;
                    case "PARAM":
                        {
                            //第几个参数?
                            //参数size?
                        }
                        break;
                    case "CALL":
                        {
                        }
                        break;
                    case "MCALL":
                        {
                            //todo:虚函数表查找调用
                        }
                        break;
                    case "ALLOC":
                        {//todo
                        }
                        break;
                    case "DEL":
                        {//todo
                        }
                        break;
                    case "ALLOC_ARRAY":
                        {//todo
                        }
                        break;
                    case "IF_FALSE_JUMP":
                        {
                            var condition = tac.arg1;

                            var jumpLabel = tac.arg2;

                            //判断条件  
                            //todo;

                            //如果条件为假，则跳转到标签
                            instructions.Add(X64.jnz(jumpLabel)); //假设条件在rax寄存器中
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

        // <summary> 寄存器分配 </summary>
        private void Pass3()
        {
            //寄存器分配
            //todo
        }


        private BasicBlock QueryBlockByLabel(string label)
        {
            return blocks.FirstOrDefault(b => b.hasLabel == label);
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
