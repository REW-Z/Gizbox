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


            //控制流图  
            cfg ??= new();
            for(int i = 0; i < blocks.Count; ++i)
            {
                var currentBlock = blocks[i];
                var lastTac = ir.codes[currentBlock.endIdx];

                if(lastTac.op == "JUMP")
                {
                    string targetLabel = lastTac.arg1;
                    var targetBlock = QueryBlockHasLabel(targetLabel);
                    if(targetBlock != null)
                    {
                        cfg.AddEdge(currentBlock, targetBlock);
                    }
                }
                else if(lastTac.op == "IF_FALSE_JUMP")
                {
                    string targetLabel = lastTac.arg2;
                    var targetBlock = QueryBlockHasLabel(targetLabel);
                    if(targetBlock != null)
                    {
                        //分支False  
                        cfg.AddEdge(currentBlock, targetBlock);
                    }
                    if(i + 1 < blocks.Count)
                    {
                        //分支True
                        cfg.AddEdge(currentBlock, blocks[i + 1]);
                    }
                }
                else if(lastTac.op == "RETURN")
                {
                    var exitBlock = QueryFunctionEndBlock(currentBlock.endIdx);
                    if(exitBlock != null)
                    {
                        cfg.AddEdge(currentBlock, exitBlock);
                    }
                }
                else if(lastTac.op == "FUNC_END")
                {
                    //函数结束 ->无后继基本块  
                }
                // 其他
                else if(i + 1 < blocks.Count)
                {
                    cfg.AddEdge(currentBlock, blocks[i + 1]);
                }
            }

            //分析USE/DEF信息  
            foreach(var block in blocks)
            {
                for(int i = block.startIdx; i <= block.endIdx; ++i)
                {
                    var tac = ir.codes[i];
                    AnalyzeUSEDEF(tac, i, block);
                }
            }

            // 收集所有变量
            var allVariables = new HashSet<SymbolTable.Record>();
            foreach(var block in blocks)
            {
                foreach(var variable in block.USE.Keys)
                {
                    allVariables.Add(variable);
                }
                foreach(var variable in block.DEF.Keys)
                {
                    allVariables.Add(variable);
                }
            }

            // 初始化IN和OUT集合
            foreach(var block in blocks)
            {
                block.IN.Clear();
                block.OUT.Clear();
            }

            // 迭代计算直到收敛
            bool changed;
            do
            {
                changed = false;

                // 反向遍历基本块（因为活跃变量是向后传播的）
                for(int i = blocks.Count - 1; i >= 0; i--)
                {
                    var block = blocks[i];

                    // 保存旧的IN集合
                    var oldIN = new List<SymbolTable.Record>(block.IN);

                    // OUT[B] = ∪(所有后继基本块的IN集合)
                    block.OUT.Clear();
                    foreach(var successor in block.successors)
                    {
                        foreach(var variable in successor.IN)
                        {
                            if(!block.OUT.Contains(variable))
                            {
                                block.OUT.Add(variable);
                            }
                        }
                    }

                    // IN[B] = USE[B] ∪ (OUT[B] - DEF[B])
                    block.IN.Clear();

                    // 添加USE集合中的变量
                    foreach(var variable in block.USE.Keys)
                    {
                        if(!block.IN.Contains(variable))
                        {
                            block.IN.Add(variable);
                        }
                    }

                    // 添加(OUT[B] - DEF[B])中的变量
                    foreach(var variable in block.OUT)
                    {
                        if(!block.DEF.ContainsKey(variable) && !block.IN.Contains(variable))
                        {
                            block.IN.Add(variable);
                        }
                    }

                    // 检查是否有变化
                    if(oldIN.Count != block.IN.Count ||
                       !oldIN.All(v => block.IN.Contains(v)))
                    {
                        changed = true;
                    }
                }
            } while(changed);

            // 输出活跃变量分析结果
            foreach(var b in blocks)
            {
                Console.WriteLine("\n\n---------block len" + (b.endIdx - b.startIdx) + "------------");
                for(int i = b.startIdx; i <= b.endIdx; ++i)
                {
                    Console.WriteLine(ir.codes[i].ToExpression());
                }

                Console.WriteLine("  USE: " + string.Join(", ", b.USE.Keys.Select(v => v.name)));
                Console.WriteLine("  DEF: " + string.Join(", ", b.DEF.Keys.Select(v => v.name)));
                Console.WriteLine("  IN:  " + string.Join(", ", b.IN.Select(v => v.name)));
                Console.WriteLine("  OUT: " + string.Join(", ", b.OUT.Select(v => v.name)));
            }

            for(int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
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




        private void AnalyzeUSEDEF(TAC tac, int lineNum, BasicBlock block)
        {
            var status = ir.scopeStatusArr[lineNum];
            ir.stackDic.TryGetValue(status, out var envStack);
            
            if(envStack == null)
            {
                throw new GizboxException(ExceptioName.Unknown, $"null env stack at line:{lineNum}!");
            }
                

            switch(tac.op)
            {
                case "=":
                    {
                        // arg1 = arg2
                        DEF(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                    }
                    break;
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                case "%=":
                    {
                        // arg1 += arg2
                        USE(tac.arg1, lineNum, block, envStack);
                        DEF(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                    }
                    break;
                case "+":
                case "-":
                case "*":
                case "/":
                case "%":
                case "<":
                case "<=":
                case ">":
                case ">=":
                case "==":
                case "!=":
                    {
                        // arg1 = arg2 op arg3
                        DEF(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                        USE(tac.arg3, lineNum, block, envStack);
                    }
                    break;
                case "NEG":
                case "!":
                case "CAST":
                    {
                        // arg1 = op arg2
                        DEF(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                    }
                    break;
                case "++":
                case "--":
                    {
                        // ++arg1 或 --arg1
                        USE(tac.arg1, lineNum, block, envStack);
                        DEF(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "IF_FALSE_JUMP":
                    {
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "RETURN":
                    {
                        if(!string.IsNullOrEmpty(tac.arg1))
                        {
                            USE(tac.arg1, lineNum, block, envStack);
                        }
                    }
                    break;
                case "PARAM":
                    {
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "ALLOC":
                    {
                        // arg1 = alloc
                        DEF(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "ALLOC_ARRAY":
                    {
                        // arg1 = alloc_array arg2
                        DEF(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                    }
                    break;
                case "DEL":
                    {
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "CALL":
                case "MCALL"://约定Callee不使用任何Caller的局部变量
                    break;
            }
        }

        /// <summary>
        /// 分析操作数（返回USE或者DEF的变量对象）
        /// </summary>
        /// <param name="operand"></param>
        /// <returns></returns>
        private string AnalyzeOprand(string operand)
        {
            //可能的格式示例：
            //  数组访问：[arr[x]]
            //  成员访问：[obj.x]
            //  局部变量访问：[var1]
            //  全局变量访问：[globalvar1]
            //  字面量:LITINT:123
            //  常量：CONSTSTRING:123  //第123个字符串常量

            // 移除方括号
            if(operand.StartsWith("[") && operand.EndsWith("]"))
            {
                operand = operand.Substring(1, operand.Length - 2);

                if(operand.Contains('.'))
                {
                    //成员访问：[obj.x]
                    var parts = operand.Split('.');
                    return parts[0].Trim();
                }
                else if(operand.Contains('[') )
                {
                    //数组访问：[arr[x]]
                    var parts = operand.Split('[');
                    return parts[0].Trim();
                }
                else
                {
                    //局部变量或全局变量访问
                    return operand.Trim();
                }
            }
            //字面量或者常量  
            else if(operand.Contains(':'))
            {
                return null;//忽略常量和字面量
            }
            return null;
        }

        private void USE(string operand, int lineNum, BasicBlock block, Gizbox.GStack<SymbolTable> envStack)
        {
            if(string.IsNullOrEmpty(operand))
                return;

            var operandVar = AnalyzeOprand(operand);

            var record = QueryVariable(operandVar, envStack);
            if(record != null)
            {
                if(!block.USE.ContainsKey(record))
                {
                    block.USE[record] = new List<int>();
                }
                block.USE[record].Add(lineNum);
            }
        }

        private void DEF(string operand, int lineNum, BasicBlock block, Gizbox.GStack<SymbolTable> envStack)
        {
            if(string.IsNullOrEmpty(operand))
                return;

            var operandVar = AnalyzeOprand(operand);

            var record = QueryVariable(operandVar, envStack);
            if(record != null)
            {
                if(!block.DEF.ContainsKey(record))
                {
                    block.DEF[record] = new List<int>();
                }
                block.DEF[record].Add(lineNum);
            }
        }


        private SymbolTable.Record QueryVariable(string varName, Gizbox.GStack<SymbolTable> envStack)
        {
            if(varName == null)
                return null;
            if(envStack == null)
                throw new GizboxException(ExceptioName.Unknown, "envStack is null!");
            
            for(int i = envStack.Count - 1; i >= 0; i--)
            {
                var env = envStack[i];
                if(env.ContainRecordName(varName))
                {
                    var record = env.GetRecord(varName);
                    if(record.category == SymbolTable.RecordCatagory.Variable ||
                       record.category == SymbolTable.RecordCatagory.Param)
                    {
                        return record;
                    }
                }
            }
            return null;
        }
        private BasicBlock QueryBlockHasLabel(string label)
        {
            return blocks.FirstOrDefault(b => b.hasLabel == label);
        }

        private BasicBlock QueryFunctionEndBlock(int instructionIndex)
        {
            for(int i = instructionIndex + 1; i < ir.codes.Count; i++)
            {
                var tac = ir.codes[i];
                if(tac.op == "FUNC_END")
                {
                    return blocks.FirstOrDefault(b => b.startIdx <= i && b.endIdx >= i);
                }
            }
            return null;
        }


        private class XUtils
        {
            public static bool IsJump(TAC tac)
            {
                return tac.op == "IF_FALSE_JUMP" ||
                    tac.op == "JUMP" ||
                    tac.op == "RETURN";
                //过程调用一般不会中断基本块，因为过程内的控制流是隐藏的  
            }
            public static bool HasLabel(TAC tac)
            {
                return !string.IsNullOrEmpty(tac.label);
            }
        }
    }
}
