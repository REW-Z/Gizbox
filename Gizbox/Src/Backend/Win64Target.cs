using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;



//      (RAX, RCX, RDX, R8, R9, R10, R11、 XMM0-XMM5 是调用者保存的寄存器)
//      (RBX、RBP、RDI、RSI、RSP、R12、R13、R14、R15 和 XMM6 - XMM15 由使用它们的函数保存和还原，视为非易失性。)

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

        private Dictionary<string, object> dataSeg = new();
        private Dictionary<string, object> roDataSeg = new();

        private Dictionary<string, IROperandExpr> operandCache = new();
        private Dictionary<TAC, TACInfo> tacInfoCache = new(); 


        private List<X64Instruction> instructions = new();//1-n  1-1  n-1

        // 占位指令  
        private HashSet<X64Instruction> callerSavePlaceHolders = new();//主调函数保存寄存器指令占位
        private HashSet<X64Instruction> calleeSavePlaceHolders = new();//被调函数保存寄存器指令占位
        private HashSet<X64Instruction> callerRestorePlaceHolders = new();//主调函数恢复寄存器占位
        private HashSet<X64Instruction> calleeRestorePlaceHolders = new();//被调函数恢复寄存器占位

        // 类表
        public Dictionary<string, SymbolTable.Record> classDict = new();

        // 类名-虚函数表roKey
        public Dictionary<string, string> vtableRoKeys = new();

        // 当前函数作用域  
        private Dictionary<SymbolTable, BiDictionary<SymbolTable.Record, X64Reg>> vRegs = new();
        private SymbolTable globalEnv = null;
        private SymbolTable funcEnv = null;


        //debug  
        private const bool debugLogSymbolTable = true;
        private const bool debugLogTacInfos = true;
        private const bool debugLogBlockInfos = false;
        



        public Win64CodeGenContext(ILUnit ir)
        {
            this.ir = ir;
        }

        public void StartCodeGen()
        {
            Pass0();//静态信息补充
            Pass1();//基本块和控制流图
            Pass2();//指令选择
            Pass3();//寄存器分配
        }

        /// <summary> 静态信息补充 </summary>
        private void Pass0()
        {
            this.globalEnv = ir.globalScope.env;

            //类布局和虚函数表布局
            var globalEnv = ir.globalScope.env;
            foreach(var (k, r) in globalEnv.records)
            {
                switch(r.category)
                {
                    case SymbolTable.RecordCatagory.Class:
                        {
                            classDict.Add(k, r);

                            //类对象布局（Vptr在对象头占8字节）  
                            GenClassLayoutInfo(r);

                            //虚函数表布局
                            GenClassInfo(r);
                        }
                        break;
                    case SymbolTable.RecordCatagory.Function:
                        {
                            //函数信息
                            GenFuncInfo(r.envPtr);
                        }
                        break;
                    default:
                        break;
                }
            }

            //建立指令附加信息  
            int currParamIdx = -1;
            for(int line = 0; line < ir.codes.Count; ++line)
            {
                var tac = ir.codes[line];

                //建立TAC信息  
                AddTacInfo(tac, line);
                var inf = GetTacInfo(tac);

                //实参排序  
                if(tac.op == "PARAM")
                {
                    currParamIdx += 1;
                    inf.PARAM_paramidx = currParamIdx;
                }
                else
                {
                    currParamIdx = -1;
                }

                //CALL参数个数  
                if(tac.op == "CALL" || tac.op == "MCALL")
                {
                    int.TryParse(tac.arg1, out inf.CALL_paramCount);
                }

                //MCALL对象类型
                if(tac.op == "MCALL")
                {
                    var prev = ir.codes[line - 1];
                    var prevInf = GetTacInfo(prev);
                    inf.MCALL_methodTargetObject = prevInf.oprand0;
                }


                //完善TAC信息  
                inf.FinishInfo();
            }



            //指令分析（收集非直接数的字面量）    
            for(int line = 0; line < ir.codes.Count; ++line)
            {
                var tac = ir.codes[line];
                // 读取字符串字面量和浮点数字面量到静态常量数据区  
                CollectReadOnlyData(tac.arg0, line);
                CollectReadOnlyData(tac.arg1, line);
                CollectReadOnlyData(tac.arg2, line);
            }
            

            //输出符号表
            if(debugLogSymbolTable)
            {
                ir.globalScope.env.Print();
            }
            //输出指令和操作数分析数据  
            if(debugLogTacInfos)
            {
                for(int line = 0; line < ir.codes.Count; ++line)
                {
                    var tacInf = GetTacInfo(ir.codes[line]);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("原指令：" + tacInf.tac.ToExpression());

                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    if(tacInf.oprand0 != null) 
                        Console.WriteLine("    op0：typeExpr:" + (tacInf.oprand0.typeExpression ?? "?") + "  type:  " + tacInf.oprand0.expr.type.ToString());
                    if(tacInf.oprand1 != null)
                        Console.WriteLine("    op1：typeExpr:" + (tacInf.oprand1.typeExpression ?? "?") + "  type:  " + tacInf.oprand1.expr.type.ToString());
                    if(tacInf.oprand2 != null)
                        Console.WriteLine("    op2：typeExpr:" + (tacInf.oprand2.typeExpression ?? "?") + "  type:  " + tacInf.oprand2.expr.type.ToString());

                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
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

                if(UtilsW64.IsJump(tac))
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

                if((nextTac != null &&  UtilsW64.HasLabel(nextTac)))
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
                    string targetLabel = lastTac.arg0;
                    var targetBlock = QueryBlockHasLabel(targetLabel);
                    if(targetBlock != null)
                    {
                        cfg.AddEdge(currentBlock, targetBlock);
                    }
                }
                else if(lastTac.op == "IF_FALSE_JUMP")
                {
                    string targetLabel = lastTac.arg1;
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
            bool changed = true; // 确保至少执行一次
            int maxIterations = blocks.Count * blocks.Count;
            int iterationCount = 0;
            while(changed && iterationCount < maxIterations)
            {
                changed = false;
                iterationCount++;

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
                        bool varActiveAtEntry = true;
                        int firstUseLine = block.USE[variable].Min();
                        if(block.DEF.TryGetValue(variable, out var vardef) && vardef.Count > 0)
                        {
                            var firstDefLine = vardef.Min();
                            if(firstDefLine < firstUseLine)//DEF在USE之前，变量在入口处不活跃
                            {
                                varActiveAtEntry = false;
                            }
                        }
                        if(varActiveAtEntry)
                        {
                            if(!block.IN.Contains(variable))
                            {
                                block.IN.Add(variable);
                            }
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
            }
            if(iterationCount >= maxIterations)
            {
                throw new GizboxException(ExceptioName.Unknown,
                    $"活跃变量分析未收敛，迭代次数超过 {maxIterations}");
            }

            // 输出活跃变量分析结果
            if(debugLogBlockInfos)
            {
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
            }

            for(int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
            }
        }

        /// <summary> 指令选择 </summary>
        private void Pass2()
        {
            vRegs.Add(globalEnv, new());

            // 参数临时列表
            List<TACInfo> tempParamList = new();
            // 指令选择  
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];
                var tacInf = GetTacInfo(tac);

                switch(tac.op)
                {
                    case "":
                        break;
                    case "JUMP":
                        {
                            instructions.Add(X64.jmp(tac.arg0));
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            //函数作用域开始
                            funcEnv = ir.GetTopEnvAtLine(i);
                            vRegs.Add(funcEnv, new());


                            //函数序言
                            instructions.Add(X64.push(X64.rbp));
                            instructions.Add(X64.mov(X64.rbp, X64.rsp));

                            //保存寄存器（非易失性需要由Callee保存）  
                            var placeholder = X64.placehold("callee_save");
                            calleeSavePlaceHolders.Add(placeholder);
                            instructions.Add(placeholder);
                        }
                        break;
                    case "FUNC_END":
                        {
                            //函数尾声
                            instructions.Add(X64.mov(X64.rsp, X64.rbp));
                            instructions.Add(X64.pop(X64.rbp));
                            instructions.Add(X64.ret());
                            //恢复寄存器（非易失性需要由Callee保存） 
                            var placeholder = X64.placehold("callee_restore");
                            calleeRestorePlaceHolders.Add(placeholder);
                            instructions.Add(placeholder);

                            //函数作用域结束
                            funcEnv = null;
                        }
                        break;
                    case "RETURN":
                        {
                            // 如果有返回值
                            if(!string.IsNullOrEmpty(tac.arg0))
                            {
                                var returnValue = ParseOperand(tacInf.oprand0);

                                //按照win64约定，整数返回值存储在rax寄存器中，浮点数返回值存储在xmm0寄存器中

                                // 根据返回值类型选择寄存器
                                var tacinfo = GetTacInfo(tac);
                                if(tacinfo.oprand0.IsSSEType())
                                {
                                    // 浮点数返回值 -> xmm0
                                    instructions.Add(X64.mov(X64.xmm0, returnValue));
                                }
                                else
                                {
                                    // 整数/指针返回值 -> rax
                                    instructions.Add(X64.mov(X64.rax, returnValue));
                                }
                            }

                            // 跳转到FUNC_END  
                            string funcEndLabel = "func_end:" + funcEnv.name;
                            instructions.Add(X64.jmp(funcEndLabel));
                        }
                        break;
                    case "EXTERN_IMPL"://无需处理
                        break;
                    case "PARAM":
                        {
                            //留到CALL指令中再做处理  
                            tempParamList.Add(tacInf);
                        }
                        break;
                    case "CALL":
                        {
                            // 第一参数是 函数名，第二个参数是 参数个数
                            string funcName = tac.arg0.Substring(1, tac.arg0.Length - 2);


                            //调用前准备  
                            {
                                // 调用者保存寄存器（易失性寄存器需Caller保存） (选择性保存)
                                var placeholderSave = X64.placehold("caller_save");
                                instructions.Add(placeholderSave);
                                callerSavePlaceHolders.Add(placeholderSave);


                                // 栈帧16字节对齐
                                instructions.Add(X64.and(X64.rsp, X64.imm(-16)));
                                // 如果是奇数个参数 -> 需要8字节对齐栈指针
                                if(tacInf.CALL_paramCount % 2 != 0)
                                {
                                    // 如果是奇数个参数，先将rsp对齐到16字节
                                    instructions.Add(X64.sub(X64.rsp, X64.imm(8)));
                                }
                                // 其他栈参数空间
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int len = (tacInf.CALL_paramCount - 4) * 8;
                                    instructions.Add(X64.sub(X64.rsp, X64.imm(len)));
                                }
                                // 影子空间(32字节)
                                instructions.Add(X64.sub(X64.rsp, X64.imm(32)));


                                // 参数赋值(IR中PARAM指令已经是倒序)  
                                foreach(var paraminfo in tempParamList)
                                {
                                    //寄存器传参  
                                    if(paraminfo.PARAM_paramidx < 4)
                                        UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, paraminfo.oprand0.IsSSEType());

                                    //栈帧传参[rsp + 8]开始
                                    int offset = (8 * (paraminfo.PARAM_paramidx + 1));
                                    instructions.Add(X64.mov(X64.mem(X64.rsp, displacement: offset), ParseOperand(paraminfo.oprand0)));
                                }
                                tempParamList.Clear();
                            }


                            // 实际的函数调用
                            // （CALL 指令会自动把返回地址（下一条指令的 RIP）压入栈顶）  
                            instructions.Add(X64.call(funcName));

                            //调用后处理
                            {
                                // 清理栈上的参数（如果有超过4个参数）
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int stackParamCount = tacInf.CALL_paramCount - 4;
                                    long stackCleanupBytes = stackParamCount * 8; // 每个参数8字节
                                    instructions.Add(X64.add(X64.rsp, X64.imm(stackCleanupBytes)));
                                }

                                //还原保存的寄存器(占位)  
                                var placeholderRestore = X64.placehold("caller_restore");
                                instructions.Add(placeholderRestore);
                                callerRestorePlaceHolders.Add(placeholderRestore);
                            }
                        }
                        break;
                    case "MCALL":
                        {
                            // 第一参数是 方法名（未混淆），第二个参数是参数个数
                            string methodName = tac.arg0.Substring(1, tac.arg0.Length - 2);


                            //this参数载入寄存器  
                            var codeParamObj = tempParamList.FirstOrDefault(c => c.PARAM_paramidx == 0);
                            var x64obj = ParseOperand(codeParamObj.oprand0);
                            instructions.Add(X64.mov(X64.rcx, x64obj));

                            //取Vptr  
                            var methodRec = QueryMember(codeParamObj.oprand0.typeExpression, methodName);
                            instructions.Add(X64.mov(X64.rax, X64.mem(X64.rcx, displacement: 0)));
                            //函数地址（addr表示在虚函数表中的偏移(Index*8)）  
                            instructions.Add(X64.mov(X64.rax, X64.mem(X64.rax, displacement: methodRec.addr)));


                            //调用前准备(和CALL指令一致)  
                            {
                                // 调用者保存寄存器（易失性寄存器需Caller保存） (选择性保存)
                                var placeholderSave = X64.placehold("caller_save");
                                instructions.Add(placeholderSave);
                                callerSavePlaceHolders.Add(placeholderSave);


                                // 栈帧16字节对齐
                                instructions.Add(X64.and(X64.rsp, X64.imm(-16)));
                                // 如果是奇数个参数 -> 需要8字节对齐栈指针
                                if(tacInf.CALL_paramCount % 2 != 0)
                                {
                                    // 如果是奇数个参数，先将rsp对齐到16字节
                                    instructions.Add(X64.sub(X64.rsp, X64.imm(8)));
                                }
                                // 其他栈参数空间
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int len = (tacInf.CALL_paramCount - 4) * 8;
                                    instructions.Add(X64.sub(X64.rsp, X64.imm(len)));
                                }
                                // 影子空间(32字节)
                                instructions.Add(X64.sub(X64.rsp, X64.imm(32)));


                                // 参数赋值(IR中PARAM指令已经是倒序)  
                                foreach(var paraminfo in tempParamList)
                                {
                                    //寄存器传参  
                                    if(paraminfo.PARAM_paramidx < 4)
                                        UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, paraminfo.oprand0.IsSSEType());

                                    //栈帧传参[rsp + 8]开始
                                    int offset = (8 * (paraminfo.PARAM_paramidx + 1));
                                    instructions.Add(X64.mov(X64.mem(X64.rsp, displacement: offset), ParseOperand(paraminfo.oprand0)));
                                }
                                tempParamList.Clear();
                            }


                            //调用
                            X64.call(X64.rax);


                            //调用后处理
                            {
                                // 清理栈上的参数（如果有超过4个参数）
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int stackParamCount = tacInf.CALL_paramCount - 4;
                                    long stackCleanupBytes = stackParamCount * 8; // 每个参数8字节
                                    instructions.Add(X64.add(X64.rsp, X64.imm(stackCleanupBytes)));
                                }

                                //还原保存的寄存器(占位)  
                                var placeholderRestore = X64.placehold("caller_restore");
                                instructions.Add(placeholderRestore);
                                callerRestorePlaceHolders.Add(placeholderRestore);
                            }
                        }
                        break;
                    case "ALLOC":
                        {
                            string typeName = tac.arg1;

                            // 获取类型记录
                            if(!classDict.TryGetValue(typeName, out var classRec))
                            {
                                throw new GizboxException(ExceptioName.Unknown, $"未找到类型定义: {typeName}");
                            }

                            // 获取类的大小
                            long objectSize = classRec.size;

                            // 调用malloc分配堆内存（参数是字节数）
                            instructions.Add(X64.mov(X64.rcx, X64.imm(objectSize)));
                            instructions.Add(X64.call("malloc"));

                            // 分配的内存地址存储到目标变量  
                            var targetVar = ParseOperand(tacInf.oprand0);
                            instructions.Add(X64.mov(targetVar, X64.rax));

                            // 将虚函数表地址写入对象的前8字节
                            instructions.Add(X64.mov(X64.rdx, X64.rel(vtableRoKeys[typeName])));
                            instructions.Add(X64.mov(X64.mem(X64.rax, displacement: 0), X64.rdx));
                        }
                        break; 
                    case "DEL":
                        {
                            var objPtr = ParseOperand(tacInf.oprand0);
                            instructions.Add(X64.mov(X64.rcx, objPtr));
                            instructions.Add(X64.call("free"));
                        }
                        break;
                    case "ALLOC_ARRAY":
                        {
                            var target = ParseOperand(tacInf.oprand0);
                            var lenOp = ParseOperand(tacInf.oprand1);
                            string arrType = tacInf.oprand0.typeExpression;
                            string elemType = UtilsW64.SliceArrayElementType(arrType);
                            int elemSize = UtilsW64.GetTypeSize(elemType);

                            instructions.Add(X64.mov(X64.rax, lenOp));//RAX 作为中间寄存器
                            instructions.Add(X64.mul(X64.rax, X64.imm(elemSize)));
                            instructions.Add(X64.mov(X64.rcx, X64.rax));

                            //动态分配  
                            instructions.Add(X64.call("malloc"));
                            //返回指针在RAX，写入目标变量  
                            instructions.Add(X64.mov(target, X64.rax));
                        }
                        break;
                    case "IF_FALSE_JUMP":
                        {
                            var cond = ParseOperand(tacInf.oprand0);
                            instructions.Add(X64.test(cond, cond));
                            instructions.Add(X64.jz(tac.arg1));//jump if zero
                        }
                        break;
                    case "=":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand1);

                            if(tacInf.oprand0.IsSSEType())
                            {
                            }
                            else
                            {
                            }
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


        #region PASS0

        private void GenClassLayoutInfo(SymbolTable.Record classRec)
        {
            var classEnv = classRec.envPtr;
            Console.WriteLine("---------" + classEnv.name + "----------");
            List<SymbolTable.Record> fieldRecs = new();
            foreach(var (memberName, memberRec) in classEnv.records)
            {
                if(memberRec.category != SymbolTable.RecordCatagory.Variable)
                    continue;
                fieldRecs.Add(memberRec);
            }
            (int size, int align)[] fieldSizeAndAlignArr = new (int size, int align)[fieldRecs.Count];
            //对象头是虚函数表指针  
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                string typeExpress = fieldRecs[i].typeExpression;
                var size = MemUtility.GetGizboxTypeSize(typeExpress);
                var align = MemUtility.GetGizboxTypeSize(typeExpress);
                fieldRecs[i].size = size;
                fieldSizeAndAlignArr[i] = (size, align);
            }
            long classSize = MemUtility.ClassLayout(8, fieldSizeAndAlignArr, out var allocAddrs);
            for(int i = 0; i < fieldRecs.Count; i++)
            {
                fieldRecs[i].addr = allocAddrs[i];
            }

            classRec.size = classSize;
        }

        private void GenClassInfo(SymbolTable.Record classRec)
        {
            var classTable = classRec.envPtr;

            //方法信息(包含构造函数)  
            foreach(var funcTable in classTable.children)
            {
                if(funcTable.tableCatagory != SymbolTable.TableCatagory.FuncScope)
                    continue;

                GenFuncInfo(funcTable);
            }

            //虚函数表和函数指针索引  
            {
                List<X64Label> vtableData = new();
                List<SymbolTable.Record> methodRecords = new ();
                foreach(var (key, rec) in classTable.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Function)
                        continue;

                    int index = rec.index;
                    rec.addr = index * 8;

                    methodRecords.Add(rec);
                }
                methodRecords.Sort((x, y) => x.index.CompareTo(y.index));
                foreach(var methodRec in methodRecords)
                {
                    var targetMethod = ir.vtables[classRec.name].Query(methodRec.name);
                    vtableData.Add(X64.label(targetMethod.funcfullname));
                }

                //填充rodata  
                string rokey = $"vtable_{classRec.name}";
                roDataSeg.Add(rokey, vtableData);
                vtableRoKeys.Add(classRec.name, rokey);
            }
        }

        private void GenFuncInfo(SymbolTable funcTable)
        {
            //参数偏移  
            {
                foreach(var (key, rec) in funcTable.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Param)
                        continue;
                    rec.addr = rec.index * 8;
                }
            }
        }

        #endregion

        #region PASS1

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
                        DEF(tac.arg0, lineNum, block, envStack);
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                case "%=":
                    {
                        // arg1 += arg2
                        USE(tac.arg0, lineNum, block, envStack);
                        DEF(tac.arg0, lineNum, block, envStack);
                        USE(tac.arg1, lineNum, block, envStack);
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
                        DEF(tac.arg0, lineNum, block, envStack);
                        USE(tac.arg1, lineNum, block, envStack);
                        USE(tac.arg2, lineNum, block, envStack);
                    }
                    break;
                case "NEG":
                case "!":
                case "CAST":
                    {
                        // arg1 = op arg2
                        DEF(tac.arg0, lineNum, block, envStack);
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "++":
                case "--":
                    {
                        // ++arg1 或 --arg1
                        USE(tac.arg0, lineNum, block, envStack);
                        DEF(tac.arg0, lineNum, block, envStack);
                    }
                    break;
                case "IF_FALSE_JUMP":
                    {
                        USE(tac.arg0, lineNum, block, envStack);
                    }
                    break;
                case "RETURN":
                    {
                        if(!string.IsNullOrEmpty(tac.arg0))
                        {
                            USE(tac.arg0, lineNum, block, envStack);
                        }
                    }
                    break;
                case "PARAM":
                    {
                        USE(tac.arg0, lineNum, block, envStack);
                    }
                    break;
                case "ALLOC":
                    {
                        // arg1 = alloc
                        DEF(tac.arg0, lineNum, block, envStack);
                    }
                    break;
                case "ALLOC_ARRAY":
                    {
                        // arg1 = alloc_array arg2
                        DEF(tac.arg0, lineNum, block, envStack);
                        USE(tac.arg1, lineNum, block, envStack);
                    }
                    break;
                case "DEL":
                    {
                        USE(tac.arg0, lineNum, block, envStack);
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
            var irOperand = GetIROperandInfo(operand);

            switch(irOperand.type)
            {
                case IROperandExpr.Type.Label:
                        return null;
                case IROperandExpr.Type.LitOrConst:
                    return null;
                case IROperandExpr.Type.Identifier:
                    {
                        return irOperand.segments[0];
                    }
                    break;
                case IROperandExpr.Type.ClassMemberAccess:
                    {
                        return irOperand.segments[0];
                    }
                case IROperandExpr.Type.ArrayElementAccess:
                    {
                        return irOperand.segments[0];
                    }
            }
            return null;
        }

        private void USE(string operand, int lineNum, BasicBlock block, Gizbox.GStack<SymbolTable> envStack)
        {
            if(string.IsNullOrEmpty(operand))
                return;

            var operandVar = AnalyzeOprand(operand);

            var record = Query(operandVar, envStack);
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

            var record = Query(operandVar, envStack);
            if(record != null)
            {
                if(!block.DEF.ContainsKey(record))
                {
                    block.DEF[record] = new List<int>();
                }
                block.DEF[record].Add(lineNum);
            }
        }

        #endregion

        #region PASS2

        /// <summary>
        /// 读取只读数据（复杂字面量）
        /// </summary>
        private void CollectReadOnlyData(string operand, int line)
        {
            if(string.IsNullOrEmpty(operand))
                return;

            var iroperand = GetIROperandInfo(operand);
            switch(iroperand.type)
            {
                case IROperandExpr.Type.LitOrConst:
                    {
                        switch(iroperand.segments[0])
                        {
                            case "LITFLOAT":
                            case "LITDOUBLE":
                                {
                                    var key = GetConstSymbol(iroperand.segments[0], iroperand.segments[1]);
                                    iroperand.roDataKey = key;
                                    if(roDataSeg.ContainsKey(key) == false)
                                    {
                                        roDataSeg[key] = iroperand;
                                    }
                                }
                                break;
                            case "CONSTSTRING":
                                {
                                    var key = GetConstSymbol(iroperand.segments[0], iroperand.segments[1]);
                                    iroperand.roDataKey = key;
                                    if(roDataSeg.ContainsKey(key) == false)
                                    {
                                        int cstIdx = int.Parse(iroperand.segments[1]);
                                        roDataSeg[key] = ir.constData[cstIdx];
                                    }
                                }
                                break;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// TAC操作数转换为X64格式(使用虚拟寄存器)
        /// </summary>
        private X64Operand ParseOperand(IROperand irOperand)
        {
            if(irOperand == null)
                return null;

            var iroperandExpr = irOperand.expr;

            switch(iroperandExpr.type)
            {
                case IROperandExpr.Type.Label:
                    {
                        return X64.label(iroperandExpr.segments[0]);
                    }
                case IROperandExpr.Type.LitOrConst:
                    {
                        switch(iroperandExpr.segments[0])
                        {
                            case "LITBOOL":
                                {
                                    var value = iroperandExpr.segments[1] == "True" ? 1L : 0L;
                                    return X64.imm(value);
                                }
                                break;
                            case "LITINT":
                                {
                                    var value = long.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "LITLONG":
                                {
                                    var value = long.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "LITCHAR":
                                {
                                    var charValue = iroperandExpr.segments[1];
                                    return X64.imm((long)charValue[0]);
                                }
                                break;
                            case "LITNULL":
                                {
                                    return X64.imm(0);
                                }
                                break;
                            case "LITFLOAT":
                            case "LITDOUBLE":
                                {
                                    return X64.rel(iroperandExpr.roDataKey);
                                }
                                break;
                            case "CONSTSTRING":
                                {
                                    return X64.rel(iroperandExpr.roDataKey);
                                }
                                break;
                        }
                    }
                    break;
                case IROperandExpr.Type.RET:
                    {
                        if(irOperand.IsSSEType())
                        {
                            return X64.xmm0;
                        }
                        else
                        {
                            return X64.rax;
                        }
                    }
                    break;
                case IROperandExpr.Type.Identifier:
                    {
                        string varName = iroperandExpr.segments[0];
                        var varRec = irOperand.segmentRecs[0];

                        if(varRec.category == SymbolTable.RecordCatagory.Variable)
                        {
                            return vreg(varRec);
                        }
                        else if(varRec.category == SymbolTable.RecordCatagory.Param)
                        {
                            long offset = varRec.addr;
                            return X64.mem(X64.rbp, displacement:offset);
                        }
                    }
                    break;
                case IROperandExpr.Type.ClassMemberAccess:
                    {
                        var objName = iroperandExpr.segments[0];
                        var fieldName = iroperandExpr.segments[1];
                        var objRec = irOperand.segmentRecs[0];
                        var fieldRec = irOperand.segmentRecs[1];

                        if(objRec == null)
                            throw new GizboxException(ExceptioName.Unknown, $"object not found for member access \"{objName}.{fieldName}\" at line {irOperand.owner.line}");
                        if(fieldRec == null)
                            throw new GizboxException(ExceptioName.Unknown, $"field not found for member access \"{objName}.{fieldName}\" at line {irOperand.owner.line}");

                        // 对象变量是一个指针值，在虚拟寄存器
                        var baseVreg = vreg(objRec);

                        // 生成内存操作数
                        return X64.mem(baseVreg, displacement: fieldRec.addr);
                    }
                    break;
                case IROperandExpr.Type.ArrayElementAccess:
                    {
                        var arrayName = iroperandExpr.segments[0];
                        var indexExpr = iroperandExpr.segments[1];
                        var arrayRec = irOperand.segmentRecs[0];
                        if(arrayRec == null)
                            throw new GizboxException(ExceptioName.Unknown, $"array not found for element access \"{arrayName}[{indexExpr}]\" at line {irOperand.owner.line}");

                        // 元素大小
                        var elemType = UtilsW64.SliceArrayElementType(arrayRec.typeExpression);
                        int elemSize = UtilsW64.GetTypeSize(elemType);

                        var baseV = vreg(arrayRec);

                        // 所有可能是LITINT或者变量  
                        var idxRec = Query(indexExpr, irOperand.owner.line);
                        if(idxRec != null)
                        {
                            var idxV = vreg(idxRec);
                            return X64.mem(baseV, idxV, elemSize, 0);
                        }
                        else
                        {
                            long immIndex = 0;
                            if(indexExpr != null && indexExpr.StartsWith("LITINT:"))
                            {
                                var lit = indexExpr.Substring("LITINT:".Length);
                                immIndex = long.Parse(lit);
                                long disp = checked(immIndex * elemSize);
                                return X64.mem(baseV, displacement: disp);
                            }

                            throw new GizboxException(ExceptioName.Unknown, $"unsupported array index expression \"{indexExpr}\" at line {irOperand.owner.line}");
                        }
                    }
                    break;
            }

            return null;
        }


        public IROperandExpr GetIROperandInfo(string rawoperand)
        {
            //可能的格式示例：
            //  数组访问：[arr[x]]
            //  成员访问：[obj.x]
            //  局部变量访问：[var1]
            //  全局变量访问：[globalvar1]
            //  字面量:LITINT:123
            //  常量：CONSTSTRING:123  //第123个字符串常量

            if(operandCache.TryGetValue(rawoperand, out var result))
            {
                return result;
            }
            else
            {
                IROperandExpr irOperand = new();

                // 移除方括号
                if(rawoperand.StartsWith("[") && rawoperand.EndsWith("]"))
                {
                    string operand = rawoperand.Substring(1, rawoperand.Length - 2);

                    if(operand.Contains('.'))
                    {
                        //成员访问：[obj.x]
                        var parts = operand.Split('.');
                        irOperand.type = IROperandExpr.Type.ClassMemberAccess;
                        irOperand.segments = parts;
                    }
                    else if(operand.Contains('['))
                    {
                        //数组访问：[arr[x]]
                        var parts = operand.Split('[');
                        var arrName = parts[0];
                        var indexExpr = parts[1].Substring(0, parts[1].Length - 1);//去掉']'
                        irOperand.type = IROperandExpr.Type.ArrayElementAccess;
                        irOperand.segments = new[] { arrName, indexExpr };
                    }
                    else
                    {
                        //局部变量或全局变量访问
                        irOperand.type = IROperandExpr.Type.Identifier;
                        irOperand.segments = new[] { operand };
                    }
                }
                //字面量或者常量  
                else if(rawoperand.Contains(':'))
                {
                    var parts = rawoperand.Split(':');
                    irOperand.type = IROperandExpr.Type.LitOrConst;
                    irOperand.segments = parts;
                }
                //其他
                else
                {
                    //返回值
                    if(rawoperand == "RET")
                    {
                        irOperand.type = IROperandExpr.Type.RET;
                        irOperand.segments = new[] { "RET" };
                    }
                    //标签  
                    else
                    {
                        irOperand.type = IROperandExpr.Type.Label;
                        irOperand.segments = new[] { rawoperand };
                    }
                }

                //缓存
                operandCache.Add(rawoperand, irOperand);

                return irOperand;
            }
        }

        private string GetConstSymbol(string littype, string litvalue)
        {
            return $"{littype}_{litvalue.Replace('.', '_')}";
        }

        private void AddTacInfo(TAC tac, int line)
        {
            TACInfo inf = new TACInfo(this, tac, line);
            tacInfoCache[tac] = inf;
        }

        private TACInfo GetTacInfo(TAC tac)
        {
            if(tacInfoCache.TryGetValue(tac, out var inf))
            {
                return inf;
            }
            return null;
        }


        public X64Reg vreg(SymbolTable.Record varrec)
        {
            var xoperand = new X64Reg(varrec);

            //局部变量  
            if(funcEnv != null)
            {
                vRegs[funcEnv].TryAdd(varrec, xoperand);
            }
            //全局变量
            else
            {
                vRegs[globalEnv].TryAdd(varrec, xoperand);
            }

            return xoperand;
        }

        #endregion

        /// <summary>
        /// 向后查找指令  
        /// </summary>
        public TACInfo Lookahead(int currLine, string targetOp, bool limitScope = true)
        {
            var currScopeStatusIdx = ir.scopeStatusArr[currLine];

            for(int i = currLine + 1; i < ir.codes.Count; ++i)
            {
                if(ir.codes[i].op == targetOp)
                {
                    var inf = GetTacInfo(ir.codes[i]);
                    return inf;
                }
                //超出作用域  
                if(limitScope && ir.scopeStatusArr[i] != currScopeStatusIdx)
                {
                    break;
                }
            }
            return null;
        }

        /// <summary>
        /// 向前查找指令
        /// </summary>
        public TACInfo Lookback(int currLine, string targetOp, bool limitScope = true)
        {
            var currScopeStatusIdx = ir.scopeStatusArr[currLine];

            for(int i = currLine - 1; i >= 0; --i)
            {
                if(ir.codes[i].op == targetOp)
                {
                    var inf = GetTacInfo(ir.codes[i]);
                    return inf;
                }
                //超出作用域  
                if(limitScope && ir.scopeStatusArr[i] != currScopeStatusIdx)
                {
                    break;
                }
            }
            return null;
        }


        private SymbolTable.Record QueryMember(string objDefineType, string memberName)
        {
            if(classDict.Count == 0)
                return null;
            return QueryMember(classDict[objDefineType], memberName);
        }
        private SymbolTable.Record QueryMember(SymbolTable.Record classRec, string memberName)
        {
            return classRec.envPtr.GetRecord(memberName);
        }

        private SymbolTable.Record Query(string name, int line)
        {
            var status = ir.scopeStatusArr[line];
            ir.stackDic.TryGetValue(status, out var envStack);
            if(envStack != null)
            {
                var rec = Query(name, envStack);
                if(rec != null)
                    return rec;
            }

            var globalRec = ir.QueryTopSymbol(name);
            return globalRec;
        }
        private SymbolTable.Record Query(string name, Gizbox.GStack<SymbolTable> envStack)
        {
            if(name == null)
                return null;
            if(envStack == null)
                throw new GizboxException(ExceptioName.Unknown, "envStack is null!");
            
            for(int i = envStack.Count - 1; i >= 0; i--)
            {
                var env = envStack[i];
                if(env.ContainRecordName(name))
                {
                    var record = env.GetRecord(name);
                    return record;
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

        public class IROperandExpr
        {
            ///操作数表达式（和变量和值并不是一一对应，值可能重复，变量在不同作用域可能重名）
            
            public enum Type
            {
                Label,//xxx    //Label, ClassName
                RET, //RET     //Return value
                LitOrConst,//LIT:xxx   //Literal, Const value
                Identifier,//[xxx]     //Variale, Function

                ClassMemberAccess,//[aaa.bbb]     //MemberAccess
                ArrayElementAccess, //[aaa[bbb]]  //ElementAccess
            }

            public Type type;
            public string[] segments;

            public string roDataKey;//rodata的name，用于字面量
        }

        public class TACInfo
        {
            /// TAC附加信息  
            public Win64CodeGenContext context;

            public TAC tac;
            public int line;

            public IROperand oprand0;
            public IROperand oprand1;
            public IROperand oprand2;

            public List<IROperand> allOprands = new();


            public int PARAM_paramidx;//参数索引（如果是PARAM指令）
            public int CALL_paramCount;//参数个数（如果是CALL指令）
            public IROperand MCALL_methodTargetObject;//this参数（如果是MCALL指令）

            public TACInfo(Win64CodeGenContext context, TAC tac, int line)
            {
                this.context = context;
                this.tac = tac;
                this.line = line;
            }
            public void FinishInfo()
            {
                if(string.IsNullOrEmpty(tac.arg0) == false)
                {
                    oprand0 = new IROperand(context, this, 0, tac.arg0);
                    allOprands.Add(oprand0);
                }
                if(string.IsNullOrEmpty(tac.arg1) == false)
                {
                    oprand1 = new IROperand(context, this, 1, tac.arg1);
                    allOprands.Add(oprand1);
                }
                if(string.IsNullOrEmpty(tac.arg2) == false)
                {
                    oprand2 = new IROperand(context, this, 2, tac.arg2);
                    allOprands.Add(oprand2);
                }

                foreach(var operand in allOprands)
                {
                    if(operand.expr.type == IROperandExpr.Type.RET)
                    {
                        
                    }
                }
            }
        }
        public class IROperand
        {
            ///操作数实例（和tac中的操作数一一对应）
            public TACInfo owner;
            public int operandIdx;
            public IROperandExpr expr;

            public string[] segments => expr.segments;//子操作数

            public string typeExpression;//类型  
            public SymbolTable.Record[] segmentRecs;//变量的符号表条目（如果子操作数是变量）  
            public SymbolTable.Record RET_functionRec;//返回值的函数符号表条目（如果是RET操作数）

            public IROperand(Win64CodeGenContext context, TACInfo tacinf, int operandIdx, string rawoperand)
            {
                this.owner = tacinf;
                this.expr = context.GetIROperandInfo(rawoperand);
                this.operandIdx = operandIdx;
                this.segmentRecs = new SymbolTable.Record[segments.Length];
                if(expr.type == IROperandExpr.Type.Identifier)//Function Or Variable
                {
                    SymbolTable.Record rec;
                    if(owner.tac.op == "MCALL" && operandIdx == 0)
                    {
                        var className = owner.MCALL_methodTargetObject.typeExpression;
                        rec = context.QueryMember(className, segments[0]);
                        segmentRecs[0] = rec;
                        typeExpression = rec.typeExpression;
                    }
                    else
                    {
                        rec = context.Query(segments[0], tacinf.line);
                        if(rec == null) throw new GizboxException(ExceptioName.Unknown, $"cannot find variable {segments[0]} at line {tacinf.line}");
                        segmentRecs[0] = rec;
                        typeExpression = rec.typeExpression;
                    }
                }
                else if(expr.type == IROperandExpr.Type.ClassMemberAccess)
                {
                    if(segments[1] != "ctor")
                    {
                        var objRec = context.Query(segments[0], tacinf.line);
                        string className = objRec.typeExpression;
                        var classRec = context.classDict[className];
                        var memberRec = context.QueryMember(className, segments[1]);
                        Console.WriteLine("成员类型：" + memberRec.category.ToString());
                        segmentRecs[0] = objRec;
                        segmentRecs[1] = memberRec;
                        typeExpression = memberRec.typeExpression;
                    }
                    else
                    {
                        var objRec = context.Query(segments[0], tacinf.line);
                        segmentRecs[0] = objRec;
                        segmentRecs[1] = null;
                        typeExpression = objRec.name;
                    }
                }
                else if(expr.type == IROperandExpr.Type.ArrayElementAccess)
                {
                    var arrayRec = context.Query(segments[0], tacinf.line);
                    var indexRec = context.Query(segments[1], tacinf.line);
                    segmentRecs[0] = arrayRec;
                    segmentRecs[1] = indexRec;
                    typeExpression = UtilsW64.SliceArrayElementType(arrayRec.typeExpression);
                }
                else if(expr.type == IROperandExpr.Type.RET)
                {
                    var call = context.Lookback(this.owner.line, "CALL");
                    var mcall = context.Lookback(this.owner.line, "CALL");

                    TACInfo lastCall = null;
                    if(call != null && mcall != null)
                    {
                        if(call.line > mcall.line)
                            lastCall = call;
                        else
                            lastCall = mcall;
                    }
                    else if(call != null)
                    {
                        lastCall = call;
                    }
                    else if(mcall != null)
                    {
                        lastCall = mcall;
                    }
                    else
                    {
                        throw new GizboxException(ExceptioName.Unknown, $"no CALL or MCALL before RET at line {this.owner.line}");
                    }

                    this.RET_functionRec = lastCall.oprand0.segmentRecs[0];

                    if(this.RET_functionRec == null)
                    {
                        throw new GizboxException(ExceptioName.Unknown, $"no function record for RET at line {this.owner.line}");
                    }

                    typeExpression = UtilsW64.SliceFunctionRetType(RET_functionRec.typeExpression);

                }
                else if(expr.type == IROperandExpr.Type.LitOrConst)
                {
                    typeExpression = UtilsW64.GetLitConstType(segments[0]);
                }
                else if(expr.type == IROperandExpr.Type.Label)
                {
                    typeExpression = "(label)";
                }
            }

            public bool IsSSEType()
            {
                //判断是否是SSE类型的字面量
                switch(expr.type)
                {
                    case IROperandExpr.Type.LitOrConst:
                        {
                            switch(expr.segments[0])
                            {
                                case "LITFLOAT":
                                case "LITDOUBLE":
                                    return true;
                                default:
                                    return false;
                            }
                        }
                        break;
                    case IROperandExpr.Type.RET:
                        {
                            return UtilsW64.IsSSETypeExpression(UtilsW64.SliceFunctionRetType(RET_functionRec.typeExpression));
                        }
                    case IROperandExpr.Type.Identifier:
                        {
                            return UtilsW64.IsSSETypeExpression(segmentRecs[0].typeExpression); 
                        }
                        break;
                    case IROperandExpr.Type.ClassMemberAccess:
                        {
                            return UtilsW64.IsSSETypeExpression(segmentRecs[1].typeExpression);
                        }
                        break;
                    case IROperandExpr.Type.ArrayElementAccess:
                        {
                            var eleType = UtilsW64.SliceArrayElementType(segmentRecs[0].typeExpression);
                            return UtilsW64.IsSSETypeExpression(eleType);
                        }
                        break;
                }
                return false;
            }
        }


        public class TypeExpr
        {
            public static Dictionary<string, TypeExpr> typeExpressionCache = new();
            public enum Category
            {
                Int,
                Long,
                Float,
                Double,
                Bool,
                Char,
                String,
                Object, //引用类型
                Array, //数组类型
                Function, //函数类型
            }

            public Category type;
            public TypeExpr Object_Class;
            public TypeExpr Array_ElementType;
            public TypeExpr Function_ReturnType;
            public List<TypeExpr> Function_ParamTypes;

            private TypeExpr() { }

            public static TypeExpr Parse(string typeExpression)
            {
                typeExpression = typeExpression.Trim();

                if(typeExpressionCache.TryGetValue(typeExpression, out var cached))
                    return cached;

                if(typeExpression.Contains("->"))
                {

                }
                else if(typeExpression.EndsWith("[]"))
                {

                }
                else if(typeExpression.StartsWith("(") && typeExpression.EndsWith(")"))
                {

                }
                else
                {
                    switch(typeExpression)
                    {
                        case "bool":
                            {
                            }
                            break;
                        case "char":
                            {
                            }
                            break;
                        case "int":
                        {
                        }
                            break;
                        case "long":
                            {
                            }
                            break;
                        case "float":
                        {
                        }
                            break;
                        case "double":
                        {
                        }
                            break;
                        case "string":
                            {
                            }
                            break;
                        default:
                            {
                            }
                            break;

                    }
                }
            }

        }
        private class UtilsW64
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
            public static bool IsSSETypeExpression(string typeExpression)
            {
                switch(typeExpression)
                {
                    case "float":
                        return true;
                    case "double":
                        return true;
                    default:
                        return false;
                }
            }
            public static string SliceFunctionRetType(string funcTypeExpression)
            {
                if(funcTypeExpression.Contains("->") == false)
                    throw new GizboxException(ExceptioName.Unknown, $"not a function type:{funcTypeExpression}");

                Console.WriteLine("funcTypeExpr:" + funcTypeExpression);
                return "";
            }
            public static string SliceArrayElementType(string arrTypeExpress)
            {
                if(arrTypeExpress.EndsWith("[]") == false)
                    throw new GizboxException(ExceptioName.Unknown, $"not a array type:{arrTypeExpress}");
                var eleType = arrTypeExpress.Substring(0, arrTypeExpress.Length - 2);
                return eleType;
            }
            public static string GetLitConstType(string litconstMark)
            {
                switch(litconstMark)
                {
                    case "LITINT": 
                        return "int";
                    case "LITLONG":
                        return "long";
                    case "LITFLOAT":
                        return "float";
                    case "LITDOUBLE":
                        return "double";
                    case "LITBOOL":
                        return "bool";
                    case "LITCHAR":
                        return "char";
                    case "CONSTSTRING":
                        return "string";
                    default:
                        return litconstMark;
                }

            }

            public static X64Reg GetParamReg(int paramIdx, bool isSSE)
            {
                if(isSSE == false)
                {
                    switch(paramIdx)
                    {
                        case 0: return X64.rcx;
                        case 1: return X64.rdx;
                        case 2: return X64.r8;
                        case 3: return X64.r9;
                    }
                }
                else
                {
                    switch(paramIdx)
                    {
                        case 0:
                            return X64.xmm0;
                        case 1:
                            return X64.xmm1;
                        case 2:
                            return X64.xmm2;
                        case 3:
                            return X64.xmm3;
                    }
                }

                throw new GizboxException(ExceptioName.Unknown, $"param cannot pass by register. idx:{paramIdx}");
            }

            public static int GetTypeSize(string typeExpression)
            {
                switch(typeExpression)
                {
                    case "int":
                        return 4;
                    case "long":
                        return 8;
                    case "float":
                        return 4;
                    case "double":
                        return 8;
                    case "bool":
                        return 1;
                    case "char":
                        return 2;
                    case "string":
                        return 8; // 字符串64位指针
                    default:
                        // 引用类型64位指针
                        return 8;
                }
            }

        }
    }
}
