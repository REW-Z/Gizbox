using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;


namespace Gizbox.Src.Backend
{
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
            private RegisterAllocator registerAllocator;
            private RegisterAllocationResult allocationResult;

            public Win64CodeGenContext(ILUnit ir)
            {
                this.ir = ir;
                this.registerAllocator = new RegisterAllocator();
            }

            public void StartCodeGen()
            {
                Pass1(); // 划分基本块
                Pass2(); // 寄存器分配
                Pass3(); // 指令选择
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
                        (nextTac != null && XUtils.HasLabel(nextTac)))
                    {
                        //Block End
                        BasicBlock b = new BasicBlock() { startIdx = blockstart, endIdx = i };
                        blocks.Add(b);
                        blockstart = i + 1;
                    }
                }

                // 构建基本块的前驱后继关系
                BuildControlFlowGraph();

                // 分析每个基本块的def和use信息
                AnalyzeDefUse();

                for(int i = 0; i < blocks.Count; ++i)
                {
                    var b = blocks[i];
                    Gizbox.Debug.Log("---------------------------------------------------------");
                    for(int j = b.startIdx; j <= b.endIdx; ++j)
                    {
                        var tac = ir.codes[j];
                        Gizbox.Debug.Log($" {(string.IsNullOrEmpty(tac.label) ? "    " : (tac.label + ":"))} \t\t  {tac.op} {tac.arg1} {tac.arg2} {tac.arg3} ");
                    }
                    Gizbox.Debug.Log("---------------------------------------------------------");
                    Gizbox.Debug.Log("\n\n");
                }
            }

            /// <summary> 寄存器分配 </summary>
            private void Pass2()
            {
                // 执行寄存器分配
                allocationResult = registerAllocator.AllocateRegisters(blocks, ir);

                Gizbox.Debug.Log("=== 寄存器分配结果 ===");
                foreach(var allocation in allocationResult.allocation)
                {
                    Gizbox.Debug.Log($"变量 {allocation.Key.name} -> 寄存器 {allocation.Value}");
                }

                if(allocationResult.spilledVariables.Count > 0)
                {
                    Gizbox.Debug.Log("=== 溢出变量 ===");
                    foreach(var spilledVar in allocationResult.spilledVariables)
                    {
                        var offset = allocationResult.spillOffsets.ContainsKey(spilledVar) ?
                            allocationResult.spillOffsets[spilledVar] : -1;
                        Gizbox.Debug.Log($"变量 {spilledVar.name} 溢出到栈偏移 {offset}");
                    }
                }
            }

            /// <summary> 指令选择 </summary>
            private void Pass3()
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

                                // 为溢出变量预留栈空间
                                if(allocationResult.spilledVariables.Count > 0)
                                {
                                    int totalSpillSize = allocationResult.spillOffsets.Values.Sum();
                                    if(totalSpillSize > 0)
                                    {
                                        instructions.Add(X64.sub("rsp", totalSpillSize.ToString()));
                                    }
                                }
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
                                    var location = GetVariableLocation(tac.arg1);
                                    instructions.Add(X64.mov("rax", location));
                                }

                                //函数尾声
                                instructions.Add(X64.mov("rsp", "rbp"));
                                instructions.Add(X64.pop("rbp"));
                                instructions.Add(X64.ret());
                            }
                            break;
                        case "=":
                            {
                                var srcLocation = GetVariableLocation(tac.arg2);
                                var dstLocation = GetVariableLocation(tac.arg1);

                                if(srcLocation != dstLocation)
                                {
                                    instructions.Add(X64.mov(dstLocation, srcLocation));
                                }
                            }
                            break;
                        case "+":
                            {
                                var src1Location = GetVariableLocation(tac.arg2);
                                var src2Location = GetVariableLocation(tac.arg3);
                                var dstLocation = GetVariableLocation(tac.arg1);

                                // 先移动第一个操作数到目标位置
                                if(src1Location != dstLocation)
                                {
                                    instructions.Add(X64.mov(dstLocation, src1Location));
                                }
                                // 然后执行加法
                                instructions.Add(X64.add(dstLocation, src2Location));
                            }
                            break;
                        case "-":
                            {
                                var src1Location = GetVariableLocation(tac.arg2);
                                var src2Location = GetVariableLocation(tac.arg3);
                                var dstLocation = GetVariableLocation(tac.arg1);

                                if(src1Location != dstLocation)
                                {
                                    instructions.Add(X64.mov(dstLocation, src1Location));
                                }
                                instructions.Add(X64.sub(dstLocation, src2Location));
                            }
                            break;
                        case "IF_FALSE_JUMP":
                            {
                                var conditionLocation = GetVariableLocation(tac.arg1);
                                var jumpLabel = tac.arg2;

                                // 测试条件
                                instructions.Add(X64.cmp(conditionLocation, "0"));
                                instructions.Add(X64.je(jumpLabel));
                            }
                            break;
                        // 其他指令的实现...
                        default:
                            break;
                    }
                }
            }

            /// <summary>
            /// 构建控制流图
            /// </summary>
            private void BuildControlFlowGraph()
            {
                // 为每个基本块建立前驱后继关系
                for(int i = 0; i < blocks.Count; i++)
                {
                    var block = blocks[i];
                    var lastInstruction = ir.codes[block.endIdx];

                    // 检查跳转指令
                    if(XUtils.IsJump(lastInstruction))
                    {
                        // 找到跳转目标块
                        if(lastInstruction.op == "JUMP")
                        {
                            var targetBlock = FindBlockByLabel(lastInstruction.arg1);
                            if(targetBlock != null)
                            {
                                block.successors.Add(targetBlock);
                                targetBlock.predecessors.Add(block);
                            }
                        }
                        else if(lastInstruction.op == "IF_FALSE_JUMP")
                        {
                            // 条件跳转有两个后继
                            var targetBlock = FindBlockByLabel(lastInstruction.arg2);
                            if(targetBlock != null)
                            {
                                block.successors.Add(targetBlock);
                                targetBlock.predecessors.Add(block);
                            }

                            // 下一个基本块
                            if(i + 1 < blocks.Count)
                            {
                                var nextBlock = blocks[i + 1];
                                block.successors.Add(nextBlock);
                                nextBlock.predecessors.Add(block);
                            }
                        }
                    }
                    else
                    {
                        // 顺序执行到下一个基本块
                        if(i + 1 < blocks.Count)
                        {
                            var nextBlock = blocks[i + 1];
                            block.successors.Add(nextBlock);
                            nextBlock.predecessors.Add(block);
                        }
                    }
                }
            }

            /// <summary>
            /// 分析def和use信息
            /// </summary>
            private void AnalyzeDefUse()
            {
                foreach(var block in blocks)
                {
                    for(int i = block.startIdx; i <= block.endIdx; i++)
                    {
                        var tac = ir.codes[i];

                        // 分析使用的变量
                        var usedVars = GetUsedVariables(tac);
                        foreach(var usedVar in usedVars)
                        {
                            if(!block.useDict.ContainsKey(usedVar))
                            {
                                block.useDict[usedVar] = new List<int>();
                            }
                            block.useDict[usedVar].Add(i);
                        }

                        // 分析定义的变量
                        var definedVars = GetDefinedVariables(tac);
                        foreach(var definedVar in definedVars)
                        {
                            if(!block.defDict.ContainsKey(definedVar))
                            {
                                block.defDict[definedVar] = new List<int>();
                            }
                            block.defDict[definedVar].Add(i);
                        }
                    }
                }
            }

            /// <summary>
            /// 获取变量的位置（寄存器或内存）
            /// </summary>
            private string GetVariableLocation(string variableName)
            {
                // 查找变量记录
                var variableRecord = FindVariableRecord(variableName);
                if(variableRecord == null)
                {
                    return variableName; // 可能是立即数或标签
                }

                // 检查是否分配了寄存器
                if(allocationResult.allocation.ContainsKey(variableRecord))
                {
                    var register = allocationResult.allocation[variableRecord];
                    return GetRegisterName(register);
                }

                // 检查是否溢出到内存
                if(allocationResult.spillOffsets.ContainsKey(variableRecord))
                {
                    var offset = allocationResult.spillOffsets[variableRecord];
                    return $"[rbp-{offset}]";
                }

                // 默认返回变量名
                return variableName;
            }

            /// <summary>
            /// 将寄存器枚举转换为字符串
            /// </summary>
            private string GetRegisterName(RegisterEnum register)
            {
                return register.ToString().ToLower();
            }

            /// <summary>
            /// 根据标签查找基本块
            /// </summary>
            private BasicBlock FindBlockByLabel(string label)
            {
                // todo:标签系统实现
                // 简化实现，需要根据实际情况调整
                for(int i = 0; i < ir.codes.Count; i++)
                {
                    if(ir.codes[i].label == label)
                    {
                        return blocks.FirstOrDefault(b => b.startIdx <= i && i <= b.endIdx);
                    }
                }
                return null;
            }

            /// <summary>
            /// 查找变量记录
            /// </summary>
            private SymbolTable.Record FindVariableRecord(string variableName)
            {
                // todo:符号表结构实现
                // 简化实现，需要在全局作用域中查找
                if(ir.globalScope?.env?.ContainRecordName(variableName) == true)
                {
                    return ir.globalScope.env.GetRecord(variableName);
                }
                return null;
            }

            /// <summary>
            /// 获取指令使用的变量
            /// </summary>
            private List<SymbolTable.Record> GetUsedVariables(TAC tac)
            {
                var result = new List<SymbolTable.Record>();

                // 根据指令类型分析使用的变量
                switch(tac.op)
                {
                    case "+":
                    case "-":
                    case "*":
                    case "/":
                        AddVariableIfExists(result, tac.arg2);
                        AddVariableIfExists(result, tac.arg3);
                        break;
                    case "=":
                        AddVariableIfExists(result, tac.arg2);
                        break;
                    case "IF_FALSE_JUMP":
                        AddVariableIfExists(result, tac.arg1);
                        break;
                        // 其他指令...
                }

                return result;
            }

            /// <summary>
            /// 获取指令定义的变量
            /// </summary>
            private List<SymbolTable.Record> GetDefinedVariables(TAC tac)
            {
                var result = new List<SymbolTable.Record>();

                // 根据指令类型分析定义的变量
                switch(tac.op)
                {
                    case "+":
                    case "-":
                    case "*":
                    case "/":
                    case "=":
                        AddVariableIfExists(result, tac.arg1);
                        break;
                        // 其他指令...
                }

                return result;
            }

            /// <summary>
            /// 如果变量存在则添加到列表
            /// </summary>
            private void AddVariableIfExists(List<SymbolTable.Record> list, string variableName)
            {
                if(string.IsNullOrEmpty(variableName))
                    return;

                var record = FindVariableRecord(variableName);
                if(record != null)
                {
                    list.Add(record);
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
