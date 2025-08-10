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

        private Dictionary<string, object> dataSeg = new();
        private Dictionary<string, object> roDataSeg = new();

        private Dictionary<string, IROperandExpr> operandCache = new();
        private Dictionary<TAC, TACInfo> tacInfoCache = new(); 


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
            //指令分析  
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
                    inf.paramidx = currParamIdx; 
                }
                else
                {
                    currParamIdx = -1;
                }

                // 读取字符串字面量和浮点数字面量到静态常量数据区  
                CollectReadOnlyData(tac.arg1, line);
                CollectReadOnlyData(tac.arg2, line);
                CollectReadOnlyData(tac.arg3, line);
            }


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
                            instructions.Add(X64.jmp(tac.arg1));
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            //函数序言
                            instructions.Add(X64.push(X64.rbp));
                            instructions.Add(X64.mov(X64.rbp, X64.rsp));

                            //保存寄存器（非易失性需要由Callee保存）  
                            //todo;
                        }
                        break;
                    case "FUNC_END":
                        {
                            //函数尾声
                            instructions.Add(X64.mov(X64.rsp, X64.rbp));
                            instructions.Add(X64.pop(X64.rbp));
                            instructions.Add(X64.ret());
                        }
                        break;
                    case "RETURN":
                        {
                            // 如果有返回值
                            if(!string.IsNullOrEmpty(tac.arg1))
                            {
                                var returnValue = ParseOperand(tacInf.oprand1);

                                //按照win64约定，整数返回值存储在rax寄存器中，浮点数返回值存储在xmm0寄存器中

                                // 根据返回值类型选择寄存器
                                var tacinfo = GetTacInfo(tac);
                                if(tacinfo.oprand1.IsSSEType())
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

                            // 函数尾声（和FUNC_END相同操作）  
                            instructions.Add(X64.mov(X64.rsp, X64.rbp));
                            instructions.Add(X64.pop(X64.rbp));
                            instructions.Add(X64.ret());
                        }
                        break;
                    case "EXTERN_IMPL"://无需处理
                        break;
                    case "PARAM":
                        {
                            if(tacInf.paramidx < 4)
                            {
                                //寄存器传参  
                                UtilsW64.GetParamReg(tacInf.paramidx, tacInf.oprand1.IsSSEType());
                            }
                            else
                            {
                                //栈帧传参(IR中PARAM指令已经是倒序)
                                instructions.Add(X64.push(ParseOperand(tacInf.oprand1)));
                            }
                        }
                        break;
                    case "CALL":
                        {
                            // IRCall指令，第一参数是[函数名]，第二个参数是参数个数
                            string funcName = tac.arg1.Substring(1, tac.arg1.Length - 2);
                            instructions.Add(X64.call(funcName));


                            // 影子空间
                            // todo;


                            // 保存调用者寄存器（易失性寄存器需Caller保存） (选择性保存)
                            // todo;

                            // RAX, RCX, RDX, R8, R9, R10, R11、 XMM0-XMM5 是调用者保存寄存器
                            // RBX、RBP、RDI、RSI、RSP、R12、R13、R14、R15 和 XMM6 - XMM15 由使用它们的函数保存和还原，视为非易失性。

                            // 实际的函数调用
                            instructions.Add(X64.call(funcName));

                            // 栈平衡：清理栈上的参数（如果有超过4个参数）
                            if(tacInf.paramCount > 4)
                            {
                                int stackParamCount = tacInf.paramCount - 4;
                                long stackCleanupBytes = stackParamCount * 8; // 每个参数8字节
                                instructions.Add(X64.add(X64.rsp, X64.imm(stackCleanupBytes)));
                            }
                        }
                        break;
                    case "MCALL":
                        {//todo:虚函数表查找调用
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
                        {//todo
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


        # region PASS1

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
            var irOperand = GetIROperandInfo(operand);

            switch(irOperand.type)
            {
                case IROperandExpr.Type.Label:
                        return null;
                case IROperandExpr.Type.LitOrConst:
                    return null;
                case IROperandExpr.Type.Var:
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

        #endregion

        #region PASS2

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
                            case "LITINT":
                                {
                                    var value = long.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "LITBOOL":
                                {
                                    var value = iroperandExpr.segments[1] == "True" ? 1L : 0L;
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
                        //todo
                    }
                    break;
                case IROperandExpr.Type.Var:
                    {
                        //todo
                        //return X64.vreg($"v_{expr}");
                    }
                    break;
                case IROperandExpr.Type.ClassMemberAccess:
                    {
                        var objName = iroperandExpr.segments[0];
                        var fieldName = iroperandExpr.segments[1];

                        //todo
                        //return X64.mem($"{objName}_obj", 0);
                    }
                    break;
                case IROperandExpr.Type.ArrayElementAccess:
                    {
                        var arrayName = iroperandExpr.segments[0];
                        var indexExpr = iroperandExpr.segments[1];

                        // 这里简化处理，实际需要更复杂的地址计算
                        //return X64.mem($"{arrayName}_array", 0);
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
                        irOperand.type = IROperandExpr.Type.ArrayElementAccess;
                        irOperand.segments = parts;
                    }
                    else
                    {
                        //局部变量或全局变量访问
                        irOperand.type = IROperandExpr.Type.Var;
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
        #endregion


        private SymbolTable.Record QueryVariable(string varName, int line)
        {
            var status = ir.scopeStatusArr[line];
            ir.stackDic.TryGetValue(status, out var envStack);
            if(envStack != null)
            {
                var rec = QueryVariable(varName, envStack);
                if(rec != null)
                    return rec;
            }

            var globalRec = ir.QueryTopSymbol(varName);
            return globalRec;
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

        public class IROperandExpr
        {
            ///操作数表达式（和变量和值并不是一一对应，值可能重复，变量在不同作用域可能重名）
            
            public enum Type
            {
                Label,
                RET,
                LitOrConst,
                Var,

                ClassMemberAccess,
                ArrayElementAccess,
            }

            public Type type;
            public string[] segments;

            public string roDataKey;//rodata的name，用于字面量
        }

        public class TACInfo
        {
            /// TAC附加信息  

            public TAC owner;
            public int ownerline;

            public IROperand oprand1;
            public IROperand oprand2;
            public IROperand oprand3;

            public List<IROperand> allOprands = new();


            public int paramidx;//参数索引（如果是PARAM指令）
            public int paramCount;//参数个数（如果是CALL指令）

            public TACInfo(Win64CodeGenContext context, TAC tac, int line)
            {
                owner = tac;
                ownerline = line;

                if(string.IsNullOrEmpty(tac.arg1) == false)
                {
                    oprand1 = new IROperand(context, this, tac.arg1);
                    allOprands.Add(oprand1);
                }
                if(string.IsNullOrEmpty(tac.arg2) == false)
                {
                    oprand2 = new IROperand(context, this, tac.arg2);
                    allOprands.Add(oprand2);
                }
                if(string.IsNullOrEmpty(tac.arg3) == false)
                {
                    oprand3 = new IROperand(context, this, tac.arg3);
                    allOprands.Add(oprand3);
                }

                foreach(var operand in allOprands)
                {
                    if(operand.expr.type == IROperandExpr.Type.RET)
                    {
                        var call = context.Lookback(line, "CALL");
                        var mcall = context.Lookback(line, "CALL");

                        TACInfo lastCall = null;
                        if(call != null && mcall != null)
                        {
                            if(call.ownerline > mcall.ownerline)
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
                            throw new GizboxException(ExceptioName.Unknown, $"no CALL or MCALL before RET at line {line}");
                        }

                        operand.funcOfRet = lastCall.oprand1.segmentRecs[0];

                        if(operand.funcOfRet == null)
                        {
                            throw new GizboxException(ExceptioName.Unknown, $"no function record for RET at line {line}");
                        }
                            
                    }
                }
            }
        }
        public class IROperand
        {
            ///操作数实例（和tac中的操作数一一对应）
            public TACInfo owner;
            public IROperandExpr expr;

            public string[] segments => expr.segments;//子操作数
            public SymbolTable.Record[] segmentRecs;//变量的符号表条目（如果子操作数是变量）  

            public SymbolTable.Record funcOfRet;//返回值的函数符号表条目（如果是RET操作数）

            public IROperand(Win64CodeGenContext context, TACInfo tacinf, string rawoperand)
            {
                owner = tacinf;
                expr = context.GetIROperandInfo(rawoperand);
                segmentRecs = new SymbolTable.Record[segments.Length];
                if(expr.type == IROperandExpr.Type.Var)
                {
                    var rec = context.QueryVariable(segments[0], tacinf.ownerline);
                    if(rec == null) throw new GizboxException(ExceptioName.Unknown, $"cannot find variable {segments[0]} at line {tacinf.ownerline}");
                    segmentRecs[0] = rec;
                }
                else if(expr.type == IROperandExpr.Type.ClassMemberAccess)
                {
                    var objRec = context.QueryVariable(segments[0], tacinf.ownerline);
                    var fieldRec = context.QueryVariable(segments[1], tacinf.ownerline);
                    segmentRecs[0] = objRec;
                    segmentRecs[1] = fieldRec;
                }
                else if(expr.type == IROperandExpr.Type.ArrayElementAccess)
                {
                    var arrayRec = context.QueryVariable(segments[0], tacinf.ownerline);
                    var indexRec = context.QueryVariable(segments[1], tacinf.ownerline);
                    segmentRecs[0] = arrayRec;
                    segmentRecs[1] = indexRec;
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
                            return UtilsW64.IsSSETypeExpression(UtilsW64.GetFunctionRetType(funcOfRet.typeExpression));
                        }
                    case IROperandExpr.Type.Var:
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
                            var eleType = UtilsW64.GetArrayElementType(segmentRecs[0].typeExpression);
                            return UtilsW64.IsSSETypeExpression(eleType);
                        }
                        break;
                }
                return false;
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
            public static string GetFunctionRetType(string funcTypeExpression)
            {
                if(funcTypeExpression.Contains("->") == false)
                    throw new GizboxException(ExceptioName.Unknown, $"not a function type:{funcTypeExpression}");

                Console.WriteLine("funcTypeExpr:" + funcTypeExpression);
                return "";
            }
            public static string GetArrayElementType(string arrTypeExpress)
            {
                if(arrTypeExpress.EndsWith("[]") == false)
                    throw new GizboxException(ExceptioName.Unknown, $"not a array type:{arrTypeExpress}");
                var eleType = arrTypeExpress.Substring(0, arrTypeExpress.Length - 2);
                return eleType;
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


        }
    }
}
