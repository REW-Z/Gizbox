using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;



//      (RAX, RCX, RDX, R8, R9, R10, R11、 XMM0-XMM5 是调用者保存的寄存器)
//      (RBX、RBP、RDI、RSI、RSP、R12、R13、R14、R15 和 XMM6 - XMM15 由使用它们的函数保存和还原，视为非易失性。)

//   内存-内存形式  :  dst/src 同为内存时，必须插入中转寄存器（整数用 R11，SSE 用 XMM0）两步搬运。



/*
 虚函数表定义：
 section .data
align 8
vtable_example:
    dq FuncA         ; 虚函数1的地址
    dq FuncB         ; 虚函数2的地址

section .text
global FuncA
global FuncB

FuncA:
    ; 函数实现
    ret

FuncB:
    ; 函数实现
    ret
 */



namespace Gizbox.Src.Backend
{
    public enum BuildMode
    {
        Debug,
        Release,
    }
    public static class Win64Target
    {
        public static void CodeGen(Compiler compiler, ILUnit ir)
        {
            Win64CodeGenContext context = new Win64CodeGenContext(compiler, ir);

            context.StartCodeGen();
        }
        public static void Log(string content)
        {
            if(!Compiler.enableLogCodeGen)
                return;
            GixConsole.LogLine("Win64 >>>" + content);
        }

        public static RecAdditionInfo GetAdditionInf(this SymbolTable.Record rec)
        {
            rec.runtimeAdditionalInfo ??= new RecAdditionInfo();
            return (rec.runtimeAdditionalInfo as RecAdditionInfo);
        }
    }

    public class Win64CodeGenContext
    {
        public Compiler compiler;
        public ILUnit ir;

        public static BuildMode buildMode = BuildMode.Debug;

        private ControlFlowGraph cfg;
        private List<BasicBlock> blocks;




        private Dictionary<string, IROperandExpr> operandCache = new();
        private Dictionary<TAC, TACInfo> tacInfoCache = new(); 


        private LList<X64Instruction> instructions = new();//1-n  1-1  n-1

        // 占位指令  
        private HashSet<X64Instruction> callerSavePlaceHolders = new();//主调函数保存寄存器指令占位
        private HashSet<X64Instruction> calleeSavePlaceHolders = new();//被调函数保存寄存器指令占位
        private HashSet<X64Instruction> callerRestorePlaceHolders = new();//主调函数恢复寄存器占位
        private HashSet<X64Instruction> calleeRestorePlaceHolders = new();//被调函数恢复寄存器占位



        // 数据段  
        private Dictionary<string, List<(GType typeExpr, string valExpr)>> section_data = new();
        private Dictionary<string, List<(GType typeExpr, string valExpr)>> section_rdata = new();
        private Dictionary<string, List<GType>> section_bss = new();


        // 本单元全局变量表  
        public Dictionary<string, SymbolTable.Record> globalVarInfos = new();
        // 本单元全局函数表  
        public Dictionary<string, SymbolTable.Record> globalFuncsInfos = new();
        // 外部变量表  
        public Dictionary<string, SymbolTable.Record> externVars = new();
        // 外部函数表  
        public Dictionary<string, SymbolTable.Record> externFuncs = new();



        // 类表
        public Dictionary<string, SymbolTable.Record> classDict = new();
        // 函数表  
        public Dictionary<string, SymbolTable.Record> funcDict = new();

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
        private const bool debugLogAsmInfos = true;




        public Win64CodeGenContext(Compiler compiler, ILUnit ir)
        {
            this.compiler = compiler;
            this.ir = ir;

            //加载依赖单元、附加符号表信息   
            foreach(var depName in ir.dependencies)
            {
                //Load Lib  
                var depUnit = compiler.LoadLib(depName);
                ir.AddDependencyLib(depUnit);
            }

            //本单元
            {
                var gEnv = ir.globalScope.env;
                foreach(var (_, gRec) in gEnv.records)
                {
                    var inf = gRec.GetAdditionInf();
                    inf.isGlobal = true;
                    inf.table = gEnv;
                }

                foreach(var scope in ir.scopes)
                {
                    var lEnv = scope.env;
                    foreach(var (_, lrec) in lEnv.records)
                    {
                        var inf = lrec.GetAdditionInf();
                        inf.table = lEnv;
                    }
                }
            }
            //依赖单元
            foreach(var dep in ir.dependencyLibs)
            {
                var gEnv = dep.globalScope.env;
                foreach(var (_, gRec) in gEnv.records)
                {
                    var inf = gRec.GetAdditionInf();
                    inf.isGlobal = true;
                    inf.table = gEnv;
                }

                foreach(var scope in dep.scopes)
                {
                    var lEnv = scope.env;
                    foreach(var (_, lrec) in lEnv.records)
                    {
                        var inf = lrec.GetAdditionInf();
                        inf.table = lEnv;
                    }
                }
            }
        }

        public void StartCodeGen()
        {
            Pass1();
            Pass2();
            Pass3();
            Pass4();
        }


        /// <summary> 静态信息补充 </summary>
        private void Pass1()
        {
            this.globalEnv = ir.globalScope.env;

            //收集全局和外部信息    
            var envs = ir.GetAllGlobalEnvs();
            foreach(var env in envs)
            {
                //本编译单元  
                if(env == ir.globalScope.env)
                {
                    foreach(var (_, rec) in env.records)
                    {
                        //全局变量  
                        if(rec.category == SymbolTable.RecordCatagory.Variable)
                        {
                            rec.GetAdditionInf().isGlobal = true;
                            string key = rec.name;
                            string initval = rec.initValue;
                            section_data.Add(key, new() { GetStaticInitValue(rec) });
                            globalVarInfos.Add(rec.name, rec);
                        }    
                        //全局函数  
                        else if(rec.category == SymbolTable.RecordCatagory.Function)
                        {
                            rec.GetAdditionInf().isGlobal = true;
                            globalFuncsInfos.Add(rec.name, rec);
                        }
                    }
                }
                //依赖单元  
                else
                {
                    foreach(var (_, rec) in env.records)
                    {
                        if(rec.category == SymbolTable.RecordCatagory.Variable)
                        {
                            ////外部变量  (只需要导入必须的外部变量)
                            //externVars.Add(rec.name, rec);
                        }
                        else if(rec.category == SymbolTable.RecordCatagory.Function)
                        {
                            ////外部函数  （只需要导入必须的外部函数）
                            //if(rec.name != "Main")
                            //{
                            //    externFuncs.Add(rec.name, rec);
                            //}
                        }

                    }
                }
            }

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
                            GenFuncInfo(r);
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
                    Win64Target.Log("原指令：" + tacInf.tac.ToExpression());

                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    if(tacInf.oprand0 != null) 
                        Win64Target.Log("    op0：typeExpr:" + (tacInf.oprand0.typeExpr?.ToString() ?? "?") + "  type:  " + tacInf.oprand0.expr?.type.ToString() ?? "");
                    if(tacInf.oprand1 != null)
                        Win64Target.Log("    op1：typeExpr:" + (tacInf.oprand1.typeExpr?.ToString() ?? "?") + "  type:  " + tacInf.oprand1.expr?.type.ToString() ?? "");
                    if(tacInf.oprand2 != null)
                        Win64Target.Log("    op2：typeExpr:" + (tacInf.oprand2.typeExpr?.ToString() ?? "?") + "  type:  " + tacInf.oprand2.expr?.type.ToString() ?? "");

                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        /// <summary> 划分基本块和活跃性分析 </summary>
        private void Pass2()
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
                throw new GizboxException(ExceptioName.CodeGen,
                    $"活跃变量分析未收敛，迭代次数超过 {maxIterations}");
            }

            // 计算活跃区间  
            foreach(var b in blocks)
            {
                b.CaculateLiveRanges();
            }
            cfg.CaculateLiveInfos();

            // 初步确定局部变量的栈帧布局  
            if(buildMode == BuildMode.Debug)
            {
                //Debug:局部变量内存不重叠  
                foreach(var (funcName, funcRec) in funcDict)
                {
                    var table = funcRec.envPtr;

                    List<SymbolTable.Record> localVars = new();
                    foreach(var (memberName, memberRec) in table.records)
                    {
                        if(memberRec.category != SymbolTable.RecordCatagory.Variable)
                            continue;
                        localVars.Add(memberRec);
                    }

                    (int size, int align)[] localvarinfo = new (int size, int align)[localVars.Count];
                    for(int i = 0; i < localVars.Count; ++i)
                    {
                        var type = GType.Parse(localVars[i].typeExpression);
                        localvarinfo[i] = (type.Size, type.Align);
                    }
                    long[] result;
                    var frameSize = MemUtility.LocalVarLayout(localvarinfo, out result);
                    for(int i = 0; i < localVars.Count; ++i)
                    {
                        localVars[i].addr = result[i];
                    }
                    funcRec.size = frameSize;
                }
            }
            else
            {
                //Release:根据变量活跃区间确定内存重用
                throw new NotImplementedException();
            }

            // 输出活跃变量分析结果
            if(debugLogBlockInfos)
            {
                foreach(var b in blocks)
                {
                    Win64Target.Log("\n\n---------block len" + (b.endIdx - b.startIdx) + "------------");
                    for(int i = b.startIdx; i <= b.endIdx; ++i)
                    {
                        Win64Target.Log(ir.codes[i].ToExpression());
                    }

                    Win64Target.Log("  USE: " + string.Join(", ", b.USE.Keys.Select(v => v.name)));
                    Win64Target.Log("  DEF: " + string.Join(", ", b.DEF.Keys.Select(v => v.name)));
                    Win64Target.Log("  IN:  " + string.Join(", ", b.IN.Select(v => v.name)));
                    Win64Target.Log("  OUT: " + string.Join(", ", b.OUT.Select(v => v.name)));
                }
            }
        }

        /// <summary> 指令选择 </summary>
        private void Pass3()
        {
            vRegs.Add(globalEnv, new());

            // 参数临时列表
            List<TACInfo> tempParamList = new();
            // 上一个IR生成的X64指令  
            LList<X64Instruction>.Node lastInstruction = null;

            // 指令选择  
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];
                var tacInf = GetTacInfo(tac);

                //空行  
                if(string.IsNullOrWhiteSpace(tac.op))
                {
                    if(string.IsNullOrEmpty(tac.label) == false)
                    {
                        instructions.AddLast(X64.emptyLine(tac.label));
                        //记录
                        lastInstruction = instructions.Last;
                    }
                    continue;
                }


                switch(tac.op)
                {
                    case "":
                        break;
                    case "JUMP":
                        {
                            instructions.AddLast(X64.jmp(tacInf.oprand0.segments[0]));
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            //函数作用域开始
                            funcEnv = ir.GetOutermostEnvAtLine(i);
                            vRegs.Add(funcEnv, new());


                            //函数序言
                            instructions.AddLast(X64.push(X64.rbp));
                            instructions.AddLast(X64.mov(X64.rbp, X64.rsp));

                            //保存寄存器（非易失性需要由Callee保存）  
                            var placeholder = X64.placehold("callee_save");
                            calleeSavePlaceHolders.Add(placeholder);
                            instructions.AddLast(placeholder);
                        }
                        break;
                    case "FUNC_END":
                        {
                            //恢复寄存器（非易失性需要由Callee保存） 
                            var placeholder = X64.placehold("callee_restore");
                            calleeRestorePlaceHolders.Add(placeholder);
                            instructions.AddLast(placeholder);

                            //函数尾声
                            instructions.AddLast(X64.mov(X64.rsp, X64.rbp));
                            instructions.AddLast(X64.pop(X64.rbp));
                            instructions.AddLast(X64.ret());

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
                                    instructions.AddLast(X64.mov(X64.xmm0, returnValue));
                                }
                                else
                                {
                                    // 整数/指针返回值 -> rax
                                    instructions.AddLast(X64.mov(X64.rax, returnValue));
                                }
                            }

                            // 跳转到FUNC_END  
                            string funcEndLabel = "func_end:" + funcEnv.name;
                            instructions.AddLast(X64.jmp(funcEndLabel));
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
                            string funcName = tac.arg0;


                            //调用前准备  
                            int rspSub = 0;
                            {
                                // 调用者保存寄存器（易失性寄存器需Caller保存） (选择性保存)
                                var placeholderSave = X64.placehold("caller_save");
                                instructions.AddLast(placeholderSave);
                                callerSavePlaceHolders.Add(placeholderSave);


                                // 栈帧16字节对齐 (如果是奇数个参数 -> 需要8字节对齐栈指针)
                                // (寄存器保存区自己确保16字节对齐)
                                if(tacInf.CALL_paramCount % 2 != 0)
                                {
                                    // 如果是奇数个参数，先将rsp对齐到16字节
                                    rspSub += 8;
                                }
                                // 其他栈参数空间
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int onStackParamLen = (tacInf.CALL_paramCount - 4) * 8;
                                    rspSub += onStackParamLen;
                                }
                                // 影子空间(32字节)
                                rspSub += 32;

                                //移动rsp  
                                instructions.AddLast(X64.sub(X64.rsp, X64.imm(rspSub)));

                                // 参数赋值(IR中PARAM指令已经是倒序)  
                                foreach(var paraminfo in tempParamList)
                                {
                                    var srcOp = ParseOperand(paraminfo.oprand0);
                                    bool isRefOfConst = paraminfo.oprand0.IsRefOfConst();

                                    //寄存器传参  
                                    if(paraminfo.PARAM_paramidx < 4)
                                    {
                                        var isSse = paraminfo.oprand0.IsSSEType();
                                        var ssereg = UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, true);
                                        var intreg = UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, false);
                                        //浮点参数  
                                        if(isSse)
                                        {
                                            if(paraminfo.oprand0.typeExpr.Size == 4)
                                                instructions.AddLast(X64.movss(ssereg, srcOp));
                                            else
                                                instructions.AddLast(X64.movsd(ssereg, srcOp));

                                            //浮点参数也需要在整数寄存器rcx,rdx,r8,r9中，以支持可变参数  
                                            instructions.AddLast(X64.mov(intreg, srcOp));
                                        }
                                        //整型参数  
                                        else
                                        {
                                            if(isRefOfConst)
                                            {
                                                instructions.AddLast(X64.lea(intreg, X64.rel(paraminfo.oprand0.expr.roDataKey)));
                                            }
                                            else
                                            {
                                                instructions.AddLast(X64.mov(intreg, srcOp));

                                            }
                                        }
                                    }

                                    //栈帧传参[rsp + 8]开始    （影子空间也需要赋值）
                                    int offset = (8 * (paraminfo.PARAM_paramidx));

                                    if(isRefOfConst)
                                    {
                                        instructions.AddLast(X64.lea(X64.r11, X64.rel(paraminfo.oprand0.expr.roDataKey)));
                                        EmitMov(X64.mem(X64.rsp, displacement: offset), X64.r11, paraminfo.oprand0.typeExpr);
                                    }
                                    else
                                    {
                                        EmitMov(X64.mem(X64.rsp, displacement: offset), srcOp, paraminfo.oprand0.typeExpr);
                                    }
                                }
                                tempParamList.Clear();
                            }


                            // 实际的函数调用
                            // （CALL 指令会自动把返回地址（下一条指令的 RIP）压入栈顶）  
                            instructions.AddLast(X64.call(funcName));

                            //调用后处理
                            {
                                // 调用后完整恢复栈
                                instructions.AddLast(X64.add(X64.rsp, X64.imm(rspSub)));

                                //还原保存的寄存器(占位)  
                                var placeholderRestore = X64.placehold("caller_restore");
                                instructions.AddLast(placeholderRestore);
                                callerRestorePlaceHolders.Add(placeholderRestore);
                            }
                        }
                        break;
                    case "MCALL":
                        {
                            // 第一参数是 方法名（未混淆），第二个参数是参数个数
                            string methodName = tac.arg0;


                            //this参数载入寄存器  
                            var codeParamObj = tempParamList.FirstOrDefault(c => c.PARAM_paramidx == 0);
                            var x64obj = ParseOperand(codeParamObj.oprand0);
                            instructions.AddLast(X64.mov(X64.rcx, x64obj));

                            //取Vptr  
                            var methodRec = QueryMember(codeParamObj.oprand0.typeExpr.ToString(), methodName);
                            instructions.AddLast(X64.mov(X64.rax, X64.mem(X64.rcx, displacement: 0)));
                            //函数地址（addr表示在虚函数表中的偏移(Index*8)）  
                            instructions.AddLast(X64.mov(X64.rax, X64.mem(X64.rax, displacement: methodRec.addr)));


                            //调用前准备(和CALL指令一致)  
                            int rspSub = 0;
                            {
                                // 调用者保存寄存器（易失性寄存器需Caller保存） (选择性保存)
                                var placeholderSave = X64.placehold("caller_save");
                                instructions.AddLast(placeholderSave);
                                callerSavePlaceHolders.Add(placeholderSave);


                                // 栈帧16字节对齐 (如果是奇数个参数 -> 需要8字节对齐栈指针)
                                // (寄存器保存区自己确保16字节对齐)
                                if(tacInf.CALL_paramCount % 2 != 0)
                                {
                                    // 如果是奇数个参数，先将rsp对齐到16字节
                                    rspSub += 8;
                                }
                                // 其他栈参数空间
                                if(tacInf.CALL_paramCount > 4)
                                {
                                    int onStackParamLen = (tacInf.CALL_paramCount - 4) * 8;
                                    rspSub += onStackParamLen;
                                }
                                // 影子空间(32字节)
                                rspSub += 32;

                                //移动rsp  
                                instructions.AddLast(X64.sub(X64.rsp, X64.imm(rspSub)));

                                // 参数赋值(IR中PARAM指令已经是倒序)  
                                foreach(var paraminfo in tempParamList)
                                {
                                    var srcOp = ParseOperand(paraminfo.oprand0);
                                    bool isRefOfConst = paraminfo.oprand0.IsRefOfConst();

                                    //寄存器传参  
                                    if(paraminfo.PARAM_paramidx < 4)
                                    {
                                        var isSse = paraminfo.oprand0.IsSSEType();
                                        var ssereg = UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, true);
                                        var intreg = UtilsW64.GetParamReg(paraminfo.PARAM_paramidx, false);
                                        //浮点参数  
                                        if(isSse)
                                        {
                                            if(paraminfo.oprand0.typeExpr.Size == 4)
                                                instructions.AddLast(X64.movss(ssereg, srcOp));
                                            else
                                                instructions.AddLast(X64.movsd(ssereg, srcOp));

                                            //浮点参数也需要在整数寄存器rcx,rdx,r8,r9中，以支持可变参数  
                                            instructions.AddLast(X64.mov(intreg, srcOp));
                                        }
                                        //整型参数  
                                        else
                                        {
                                            if(isRefOfConst)
                                            {
                                                instructions.AddLast(X64.lea(intreg, X64.rel(paraminfo.oprand0.expr.roDataKey)));
                                            }
                                            else
                                            {
                                                instructions.AddLast(X64.mov(intreg, srcOp));

                                            }
                                        }
                                    }

                                    //栈帧传参[rsp + 8]开始    （影子空间也需要赋值）
                                    int offset = (8 * (paraminfo.PARAM_paramidx));

                                    if(isRefOfConst)
                                    {
                                        instructions.AddLast(X64.lea(X64.r11, X64.rel(paraminfo.oprand0.expr.roDataKey)));
                                        EmitMov(X64.mem(X64.rsp, displacement: offset), X64.r11, paraminfo.oprand0.typeExpr);
                                    }
                                    else
                                    {
                                        EmitMov(X64.mem(X64.rsp, displacement: offset), srcOp, paraminfo.oprand0.typeExpr);
                                    }
                                }
                                tempParamList.Clear();
                            }


                            //调用
                            instructions.AddLast(X64.call(X64.rax));


                            //调用后处理
                            {
                                // 调用后完整恢复栈
                                instructions.AddLast(X64.add(X64.rsp, X64.imm(rspSub)));

                                //还原保存的寄存器(占位)  
                                var placeholderRestore = X64.placehold("caller_restore");
                                instructions.AddLast(placeholderRestore);
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
                                throw new GizboxException(ExceptioName.CodeGen, $"未找到类型定义: {typeName}");
                            }

                            // 获取类的大小
                            long objectSize = classRec.size;

                            // 调用malloc分配堆内存（参数是字节数）
                            instructions.AddLast(X64.mov(X64.rcx, X64.imm(objectSize)));
                            instructions.AddLast(X64.call("malloc"));

                            // 分配的内存地址存储到目标变量  
                            var targetVar = ParseOperand(tacInf.oprand0);
                            instructions.AddLast(X64.mov(targetVar, X64.rax));

                            // 将虚函数表地址写入对象的前8字节
                            instructions.AddLast(X64.mov(X64.rdx, X64.rel(vtableRoKeys[typeName])));
                            instructions.AddLast(X64.mov(X64.mem(X64.rax, displacement: 0), X64.rdx));
                        }
                        break; 
                    case "DEL":
                        {
                            var objPtr = ParseOperand(tacInf.oprand0);
                            instructions.AddLast(X64.mov(X64.rcx, objPtr));
                            instructions.AddLast(X64.call("free"));
                        }
                        break;
                    case "ALLOC_ARRAY":
                        {
                            var target = ParseOperand(tacInf.oprand0);
                            var lenOp = ParseOperand(tacInf.oprand1);
                            var arrType = tacInf.oprand0.typeExpr;
                            var elemType = arrType.ArrayElementType;
                            int elemSize = elemType.Size;

                            instructions.AddLast(X64.mov(X64.rax, lenOp));//RAX 作为中间寄存器
                            instructions.AddLast(X64.mul(X64.imm(elemSize)));
                            instructions.AddLast(X64.mov(X64.rcx, X64.rax));

                            //动态分配  
                            instructions.AddLast(X64.call("malloc"));
                            //返回指针在RAX，写入目标变量  
                            instructions.AddLast(X64.mov(target, X64.rax));
                        }
                        break;
                    case "IF_FALSE_JUMP":
                        {
                            var cond = ParseOperand(tacInf.oprand0);
                            instructions.AddLast(X64.test(cond, cond));
                            instructions.AddLast(X64.jz(tacInf.oprand1.segments[0]));//jump if zero
                        }
                        break;
                    case "=":
                        {
                            var dst = ParseOperand(tacInf.oprand0);

                            if(tacInf.oprand1.IsRefOfConst())
                            {
                                var key = tacInf.oprand1.expr.roDataKey;
                                instructions.AddLast(X64.lea(X64.r11, X64.rel(key)));
                                EmitMov(dst, X64.r11, tacInf.oprand0.typeExpr);
                            }
                            else
                            {
                                var src = ParseOperand(tacInf.oprand1);
                                EmitMov(dst, src, tacInf.oprand0.typeExpr);
                            }
                        }
                        break;
                    case "+=":
                    case "-=":
                    case "*=":
                    case "/=":
                    case "%=":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand1);
                            EmitCompoundAssign(dst, src, tacInf.oprand0.typeExpr, tac.op);
                        }
                        break;
                    case "+":
                    case "-":
                    case "*":
                    case "/":
                    case "%":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var a = ParseOperand(tacInf.oprand1);
                            var b = ParseOperand(tacInf.oprand2);
                            EmitBiOp(dst, a, b, tacInf.oprand0.typeExpr, tac.op);
                        }
                        break;
                    case "NEG":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand1);
                            if(!tacInf.oprand0.IsSSEType())
                            {
                                instructions.AddLast(X64.mov(dst, src));
                                instructions.AddLast(X64.neg(dst));
                            }
                        }
                        break;
                    case "!":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand1);
                            instructions.AddLast(X64.mov(dst, src));
                            instructions.AddLast(X64.test(dst, dst));
                            instructions.AddLast(X64.sete(dst));
                        }
                        break;
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                    case "==":
                    case "!=":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var a = ParseOperand(tacInf.oprand1);
                            var b = ParseOperand(tacInf.oprand2);
                            instructions.AddLast(X64.mov(dst, a));
                            instructions.AddLast(X64.cmp(dst, b));
                            switch(tac.op)
                            {
                                case "<": instructions.AddLast(X64.setl(dst)); break;
                                case "<=": instructions.AddLast(X64.setle(dst)); break;
                                case ">": instructions.AddLast(X64.setg(dst)); break;
                                case ">=": instructions.AddLast(X64.setge(dst)); break;
                                case "==": instructions.AddLast(X64.sete(dst)); break;
                                case "!=": instructions.AddLast(X64.setne(dst)); break;
                            }
                        }
                        break;
                    case "++":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            if(!tacInf.oprand0.IsSSEType()) instructions.AddLast(X64.inc(dst));
                        }
                        break;
                    case "--":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            if(!tacInf.oprand0.IsSSEType()) instructions.AddLast(X64.dec(dst));
                        }
                        break;
                    case "CAST"://CAST [tmp15] Human [tmp14]   tmp15 = (Human)tmp14
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand2);

                            var targetType = GType.Parse(tacInf.tac.arg1);
                            var srcType = tacInf.oprand2.typeExpr;

                            //相同类型
                            if(srcType.ToString() == targetType.ToString())
                            {
                                if(dst != src)
                                    instructions.AddLast(X64.mov(dst, src));
                                break;
                            }

                            //指针类型转换 -> 指针直接赋值  
                            if(srcType.IsPointerType && targetType.IsPointerType)
                            {
                                instructions.AddLast(X64.mov(dst, src));
                                break;
                            }

                            //浮点数 -> 浮点数
                            if(srcType.IsSSE && targetType.IsSSE)
                            {
                                if(srcType.Size == 4 && targetType.Size == 8)
                                    instructions.AddLast(X64.cvtss2sd(dst, src));
                                else if(srcType.Size == 8 && targetType.Size == 4)
                                    instructions.AddLast(X64.cvtsd2ss(dst, src));

                                break;
                            }

                            //整数->整数  
                            if(srcType.IsInteger && targetType.IsInteger)
                            {
                                if(srcType.Size == targetType.Size)
                                {
                                    instructions.AddLast(X64.mov(dst, src));
                                }
                                else if(srcType.Size < targetType.Size)//扩展
                                {
                                    
                                    if(srcType.IsSigned)
                                        instructions.AddLast(X64.movsx(dst, src));
                                    else
                                        instructions.AddLast(X64.movzx(dst, src));
                                }
                                else //截断
                                {
                                    instructions.AddLast(X64.mov(dst, src));
                                }
                                break;
                            }

                            //整数->浮点数
                            if(srcType.IsInteger && targetType.IsSSE)
                            {
                                if(targetType.Size == 4)
                                    instructions.AddLast(X64.cvtsi2ss(dst, src));
                                else 
                                    instructions.AddLast(X64.cvtsi2sd(dst, src));
                                break;
                            }
                            // 浮点 -> 整数 (截断)
                            if(srcType.IsSSE && targetType.IsInteger)
                            {
                                if(srcType.Size == 4) 
                                {
                                    if(targetType.Size == 8)
                                        instructions.AddLast(X64.cvttss2siq(dst, src));
                                    else
                                        instructions.AddLast(X64.cvttss2si(dst, src));
                                }
                                else
                                {
                                    if(targetType.Size == 8)
                                        instructions.AddLast(X64.cvttsd2siq(dst, src));
                                    else
                                        instructions.AddLast(X64.cvttsd2si(dst, src));
                                }
                                break;
                            }

                            //整数 -> 字符串  
                            //浮点数 -< 字符串


                            throw new GizboxException(ExceptioName.CodeGen, $"cast not support:{srcType.ToString()}->{targetType.ToString()}");
                        }
                        break;
                    default:
                        break;
                }


                //物理寄存器占用  
                foreach(var regEnum in X64.physRegisterUseTemp)
                {
                    tacInf.physRegistersUse.Add(regEnum);
                }
                X64.physRegisterUseTemp.Clear();

                //记录指令起止  
                if(lastInstruction?.Next != null)
                {
                    tacInf.startX64Node = lastInstruction;
                    tacInf.endX64Node = instructions.Last;
                }
                else
                {
                    tacInf.startX64Node = null;
                    tacInf.endX64Node = null;
                }

                //当前IR有标签
                if(string.IsNullOrEmpty(tac.label) == false)
                {
                    if(lastInstruction.Next == null)
                        throw new GizboxException(ExceptioName.CodeGen, "Internal error: no instruction for label!");

                    lastInstruction.Next.value.label = tac.label;
                }

                //添加注释
                if(lastInstruction != null)
                {
                    if(lastInstruction.Next != null)
                    {
                        lastInstruction.Next.value.comment += $"// {tacInf.line} : {tac.ToExpression(showlabel: false, indent: false)}";
                    }
                    else
                    {
                        lastInstruction.value.comment += $"//  + : {tacInf.line} : {tac.ToExpression(showlabel: false, indent: false)}";
                    }
                }
                    

                //记录
                lastInstruction = instructions.Last;
            }
        }

        // <summary> 寄存器分配 </summary>
        private void Pass4()
        {
            if(debugLogAsmInfos)
            {
                Print();
            }

            List<FunctionAdditionInfo> funcInfos = new();
            FunctionAdditionInfo currFunc = null;
            for(int i = 0; i < ir.codes.Count; ++i)
            {
                var tac = ir.codes[i];
                var tacInf = GetTacInfo(tac);
                if(tacInf.tac.op == "FUNC_BEGIN")
                {
                    currFunc = new ();
                    currFunc.funcRec = funcDict[tac.arg0];
                    currFunc.irLineStart = i - 1;
                }

                if(tacInf.tac.op == "FUNC_END")
                {
                    currFunc.irLineEnd = i + 1;
                    funcInfos.Add(currFunc);
                    currFunc = null;
                }
            }

            foreach(var func in funcInfos)
            {
                RegInterfGraph gpGraph = new();
                RegInterfGraph sseGraph = new();

                List<(RegInterfGraph.Node node, int start, int end)> nodeRanges = new();
                List<(RegInterfGraph.Node node, int start, int end)> sseNodeRanges = new();

                //预着色节点  
                for(int l = func.irLineStart; l < func.irLineEnd; ++l)
                {
                    var tac = ir.codes[l];
                    var tacInf = GetTacInfo(tac);
                    foreach(var reg in tacInf.physRegistersUse)
                    {
                        if(reg >= RegisterEnum.XMM0)
                        {
                            var precoloredNode = sseGraph.AddPrecoloredNode(reg);
                            sseNodeRanges.Add((precoloredNode, l, l));
                        }
                        else
                        {
                            var precoloredNode = gpGraph.AddPrecoloredNode(reg);
                            nodeRanges.Add((precoloredNode, l, l));
                        }
                    }
                }
                //变量节点
                foreach(var (_, rec) in func.funcRec.envPtr.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Param && rec.category != SymbolTable.RecordCatagory.Variable)
                        continue;

                    var type = GType.Parse(rec.typeExpression);
                    if(type.IsSSE)
                    {
                        var newNode = sseGraph.AddVarNode(rec);
                        foreach(var range in cfg.varialbeLiveInfos[rec].mergedRanges)
                        {
                            sseNodeRanges.Add((newNode, range.start, range.die - 1));
                        }
                    }
                    else
                    {
                        var newNode = gpGraph.AddVarNode(rec);
                        foreach(var range in cfg.varialbeLiveInfos[rec].mergedRanges)
                        {
                            nodeRanges.Add((newNode, range.start, range.die - 1));
                        }
                    }
                }

                //构建冲突边  
                for(int i = 0; i < nodeRanges.Count; i++)
                {
                    for(int j = i + 1; j < nodeRanges.Count; j++)
                    {
                        if(nodeRanges[i].node == nodeRanges[j].node)
                            continue;

                        //相交  
                        if(nodeRanges[i].end >= nodeRanges[j].start && nodeRanges[j].end >= nodeRanges[i].start)
                        {
                            nodeRanges[i].node.AddEdge(nodeRanges[j].node);
                        }
                    }
                }


                //尝试着色（目前只用 callee-save（非易失）寄存器）    
                //使用 callee-save 只需在函数序言/尾声一次性保存/恢复“实际用到的非易失寄存器”，对调用点零改动，最简单直接。
                bool success = gpGraph.TryColoring(X64RegisterUtility.GPCalleeSaveRegs.Where(r => r != RegisterEnum.RBP).ToArray());
                bool successSSE = sseGraph.TryColoring(X64RegisterUtility.XMMCalleeSaveRegs);

                //需要溢出->重写图着色的迭代，直到着色成功  
                //......

                //寄存器分配后的统一重写阶段  
                {
                    //统计分配的结果
                    var gpAssign = new Dictionary<SymbolTable.Record, RegisterEnum>();
                    var sseAssign = new Dictionary<SymbolTable.Record, RegisterEnum>();
                    foreach(var n in gpGraph.allNodes)
                    {
                        if(n.variable != null && n.assignedColor != RegisterEnum.Undefined)
                            gpAssign[n.variable] = n.assignedColor;
                    }
                    foreach(var n in sseGraph.allNodes)
                    {
                        if(n.variable != null && n.assignedColor != RegisterEnum.Undefined)
                            sseAssign[n.variable] = n.assignedColor;
                    }

                    //统计本函数实际用到的寄存器集合  
                    var usedGpRegs = new HashSet<RegisterEnum>(gpAssign.Values);
                    var usedXmmRegs = new HashSet<RegisterEnum>(sseAssign.Values);

                    var funcUsedCalleeGP = new HashSet<RegisterEnum>(usedGpRegs.Where(r => X64RegisterUtility.GPCalleeSaveRegs.Contains(r) && r != RegisterEnum.RBP));
                    var funcUsedCalleeXMM = new HashSet<RegisterEnum>(usedXmmRegs.Where(r => X64RegisterUtility.XMMCalleeSaveRegs.Contains(r)));

                    var funcUsedCallerGP = new HashSet<RegisterEnum>(usedGpRegs.Where(r => X64RegisterUtility.GPCallerSaveRegs.Contains(r)));
                    var funcUsedCallerXMM = new HashSet<RegisterEnum>(usedXmmRegs.Where(r => X64RegisterUtility.XMMCallerSaveRegs.Contains(r)));

                    //占位展开  
                    //todo

                    //替换虚拟寄存器，尝试溢出
                    //todo
                }
            }
        }


        #region PASS0

        private void GenClassLayoutInfo(SymbolTable.Record classRec)
        {
            var classEnv = classRec.envPtr;
            Win64Target.Log("---------" + classEnv.name + "----------");
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
            foreach(var (memName, memRec) in classTable.records)
            {
                if(memRec.category != SymbolTable.RecordCatagory.Function)
                    continue;

                GenFuncInfo(memRec);
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
                section_rdata.Add(rokey, vtableData.Select(v => (GType.Parse("FuncPtr"), v.name)).ToList());
                vtableRoKeys.Add(classRec.name, rokey);
            }
        }

        private void GenFuncInfo(SymbolTable.Record funcRec)
        {
            var funcTable = funcRec.envPtr;


            if(funcTable == null)
                throw new GizboxException(ExceptioName.CodeGen, $"null func table of {(funcRec?.name ?? "?")}.");

            //添加进函数表    
            funcDict.Add(funcTable.name, funcRec);
            //参数偏移  
            {
                foreach(var (key, rec) in funcTable.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Param)
                        continue;
                    rec.addr = rec.index * 8;
                }
            }
            //（局部变量内存偏移，需要等活跃变量分析之后）
        }

        private (GType type, string valExpr) GetStaticInitValue(SymbolTable.Record rec)
        {
            if(rec.category == SymbolTable.RecordCatagory.Variable)
            {
                var type = GType.Parse(rec.typeExpression);
                if(string.IsNullOrEmpty(rec.initValue))
                {
                    switch(type.Category)
                    {
                        case GType.Kind.Bool:
                            return (GType.Parse("bool"), "false");
                        case GType.Kind.Char:
                            return (GType.Parse("char"), "\0");
                        case GType.Kind.Int:
                            return (GType.Parse("int"), "0");
                        case GType.Kind.Long:
                            return (GType.Parse("long"), "0");
                        case GType.Kind.Float:
                            return (GType.Parse("int"), "0.0");
                        case GType.Kind.Double:
                            return (GType.Parse("int"), "0.0");
                    }
                }
                else
                {
                    return (GType.Parse(rec.typeExpression), UtilsW64.GLitToW64Lit(GType.Parse(rec.typeExpression), rec.initValue));
                }
            }
            return default;
        }

        #endregion

        #region PASS1

        private void AnalyzeUSEDEF(TAC tac, int lineNum, BasicBlock block)
        {
            var status = ir.scopeStatusArr[lineNum];
            ir.stackDic.TryGetValue(status, out var envStack);
            
            if(envStack == null)
            {
                throw new GizboxException(ExceptioName.CodeGen, $"null env stack at line:{lineNum}!");
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
            var irOperand = ParseIRExpr(operand);

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

            var iroperand = ParseIRExpr(operand);
            switch(iroperand.type)
            {
                case IROperandExpr.Type.LitOrConst:
                    {
                        switch(iroperand.segments[0])
                        {
                            case "%LITFLOAT":
                                {
                                    var key = GetConstSymbol(iroperand.segments[0], iroperand.segments[1]);
                                    iroperand.roDataKey = key;
                                    if(section_rdata.ContainsKey(key) == false)
                                    {
                                        section_rdata[key] = new() { (GType.Parse("float"), iroperand.segments[1]) };
                                    }
                                }
                                break;
                            case "%LITDOUBLE":
                                {
                                    var key = GetConstSymbol(iroperand.segments[0], iroperand.segments[1]);
                                    iroperand.roDataKey = key;
                                    if(section_rdata.ContainsKey(key) == false)
                                    {
                                        section_rdata[key] = new() { (GType.Parse("double"), iroperand.segments[1]) };
                                    }
                                }
                                break;
                            case "%CONSTSTRING":
                                {
                                    var key = GetConstSymbol(iroperand.segments[0], iroperand.segments[1]);
                                    iroperand.roDataKey = key;
                                    if(section_rdata.ContainsKey(key) == false)
                                    {
                                        int cstIdx = int.Parse(iroperand.segments[1]);
                                        section_rdata[key] = new() { (GType.Parse(ir.constData[cstIdx].typeExpress), ir.constData[cstIdx].valueExpr) };
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
                            case "%LITBOOL":
                                {
                                    var value = iroperandExpr.segments[1] == "True" ? 1L : 0L;
                                    return X64.imm(value);
                                }
                                break;
                            case "%LITINT":
                                {
                                    var value = int.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "%LITLONG":
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
                            case "%LITNULL":
                                {
                                    return X64.imm(0);
                                }
                                break;
                            case "%LITFLOAT":
                            case "%LITDOUBLE":
                                {
                                    return X64.rel(iroperandExpr.roDataKey);
                                }
                                break;
                            case "%CONSTSTRING":
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

                        if(varRec == null)
                        {
                            throw new GizboxException(ExceptioName.Undefine, $"null var rec in segment recs:{varName}");
                        }
                        if(varRec.category == SymbolTable.RecordCatagory.Variable)
                        {
                            //全局变量
                            if(varRec.GetAdditionInf().isGlobal)
                            {
                                return X64.rel(varRec.name);
                            }
                            //局部变量  
                            else
                            {
                                return vreg(varRec);
                            }
                            
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
                            throw new GizboxException(ExceptioName.CodeGen, $"object not found for member access \"{objName}.{fieldName}\" at line {irOperand.owner.line}");
                        if(fieldRec == null)
                            throw new GizboxException(ExceptioName.CodeGen, $"field not found for member access \"{objName}.{fieldName}\" at line {irOperand.owner.line}");

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
                            throw new GizboxException(ExceptioName.CodeGen, $"array not found for element access \"{arrayName}[{indexExpr}]\" at line {irOperand.owner.line}");

                        // 元素大小
                        var elemType = GType.Parse(arrayRec.typeExpression);
                        int elemSize = elemType.Size;

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
                            if(indexExpr != null && indexExpr.StartsWith("%LITINT:"))
                            {
                                var lit = indexExpr.Substring("%LITINT:".Length);
                                immIndex = long.Parse(lit);
                                long disp = checked(immIndex * elemSize);
                                return X64.mem(baseV, displacement: disp);
                            }

                            throw new GizboxException(ExceptioName.CodeGen, $"unsupported array index expression \"{indexExpr}\" at line {irOperand.owner.line}");
                        }
                    }
                    break;
            }

            return null;
        }

        public IROperandExpr ParseIRExpr(string rawoperand)
        {
            if(operandCache.TryGetValue(rawoperand, out var result))
            {
                return result;
            }
            else
            {
                IROperandExpr irOperand = new();

                //返回值虚拟寄存器
                if(rawoperand == "%RET")
                {
                    irOperand.type = IROperandExpr.Type.RET;
                    irOperand.segments = new[] { "%RET" };
                }
                //字面量或者常量  
                else if(rawoperand.StartsWith("%CONST") || rawoperand.StartsWith("%LIT"))
                {
                    var parts = rawoperand.Split(':');
                    irOperand.type = IROperandExpr.Type.LitOrConst;
                    irOperand.segments = parts;
                }
                //标签  
                else if(rawoperand.StartsWith("%LABEL:"))
                {
                    irOperand.type = IROperandExpr.Type.Label;
                    irOperand.segments = new[] { rawoperand.Substring(7) };
                }
                //成员访问
                else if(rawoperand.Contains("->"))
                {
                    //成员访问：obj.x
                    var parts = rawoperand.SplitViaStr("->");

                    irOperand.type = IROperandExpr.Type.ClassMemberAccess;
                    irOperand.segments = parts;
                }
                //数组元素访问  
                else if(rawoperand.Contains('[') && rawoperand.EndsWith("]"))
                {
                    //数组访问：arr[x]
                    var parts = rawoperand.Split('[');
                    var arrName = parts[0];
                    var indexExpr = parts[1].Substring(0, parts[1].Length - 1);//去掉']'
                    irOperand.type = IROperandExpr.Type.ArrayElementAccess;
                    irOperand.segments = new[] { arrName, indexExpr };
                }
                //标识符（变量名、函数名、类型名）
                else
                {
                    irOperand.type = IROperandExpr.Type.Identifier;
                    irOperand.segments = new[] { rawoperand };
                }


                //缓存
                operandCache.Add(rawoperand, irOperand);

                return irOperand;
            }
        }

        private string GetConstSymbol(string littype, string litvalue)
        {
            return $"{littype.Substring(1)}_{litvalue.Replace('.', '_')}";
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

        #region PASS3

        private void EmitMov(X64Operand dst, X64Operand src, GType type)
        {
            bool IsRegOrVReg(X64Operand op) => op is X64Reg;

            if(type.IsSSE)
            {
                bool f32 = type.Size == 4;
                if(IsRegOrVReg(dst) == false && IsRegOrVReg(src) == false)
                {
                    if(f32)
                    { instructions.AddLast(X64.movss(X64.xmm0, src)); instructions.AddLast(X64.movss(dst, X64.xmm0)); }
                    else
                    { instructions.AddLast(X64.movsd(X64.xmm0, src)); instructions.AddLast(X64.movsd(dst, X64.xmm0)); }
                }
                else
                {
                    instructions.AddLast(f32 ? X64.movss(dst, src) : X64.movsd(dst, src));
                }
            }
            else
            {
                if(IsRegOrVReg(dst) == false && IsRegOrVReg(src) == false)
                {
                    instructions.AddLast(X64.mov(X64.r11, src));
                    instructions.AddLast(X64.mov(dst, X64.r11));
                }
                else
                {
                    instructions.AddLast(X64.mov(dst, src));
                }
            }
        }

        // 二元运算  
        private void EmitBiOp(X64Operand dst, X64Operand a, X64Operand b, GType type, string op)
        {
            bool IsRegOrVReg(X64Operand op) => op is X64Reg;

            if(type.IsSSE)
            {
                bool f32 = type.Size == 4;

                X64Instruction SseOp(X64Operand d, X64Operand s)
                {
                    switch(op)
                    {
                        case "+":
                            return f32 ? X64.addss(d, s) : X64.addsd(d, s);
                        case "-":
                            return f32 ? X64.subss(d, s) : X64.subsd(d, s);
                        case "*":
                            return f32 ? X64.mulss(d, s) : X64.mulsd(d, s);
                        case "/":
                            return f32 ? X64.divss(d, s) : X64.divsd(d, s);
                        case "%":
                            throw new GizboxException(ExceptioName.CodeGen, "float/double 不支持 %，请改写为运行时函数");
                    }
                    throw new GizboxException(ExceptioName.CodeGen, $"未知操作符: {op}");
                }

                if(IsRegOrVReg(dst))
                {
                    if(Equals(dst, a))
                    {
                        instructions.AddLast(SseOp(dst, b));
                    }
                    else
                    {
                        // dst <- a; dst = dst op b
                        instructions.AddLast(f32 ? X64.movss(dst, a) : X64.movsd(dst, a));
                        instructions.AddLast(SseOp(dst, b));
                    }
                }
                else
                {
                    // dst(内存) = (a op b) via xmm0
                    if(f32)
                        instructions.AddLast(X64.movss(X64.xmm0, a));
                    else
                        instructions.AddLast(X64.movsd(X64.xmm0, a));
                    instructions.AddLast(SseOp(X64.xmm0, b));
                    EmitMov(dst, X64.xmm0, type);
                }
            }
            else
            {
                // 整数/指针
                if(op == "/" || op == "%")
                {
                    // rdx:rax / b
                    instructions.AddLast(X64.mov(X64.rax, a));
                    instructions.AddLast(X64.cqo());
                    instructions.AddLast(X64.idiv(b));
                    EmitMov(dst, op == "/" ? (X64Operand)X64.rax : X64.rdx, type);
                    return;
                }

                X64Instruction IntOp(string o, X64Operand d, X64Operand s)
                {
                    switch(o)
                    {
                        case "+":
                            return X64.add(d, s);
                        case "-":
                            return X64.sub(d, s);
                        case "*":
                            return X64.imul(d, s);
                    }
                    throw new GizboxException(ExceptioName.CodeGen, $"未知操作符: {o}");
                }

                if(IsRegOrVReg(dst))
                {
                    if(Equals(dst, a))
                    {
                        instructions.AddLast(IntOp(op, dst, b));
                    }
                    else
                    {
                        // dst <- a; dst = dst op b
                        instructions.AddLast(X64.mov(dst, a));
                        instructions.AddLast(IntOp(op, dst, b));
                    }
                }
                else
                {
                    // dst(内存) = (a op b) via r11
                    instructions.AddLast(X64.mov(X64.r11, a));
                    instructions.AddLast(IntOp(op, X64.r11, b));
                    EmitMov(dst, X64.r11, type);
                }
            }
        }

        // 复合赋值
        private void EmitCompoundAssign(X64Operand dst, X64Operand rhs, GType type, string op)
        {
            if(type.IsSSE)
            {
                bool f32 = type.Size == 4;

                if(f32)
                    instructions.AddLast(X64.movss(X64.xmm0, dst));
                else
                    instructions.AddLast(X64.movsd(X64.xmm0, dst));

                switch(op)
                {
                    case "+=":
                        if(f32)
                            instructions.AddLast(X64.addss(X64.xmm0, rhs));
                        else
                            instructions.AddLast(X64.addsd(X64.xmm0, rhs));
                        break;
                    case "-=":
                        if(f32)
                            instructions.AddLast(X64.subss(X64.xmm0, rhs));
                        else
                            instructions.AddLast(X64.subsd(X64.xmm0, rhs));
                        break;
                    case "*=":
                        if(f32)
                            instructions.AddLast(X64.mulss(X64.xmm0, rhs));
                        else
                            instructions.AddLast(X64.mulsd(X64.xmm0, rhs));
                        break;
                    case "/=":
                        if(f32)
                            instructions.AddLast(X64.divss(X64.xmm0, rhs));
                        else
                            instructions.AddLast(X64.divsd(X64.xmm0, rhs));
                        break;
                    case "%=":
                        throw new GizboxException(ExceptioName.CodeGen, "浮点数不支持%");
                    default:
                        throw new GizboxException(ExceptioName.CodeGen, $"未知复合操作符: {op}");
                }
                EmitMov(dst, X64.xmm0, type);
            }
            else
            {
                switch(op)
                {
                    case "+=":
                    case "-=":
                    case "*=":
                        {
                            instructions.AddLast(X64.mov(X64.r11, dst));
                            if(op == "+=")
                                instructions.AddLast(X64.add(X64.r11, rhs));
                            else if(op == "-=")
                                instructions.AddLast(X64.sub(X64.r11, rhs));
                            else
                                instructions.AddLast(X64.imul(X64.r11, rhs));
                            EmitMov(dst, X64.r11, type);
                            break;
                        }
                    case "/=":
                        {
                            instructions.AddLast(X64.mov(X64.rax, dst));
                            instructions.AddLast(X64.cqo());
                            instructions.AddLast(X64.idiv(rhs));
                            EmitMov(dst, X64.rax, type);
                            break;
                        }
                    case "%=":
                        {
                            instructions.AddLast(X64.mov(X64.rax, dst));
                            instructions.AddLast(X64.cqo());
                            instructions.AddLast(X64.idiv(rhs));
                            EmitMov(dst, X64.rdx, type);
                            break;
                        }
                    default:
                        throw new GizboxException(ExceptioName.CodeGen, $"未知复合操作符: {op}");
                }
            }
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


        public (SymbolTable.Record, SymbolTable.Record) QueryClassAndMember(string objDefineType, string memberName)
        {
            if(classDict.Count == 0 || classDict.ContainsKey(objDefineType) == false)
                return (null, null);
            return (classDict[objDefineType], QueryMember(classDict[objDefineType], memberName));

        }
        public SymbolTable.Record QueryMember(string objDefineType, string memberName)
        {
            if(classDict.Count == 0)
                return null;
            return QueryMember(classDict[objDefineType], memberName);
        }
        private SymbolTable.Record QueryMember(SymbolTable.Record classRec, string memberName)
        {
            return classRec.envPtr.GetRecord(memberName);
        }

        public SymbolTable.Record Query(string name, int line)
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
                throw new GizboxException(ExceptioName.CodeGen, "envStack is null!");
            
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


        private void Print()
        {
            StringBuilder strb = new StringBuilder();

            //global and extern  
            strb.AppendLine("\n");
            foreach(var g in globalVarInfos)
            {
                strb.AppendLine($"global  {g.Key}");
            }
            foreach(var g in globalFuncsInfos)
            {
                if(g.Key == "Main")
                    continue;
                strb.AppendLine($"global  {g.Key}");
            }
            foreach(var g in externVars)
            {
                strb.AppendLine($"extern  {g.Key}");
            }
            foreach(var g in externFuncs)
            {
                if(g.Key == "Main")
                    continue;
                strb.AppendLine($"extern  {g.Key}");
            }

            //.data
            strb.AppendLine("\n");
            strb.AppendLine("section .rdata");
            foreach(var rodata in section_rdata)
            {
                if(rodata.Value.Count == 1)
                {
                    var (typeExpr, valExpr) = rodata.Value[0];
                    strb.AppendLine($"\t{rodata.Key}  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{rodata.Key}:");
                    foreach(var (typeExpr, valExpr) in rodata.Value)
                    {
                        strb.AppendLine($"\t\t  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                    }
                }
            }
            strb.AppendLine("\n");
            strb.AppendLine("section .data");
            foreach(var data in section_data)
            {
                if(data.Value.Count == 1)
                {
                    var (typeExpr, valExpr) = data.Value[0];
                    strb.AppendLine($"\t{data.Key}  {UtilsW64.GetX64DefineType(typeExpr, valExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{data.Key}:");
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
                    strb.AppendLine($"\t{bss.Key}  {UtilsW64.GetX64ReserveDefineType(typeExpr)}");
                }
                else
                {
                    strb.AppendLine($"\t{bss.Key}:");
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
                strb.Append(UtilityX64.SerializeInstruction(instruction));
            }
            Win64Target.Log(strb.ToString());
        }
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


            //是否有外部函数  
            if(tac.op == "CALL" ) //todo：MCALL通过虚函数表中函数指针调用，是否不需要导入成员函数？  
            {
                var funcRec = oprand0.segmentRecs[0];
                if(funcRec.category != SymbolTable.RecordCatagory.Function)
                    throw new GizboxException(ExceptioName.Undefine, "func rec invalid.");
                
                if(funcRec.GetAdditionInf().table != context.ir.globalScope.env)
                {
                    context.externFuncs.Add(funcRec.name, funcRec);
                }
            }
            //是否有外部变量  
            foreach(var operand in allOprands)
            {
                foreach(var rec in operand.segmentRecs)
                {
                    if(rec == null)
                        continue;
                    if(rec.GetAdditionInf().isGlobal 
                        && rec.category == SymbolTable.RecordCatagory.Variable 
                        && rec.GetAdditionInf().table != context.ir.globalScope.env)
                    {
                        context.externVars.Add(rec.name, rec);
                    }
                }
            }
        }


        #region X64指令相关
        public LList<X64Instruction>.Node startX64Node;
        public LList<X64Instruction>.Node endX64Node;
        public HashSet<RegisterEnum> physRegistersUse = new();//特定指令使用到的寄存器 -> 作为预着色节点
        #endregion
    }

    public class IROperand
    {
        ///操作数实例（和tac中的操作数一一对应）
        public TACInfo owner;
        public int operandIdx;
        public IROperandExpr expr;

        public string[] segments => expr.segments;//子操作数

        public GType typeExpr;//类型  
        public SymbolTable.Record[] segmentRecs;//变量的符号表条目（如果子操作数是变量）  
        public SymbolTable.Record RET_functionRec;//返回值的函数符号表条目（如果是RET操作数）

        public IROperand(Win64CodeGenContext context, TACInfo tacinf, int operandIdx, string rawoperand)
        {
            this.owner = tacinf;
            this.expr = context.ParseIRExpr(rawoperand);
            this.operandIdx = operandIdx;
            this.segmentRecs = new SymbolTable.Record[segments.Length];
            if(expr.type == IROperandExpr.Type.Identifier)//Function Or Variable
            {
                SymbolTable.Record rec;

                if(segments[0].Contains("."))
                {
                    if(segments[0].EndsWith(".ctor"))
                    {
                        string className = segments[0].Substring(0, segments[0].Length - 5);
                        var classRec = context.Query(className, owner.line);
                        segmentRecs[0] = null;
                        typeExpr = Utils.CtorType(classRec);
                    }
                    else
                    {
                        string[] parts = segments[0].Split('.');
                        string className = parts[0];
                        string memberName = parts[1];
                        var (classRec, memberRec) = context.QueryClassAndMember(className, memberName);
                        segmentRecs[0] = memberRec;
                        typeExpr = GType.Parse(memberRec.typeExpression);
                        Win64Target.Log("------- " + typeExpr.ToString());
                    }
                }
                else
                {
                    //Call指令中标识符作为函数名  
                    if(owner.tac.op == "MCALL" && operandIdx == 0)
                    {
                        var objClass = owner.MCALL_methodTargetObject.typeExpr;
                        rec = context.QueryMember(objClass.ToString(), segments[0]);
                        segmentRecs[0] = rec;
                        typeExpr = GType.Parse(rec.typeExpression);
                    }
                    else if(owner.tac.op == "CALL" && operandIdx == 0)
                    {
                        rec = context.Query(segments[0], owner.line);
                        segmentRecs[0] = rec;
                        typeExpr = GType.Parse(rec.typeExpression);
                    }
                    //CAST指令中标识符作为类型名
                    else if(owner.tac.op == "CAST" && this.operandIdx == 1)
                    {
                        typeExpr = GType.Parse("(type)");
                    }
                    else
                    {
                        rec = context.Query(segments[0], tacinf.line);
                        if(rec == null)
                            throw new GizboxException(ExceptioName.CodeGen, $"cannot find variable {segments[0]} at line {tacinf.line}");
                        segmentRecs[0] = rec;
                        typeExpr = GType.Parse(rec.typeExpression);
                    }
                }
            }
            else if(expr.type == IROperandExpr.Type.ClassMemberAccess)
            {
                if(segments[1] != "ctor")
                {
                    var objRec = context.Query(segments[0], tacinf.line);
                    string className;
                    if(objRec.category == SymbolTable.RecordCatagory.Class)//静态函数
                    {
                        className = objRec.name;
                    }
                    else
                    {
                        className = objRec.typeExpression;
                    }
                    var classRec = context.classDict[className];
                    var memberRec = context.QueryMember(className, segments[1]);
                    segmentRecs[0] = objRec;
                    segmentRecs[1] = memberRec;
                    typeExpr = GType.Parse(memberRec.typeExpression);
                }
                else
                {
                    var objRec = context.Query(segments[0], tacinf.line);
                    segmentRecs[0] = objRec;
                    segmentRecs[1] = null;
                    typeExpr = GType.Parse(objRec.name);
                }
            }
            else if(expr.type == IROperandExpr.Type.ArrayElementAccess)
            {
                var arrayRec = context.Query(segments[0], tacinf.line);
                var indexRec = context.Query(segments[1], tacinf.line);
                segmentRecs[0] = arrayRec;
                segmentRecs[1] = indexRec;
                typeExpr = GType.Parse(arrayRec.typeExpression).ArrayElementType;
            }
            else if(expr.type == IROperandExpr.Type.RET)
            {
                var call = context.Lookback(this.owner.line, "CALL");
                var mcall = context.Lookback(this.owner.line, "MCALL");

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
                    throw new GizboxException(ExceptioName.CodeGen, $"no CALL or MCALL before RET at line {this.owner.line}");
                }

                this.RET_functionRec = lastCall.oprand0.segmentRecs[0];

                if(this.RET_functionRec == null)
                {
                    throw new GizboxException(ExceptioName.CodeGen, $"no function record for RET at line {this.owner.line}");
                }

                typeExpr = GType.Parse(RET_functionRec.typeExpression).FunctionReturnType;
            }
            else if(expr.type == IROperandExpr.Type.LitOrConst)
            {
                typeExpr = GType.Parse(UtilsW64.GetLitConstType(segments[0]));
            }
            else if(expr.type == IROperandExpr.Type.Label)
            {
                typeExpr = GType.Parse("(label)");
            }
        }

        /// <summary> SSE类型 </summary>
        public bool IsSSEType()
        {
            return typeExpr.IsSSE;
        }

        /// <summary> 是引用类型常量（目前只有字符串常量） </summary>
        public bool IsRefOfConst()
        {
            return this?.expr?.type == IROperandExpr.Type.LitOrConst
                && this.expr.segments?.Length > 0
                && this.expr.segments[0] == "%CONSTSTRING";
        }
    }


    public class RecAdditionInfo
    {
        private SymbolTable.Record target;

        public SymbolTable table;

        public bool isGlobal = false;
    }
    public class FunctionAdditionInfo
    {
        public SymbolTable.Record funcRec;
        public int irLineStart;
        public int irLineEnd;
    }



    public class X64FunctionDesc
    {
        public string name;
        public LList<X64Instruction>.Node start;
        public LList<X64Instruction>.Node end;
    }
    public class UtilsW64
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

        public static string GetLitConstType(string litconstMark)
        {
            switch(litconstMark)
            {
                case "%LITINT":
                    return "int";
                case "%LITLONG":
                    return "long";
                case "%LITFLOAT":
                    return "float";
                case "%LITDOUBLE":
                    return "double";
                case "%LITBOOL":
                    return "bool";
                case "%LITCHAR":
                    return "char";
                case "%CONSTSTRING":
                    return "string";
                default:
                    return litconstMark;
            }

        }

        public static string GLitToW64Lit(GType gType, string gLiterial)
        {
            switch(gType.Category)
            {
                case GType.Kind.Bool:
                    return bool.Parse(gLiterial) ? "1" : "0";
                case GType.Kind.Char:
                    return gLiterial;
                case GType.Kind.Int:
                    {
                        return gLiterial;
                    }
                case GType.Kind.Long:
                    {
                        if(gLiterial.EndsWith("L") || gLiterial.EndsWith("l"))
                            gLiterial = gLiterial.Substring(0, gLiterial.Length - 1);
                        return gLiterial;
                    }
                case GType.Kind.Float:
                    {
                        if(gLiterial.EndsWith("F") || gLiterial.EndsWith("f"))
                            gLiterial = gLiterial.Substring(0, gLiterial.Length - 1);
                        return gLiterial;
                    }
                case GType.Kind.Double:
                    {
                        if(gLiterial.EndsWith("D") || gLiterial.EndsWith("d"))
                            gLiterial = gLiterial.Substring(0, gLiterial.Length - 1);
                        return gLiterial;
                    }
                case GType.Kind.String:
                    {
                        return $"\"{gLiterial}\"";
                    }
                default:
                    throw new GizboxException(ExceptioName.Undefine, $"not support lit type:{gType.ToString()}");
                    
            }
            return "";
        }

        public static string GetX64DefineType(GType gtype, string val)
        {
            string x64DefineType = string.Empty;
            switch(gtype.Category)
            {
                case GType.Kind.Void:
                case GType.Kind.Bool:
                case GType.Kind.String://字符串常量  
                    x64DefineType = "db";
                    break;
                case GType.Kind.Char:
                    x64DefineType = "dw";
                    break;
                case GType.Kind.Int:
                case GType.Kind.Float:
                    x64DefineType = "dd";
                    break;
                case GType.Kind.Long:
                case GType.Kind.Double:
                    x64DefineType = "dq";
                    break;
                case GType.Kind.Array:
                default:
                    x64DefineType = "dq";//引用类型指针  
                    break;
            }

            string x64ValExpr = GLitToW64Lit(gtype, val);

            return $"{x64DefineType}  {x64ValExpr}";
        }

        public static string GetX64ReserveDefineType(GType gtype)
        {
            switch(gtype.Category)
            {
                case GType.Kind.Void:
                case GType.Kind.Bool:
                    return "resb  1";
                case GType.Kind.Char://字符串常量  
                    return "resw 1";
                case GType.Kind.Int:
                case GType.Kind.Float:
                    return "resd 1";
                case GType.Kind.Long:
                case GType.Kind.Double:
                    return "resq 1";
                case GType.Kind.Array://引用类型
                case GType.Kind.Object://引用类型
                case GType.Kind.String://非常量字符串
                    return "resq 1";//指针
                default:
                    throw new GizboxException(ExceptioName.Undefine, $"not support .bss type:{gtype.ToString()}");
                    break;
            }
        }

        public static X64Reg GetParamReg(int paramIdx, bool isSSE)
        {
            if(isSSE == false)
            {
                switch(paramIdx)
                {
                    case 0:
                        return X64.rcx;
                    case 1:
                        return X64.rdx;
                    case 2:
                        return X64.r8;
                    case 3:
                        return X64.r9;
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

            throw new GizboxException(ExceptioName.CodeGen, $"param cannot pass by register. idx:{paramIdx}");
        }
    }
}
