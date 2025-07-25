﻿using System;
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

        private List<BasicBlock> blocks;

        private List<X64Instruction> instructions = new();//1-n  1-1  n-1

        public Win64CodeGenContext(ILUnit ir)
        {
            this.ir = ir;
        }

        public void StartCodeGen()
        {
            Pass1();
            Pass2();
        }


        /*
bool condition;
if (condition)
{
    // 空语句块
}
        


         
; 假设 condition 在 [rbp-4]
mov eax, dword ptr [rbp-4]   ; 读取 condition 到 eax
test eax, eax                ; 检查 eax 是否为0
je  label_after_if           ; 如果为0（false），跳转到 if 之后
; if 语句块内容（此处为空）
label_after_if:
         */

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
                            //如果有返回值
                            if(tac.arg1 != null)
                            {
                                instructions.Add(X64.mov("rax", tac.arg1)); //假设rax是返回值寄存器
                            }

                            //函数尾声
                            instructions.Add(X64.mov("rsp", "rbp"));
                            instructions.Add(X64.pop("rbp"));
                            instructions.Add(X64.ret());
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
                            instructions.Add(X64.call(tac.arg1)); //保存调用前的栈帧指针
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
