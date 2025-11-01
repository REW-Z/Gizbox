using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Linq;

using Gizbox;
using Gizbox.IR;

using InstructionNode = Gizbox.LList<Gizbox.Src.Backend.X64Instruction>.Node;
using SymbolRecord = Gizbox.SymbolTable.Record;


namespace Gizbox.Src.Backend
{
    public static class Win64Target
    {
        public static string[] GenAsms(Compiler compiler, IRUnit mainUnit, string outputDir, Gizbox.CompileOptions options)
        {
            //对所有编译单元进行汇编代码生成
            mainUnit.AutoLoadDependencies(compiler, true);
            HashSet<IRUnit> allunit = new();
            CollectIRUnits(allunit, mainUnit);
            string[] allpath = new string[allunit.Count];
            int currIdx = 0;
            foreach(var unit in allunit)
            {
                string name;
                bool isMainUnit;
                if(unit == mainUnit)
                {
                    name = "main";
                    isMainUnit = true;
                }
                else
                {
                    name = unit.name;
                    isMainUnit = false;
                }
                
                allpath[currIdx] = GenSingleAsm(compiler, unit, isMainUnit, name, options, outputDir);
                currIdx++;
            }
            GixConsole.WriteLine($"已生成{allunit.Count}个单元");
            return allpath;
        }

        private static void CollectIRUnits(HashSet<IRUnit> units, IRUnit ir)
        {
            if(ir.dependencyLibs != null)
            {
                foreach(var dep in ir.dependencyLibs)
                {
                    CollectIRUnits(units, dep);
                }
            }
            units.Add(ir);
        }

        private static string GenSingleAsm(Compiler compiler, IRUnit irunit, bool isMainUnit, string name, CompileOptions options, string outputDir)
        {
            Win64CodeGenContext context = new Win64CodeGenContext(compiler, irunit, isMainUnit, options);
            context.StartCodeGen();
            var result = context.GetResult();
            var path = System.IO.Path.Combine(outputDir, $"{name}.asm");
            System.IO.File.WriteAllText(path, result);

            return path;
        }

        public static void Log(string content)
        {
            if(!Compiler.enableLogCodeGen)
                return;
            GixConsole.WriteLine("Win64 >>>" + content);
        }

        public static RecAdditionInfo GetAdditionInf(this SymbolTable.Record rec)
        {
            rec.runtimeAdditionalInfo ??= new RecAdditionInfo(rec);
            return (rec.runtimeAdditionalInfo as RecAdditionInfo);
        }
    }

    public class Win64CodeGenContext
    {
        public Compiler compiler;
        public IRUnit ir;
        private bool isMainUnit;
        public List<IRUnit> allunits;


        public Gizbox.CompileOptions options;

        private ControlFlowGraph cfg;
        private List<BasicBlock> blocks;


        private Dictionary<string, IROperandExpr> operandCache = new();
        private Dictionary<TAC, TACInfo> tacInfoCache = new(); 


        private LList<X64Instruction> instructions = new();//1-n  1-1  n-1

        // 占位指令  
        private Dictionary<SymbolTable, HashSet<LList<X64Instruction>.Node>> callerSavePlaceHolders = new();//主调函数保存寄存器指令占位
        private Dictionary<SymbolTable, HashSet<LList<X64Instruction>.Node>>  callerRestorePlaceHolders = new();//主调函数恢复寄存器占位
        private Dictionary<SymbolTable, LList<X64Instruction>.Node> calleeSavePlaceHolders = new();//被调函数保存寄存器指令占位
        private Dictionary<SymbolTable, LList<X64Instruction>.Node> calleeRestorePlaceHolders = new();//被调函数恢复寄存器占位



        // 数据段  
        private Dictionary<string, List<(GType typeExpr, string valExpr)>> section_data = new();
        private Dictionary<string, List<(GType typeExpr, string valExpr)>> section_rdata = new();
        private Dictionary<string, List<GType>> section_bss = new();


        // 本单元全局变量表  
        public HashSet<string> globalVarInfos = new();
        // 本单元全局函数表  
        public HashSet<string> globalFuncsInfos = new();
        // 外部变量表  
        public HashSet<string> externVars = new();
        // 外部函数表  
        public HashSet<string> externFuncs = new();//value可以为空


        // 类表（所有单元）
        public Dictionary<string, SymbolTable.Record> classDict = new();
        // 函数表（所有单元）  
        public Dictionary<string, SymbolTable.Record> funcDict = new();

        // 类名-虚函数表roKey
        public Dictionary<string, string> vtableRoKeys = new();
        
        
        // 所有虚拟寄存器  
        private Dictionary<SymbolTable, Dictionary<SymbolTable.Record, List<VRegDesc>>> vRegDict = new();
        

        // 附加数据  
        private Dictionary<SymbolTable, int> localVarsSpaceSize = new();
        private Dictionary<X64Instruction, InstructionAdditionalInfo> instuctonAdditionalInfos = new();

        // *** 状态数据 ***
        private SymbolTable globalEnv = null; // 全局作用域
        private SymbolTable currFuncEnv = null;// 当前函数作用域  
        private Dictionary<RegisterEnum, RegUsageRange> currBorrowRegisters = new();//当前正在借用的寄存器  

        // *** 只读信息 ***  
        private static readonly RegisterEnum[] tempRegistersGP = new RegisterEnum[] { RegisterEnum.R11, RegisterEnum.R10, RegisterEnum.RAX };
        private static readonly RegisterEnum[] tempRegistersSSE = new RegisterEnum[] { RegisterEnum.XMM5, RegisterEnum.XMM4, RegisterEnum.XMM0 };

        // *** debug ***  
        private const bool debugLogSymbolTable = true;
        private const bool debugLogTacInfos = true;
        private const bool debugLogBlockInfos = false;
        private const bool debugLogAsmInfos = true;


        public Win64CodeGenContext(Compiler compiler, IRUnit ir, bool isMainUnit, CompileOptions options)
        {
            this.compiler = compiler;
            this.ir = ir;
            this.isMainUnit = isMainUnit;
            this.allunits = new();
            this.options = options;


            //所有关联的编译单元  
            void VisitUnit(IRUnit unit)
            {
                if(allunits.Contains(unit) == false)
                    allunits.Add(unit);
                foreach(var dep in unit.dependencyLibs)
                {
                    VisitUnit(dep);
                }
            }
            VisitUnit(ir);


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
            foreach(var dep in allunits)
            {
                if(dep == ir)
                    continue;

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

        public string GetResult()
        {
            return UtilityNASM.Emit(this, instructions, section_data, section_rdata, section_bss);
        }


        /// <summary> 静态信息补充 </summary>
        private void Pass1()
        {
            this.globalEnv = ir.globalScope.env;


            //常用C外部调用  
            externFuncs.Add("malloc");
            externFuncs.Add("free");

            //常用常量  
            if(!section_rdata.ContainsKey("__const_neg_one_f32"))
                section_rdata["__const_neg_one_f32"] = new() { (GType.Parse("float"), "-1.0") };
            if(!section_rdata.ContainsKey("__const_neg_one_f64"))
                section_rdata["__const_neg_one_f64"] = new() { (GType.Parse("double"), "-1.0") };


            //所有单元：所有类/虚函数表信息/函数记录    
            foreach(var unit in allunits)
            {
                var ge = unit.globalScope.env;
                foreach(var (k, r) in ge.records)
                {
                    switch(r.category)
                    {
                        case SymbolTable.RecordCatagory.Class:
                            classDict.Add(k, r);
                            foreach(var methodrec in r.envPtr.GetByCategory(SymbolTable.RecordCatagory.Function))
                            {
                                funcDict.Add(methodrec.envPtr.name, methodrec);
                            }
                            string rokey = $"vtable_{r.name}";
                            vtableRoKeys.Add(r.name, rokey);

                            if(unit == ir)
                            {
                                globalVarInfos.Add(rokey);
                            }
                            //是其他单元的类
                            else
                            {
                                externVars.Add(rokey);
                            }
                            break;
                        case SymbolTable.RecordCatagory.Function:
                            funcDict.Add(k, r);
                            break;
                    }
                }
            }


            //本编译单元：收集全局函数和全局变量信息    
            var genv = ir.globalScope.env;
            foreach(var (_, rec) in genv.records)
            {
                //全局变量  
                if(rec.category == SymbolTable.RecordCatagory.Variable)
                {
                    rec.GetAdditionInf().isGlobal = true;
                    string key = rec.name;
                    string initval = rec.initValue;

                    var type = GType.Parse(rec.typeExpression);
                    if(type.IsReferenceType == false)
                    {
                        section_data.Add(key, new() { GetStaticInitValue(rec) });
                    }
                    else
                    {
                        section_bss.Add(key, new() { GType.Parse(rec.typeExpression) });
                    }

                    globalVarInfos.Add(rec.name);
                }
                //全局函数  
                else if(rec.category == SymbolTable.RecordCatagory.Function)
                {
                    rec.GetAdditionInf().isGlobal = true;

                    if((rec.flags.HasFlag(SymbolTable.RecordFlag.ExternFunc)))
                    {
                        externFuncs.Add(rec.name);
                    }
                    else
                    {
                        globalFuncsInfos.Add(rec.name);
                    }

                }
            }

            //本单元：类布局/虚函数表布局/函数布局  
            var globalEnv = ir.globalScope.env;
            foreach(var (k, r) in globalEnv.records)
            {
                switch(r.category)
                {
                    case SymbolTable.RecordCatagory.Class:
                        {
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
                else if(tac.op == "CALL" || tac.op == "MCALL")
                {
                    currParamIdx = -1;
                }

                //CALL参数个数  
                if(tac.op == "CALL" || tac.op == "MCALL")
                {
                    int.TryParse(tac.arg1, out inf.CALL_paramCount);
                }

                //构造函数调用对象类型
                if(tac.op == "CALL" && tac.arg0.EndsWith(".ctor"))
                {
                    var prev = ir.codes[line - 1];
                    var prevInf = GetTacInfo(prev);
                    inf.CTOR_CALL_TargetObject = prevInf.oprand0;
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
            cfg.blocks = this.blocks;
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

            // 计算活跃区间并和合并  
            foreach(var b in blocks)
            {
                b.CaculateLiveRanges();
            }
            cfg.CaculateAndMergeLiveInfos();

            // 初步确定局部变量的栈帧布局  
            if(options.buildMode == BuildMode.Debug)
            {
                //Debug:局部变量内存不重叠  
                foreach(var (funcName, funcRec) in funcDict)
                {
                    var funcTable = funcRec.envPtr;

                    List<SymbolTable.Record> localVars = new();
                    foreach(var (memberName, memberRec) in funcTable.GetRecordsRecursive())
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
                    localVarsSpaceSize[funcRec.envPtr] = (int)frameSize;
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
                        Emit(X64.emptyLine(UtilsW64.ConvertLabel(tac.label)));
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
                            Emit(X64.jmp(tacInf.oprand0.segments[0]));
                        }
                        break;
                    case "FUNC_BEGIN":
                        {
                            //函数作用域开始
                            currFuncEnv = ir.GetOutermostEnvAtLine(i);


                            //函数序言
                            Emit(X64.push(X64.rbp));
                            Emit(X64.mov(X64.rbp, X64.rsp, X64Size.qword));

                            //保存寄存器（非易失性需要由Callee保存）  
                            var placeholder = X64.placehold("callee_save");
                            var placeholderNode = Emit(placeholder);
                            calleeSavePlaceHolders.Add(currFuncEnv, placeholderNode);

                            //rsp分配局部变量空间  
                            int localVarsSize = localVarsSpaceSize[currFuncEnv];
                            if(localVarsSize % 16 != 0)
                                localVarsSize = ((localVarsSize / 16) + 1) * 16;
                            var subrsp = X64.sub(X64.rsp, X64.imm(localVarsSize), X64Size.qword);
                            subrsp.comment += $"    (local variables space alloc)";
                            Emit(subrsp);
                        }
                        break;
                    case "FUNC_END":
                        {
                            //回收局部变量空间  
                            int localVarsSize = localVarsSpaceSize[currFuncEnv];
                            if(localVarsSize % 16 != 0)
                                localVarsSize = ((localVarsSize / 16) + 1) * 16;
                            var addrsp = X64.add(X64.rsp, X64.imm(localVarsSize), X64Size.qword);
                            addrsp.comment += $"    (local variables space release)";
                            Emit(addrsp);


                            //恢复寄存器（非易失性需要由Callee保存） 
                            Debug.Assert(currFuncEnv != null);
                            var placeholder = X64.placehold("callee_restore");
                            var placeholderNode = Emit(placeholder);
                            calleeRestorePlaceHolders.Add(currFuncEnv, placeholderNode);

                            //函数尾声
                            Emit(X64.mov(X64.rsp, X64.rbp, X64Size.qword));
                            Emit(X64.pop(X64.rbp));
                            Emit(X64.ret());

                            //函数作用域结束
                            currFuncEnv = null;
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
                                var size = (X64Size)tacInf.oprand0.typeExpr.Size;
                                if(tacinfo.oprand0.IsSSEType())
                                {
                                    // 浮点数返回值 -> xmm0
                                    using(new RegUsageRange(this, RegisterEnum.XMM0))
                                    {
                                        Emit(X64.mov(X64.xmm0, returnValue, size));
                                    }
                                }
                                else
                                {
                                    // 整数/指针返回值 -> rax
                                    using(new RegUsageRange(this, RegisterEnum.RAX))
                                    {
                                        if(tacinfo.oprand0.IsConstAddrSemantic())
                                            Emit(X64.lea(X64.rax, returnValue));
                                        else
                                            Emit(X64.mov(X64.rax, returnValue, size));
                                    }
                                }
                            }

                            // 跳转到FUNC_END  
                            string funcEndLabel = "func_end:" + currFuncEnv.name;
                            Emit(X64.jmp(funcEndLabel));
                        }
                        break;
                    case "EXTERN_IMPL":
                        {
                            //外部实现  

                        }
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
                            var targetFuncRec = tacInf.oprand0.segmentRecs[0];
                            Debug.Assert(targetFuncRec != null);

                            //调用前准备  
                            int rspSub = 0;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            BeforeCall(tacInf, tempParamList, out rspSub, ref homedRegs);

                            // 实际的函数调用
                            // （CALL 指令会自动把返回地址（下一条指令的 RIP）压入栈顶）  
                            Emit(X64.call(funcName));

                            //调用后处理
                            AfterCall(rspSub, homedRegs);
                        }
                        break;
                    case "MCALL":
                        {
                            // 第一参数是 方法名（未混淆），第二个参数是参数个数
                            string methodName = tac.arg0;

                            var targetFuncRec = tacInf.oprand0.segmentRecs[0];
                            Debug.Assert(targetFuncRec != null);


                            //this参数载入寄存器  
                            var codeParamObj = tempParamList.FirstOrDefault(c => c.PARAM_paramidx == 0);
                            var x64obj = ParseOperand(codeParamObj.oprand0);
                            Emit(X64.mov(X64.rcx, x64obj, X64Size.qword));//指针

                            //取Vptr  
                            string className = codeParamObj.oprand0.typeExpr.ToString();
                            var (index, vrec) = QueryVTable(className, methodName);
                            Emit(X64.mov(X64.rax, X64.mem(X64.rcx, disp: 0), X64Size.qword));
                            //函数地址（addr表示在虚函数表中的偏移(Index*8)）  
                            Emit(X64.mov(X64.rax, X64.mem(X64.rax, disp: index * 8), X64Size.qword));


                            //调用前准备(和CALL指令一致)  
                            int rspSub = 0;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            BeforeCall(tacInf, tempParamList, out rspSub, ref homedRegs);
                            

                            //调用
                            Emit(X64.call(X64.rax));


                            //调用后处理
                            AfterCall(rspSub, homedRegs);
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

                            int rspSub;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            List<(X64Operand srcOperand, int idx, GType type, bool isConstAddrSemantic, string? rokey)> paramInfos 
                                = new() { (X64.imm(objectSize), 0, GType.Parse("int"), false, null) } ;
                            BeforeCall(1, paramInfos, out rspSub, ref homedRegs);
                            // 调用malloc分配堆内存（参数是字节数）
                            Emit(X64.call("malloc"));
                            AfterCall(rspSub, homedRegs);

                            // 分配的内存地址存储到目标变量  
                            var targetVar = ParseOperand(tacInf.oprand0);
                            Emit(X64.mov(targetVar, X64.rax, X64Size.qword));

                            // 将虚函数表地址写入对象的前8字节(地址)
                            Emit(X64.lea(X64.rdx, X64.rel(vtableRoKeys[typeName])));
                            Emit(X64.mov(X64.mem(X64.rax, disp: 0), X64.rdx, X64Size.qword));
                        }
                        break;
                    case "ALLOC_ARRAY":
                        {
                            var target = ParseOperand(tacInf.oprand0);
                            var lenOp = ParseOperand(tacInf.oprand1);
                            var arrType = tacInf.oprand0.typeExpr;
                            var elemType = arrType.ArrayElementType;
                            int elemSize = elemType.Size;

                            Emit(X64.mov(X64.rax, lenOp, X64Size.qword));//RAX 作为中间寄存器
                            Emit(X64.imul_2(X64.rax, X64.imm(elemSize), X64Size.qword));

                            //动态分配  
                            int rspSub;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            List<(X64Operand srcOperand, int idx, GType type, bool isConstAddrSemantic, string? rokey)> paramInfos
                                = new() { (X64.rax, 0, GType.Parse("int"), false, null) };
                            BeforeCall(1, paramInfos, out rspSub, ref homedRegs);
                            Emit(X64.call("malloc"));
                            AfterCall(rspSub, homedRegs);


                            //返回指针在RAX，写入目标变量  
                            Emit(X64.mov(target, X64.rax, X64Size.qword));
                        }
                        break;
                    case "DEALLOC":
                        {
                            var objPtr = ParseOperand(tacInf.oprand0);

                            int rspSub;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            List<(X64Operand srcOperand, int idx, GType type, bool isConstAddrSemantic, string? rokey)> paramInfos
                                = new() { (objPtr, 0, tacInf.oprand0.typeExpr, false, null) };
                            BeforeCall(1, paramInfos, out rspSub, ref homedRegs);
                            Emit(X64.call("free"));
                            AfterCall(rspSub, homedRegs);
                        }
                        break;
                    case "DEALLOC_ARRAY":
                        {
                            // 与DEALLOC一致
                            var arrPtr = ParseOperand(tacInf.oprand0);

                            int rspSub;
                            List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs = new();
                            List<(X64Operand srcOperand, int idx, GType type, bool isConstAddrSemantic, string? rokey)> paramInfos
                                = new() { (arrPtr, 0, tacInf.oprand0.typeExpr, false, null) };
                            BeforeCall(1, paramInfos, out rspSub, ref homedRegs);
                            Emit(X64.call("free"));
                            AfterCall(rspSub, homedRegs);
                        }
                        break;
                    case "IF_FALSE_JUMP":
                        {
                            var cond = ParseOperand(tacInf.oprand0);
                            //AddInstructionLast(X64.test(cond, cond));
                            //AddInstructionLast(X64.jz(tacInf.oprand1.segments[0]));//jump if zero

                            // 先零扩展到64位再test，避免上层仅写入低8位导致的高位脏值干扰
                            if(!tacInf.oprand0.IsSSEType() && tacInf.oprand0.typeExpr.Size == 1)
                            {
                                using(new RegUsageRange(this, RegisterEnum.R11))
                                {
                                    if(cond is X64Immediate imm)
                                        Emit(X64.mov(X64.r11, imm, X64Size.@byte));
                                    Emit(X64.movzx(X64.r11, cond, X64Size.qword, X64Size.@byte));
                                    Emit(X64.test(X64.r11, X64.r11));
                                }
                            }
                            else
                            {
                                Emit(X64.test(cond, cond));
                            }
                            Emit(X64.jz(tacInf.oprand1.segments[0]));// jump if zero
                        }
                        break;
                    case "=":
                        {
                            var dst = ParseOperand(tacInf.oprand0);

                            if(tacInf.oprand1.IsConstAddrSemantic())
                            {
                                var key = tacInf.oprand1.expr.roDataKey;

                                using(new RegUsageRange(this, RegisterEnum.R11))
                                {
                                    Emit(X64.lea(X64.r11, X64.rel(key)));
                                    EmitMov(dst, X64.r11, tacInf.oprand0.typeExpr);
                                }
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

                            var size = (X64Size)tacInf.oprand0.typeExpr.Size;
                            if(tacInf.oprand0.IsSSEType())
                            {
                                bool isF32 = size == X64Size.dword;

                                // 工作寄存器
                                X64Operand work = dst;
                                bool needStoreBack = false;

                                // 目标已是XMM
                                if(work is X64Reg r && r.isVirtual == false && r.IsXXM())
                                {
                                    //若dst != src先复制  
                                    if(!Equals(dst, src))
                                    {
                                        if(isF32)
                                            Emit(X64.movss(work, src));
                                        else
                                            Emit(X64.movsd(work, src));
                                    }
                                }
                                // 若目标不是XMM（可能是内存） -> 用xmm0中转
                                else
                                {
                                    using(new RegUsageRange(this, RegisterEnum.XMM0))
                                    {
                                        work = X64.xmm0;
                                        needStoreBack = true;
                                        // 加载src
                                        if(isF32)
                                            Emit(X64.movss(work, src));
                                        else
                                            Emit(X64.movsd(work, src));
                                    }
                                }

                                // 乘以-1f或者-1d  
                                if(isF32)
                                    Emit(X64.mulss(work, X64.rel("__const_neg_one_f32")));
                                else
                                    Emit(X64.mulsd(work, X64.rel("__const_neg_one_f64")));

                                // 回写
                                if(needStoreBack)
                                {
                                    if(isF32)
                                        Emit(X64.movss(dst, work));
                                    else
                                        Emit(X64.movsd(dst, work));
                                }
                            }
                            else
                            {
                                Emit(X64.mov(dst, src, size));
                                Emit(X64.neg(dst, size));
                            }
                        }
                        break;
                    case "!":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand1);
                            var size = tacInf.oprand0.typeExpr.Size;
                            Emit(X64.mov(dst, src, (X64Size)size));
                            Emit(X64.test(dst, dst));
                            Emit(X64.sete(dst));
                            //把仅写入低8位的结果零扩展到寄存器宽度
                            if(dst is not X64Reg)
                            {
                                using(new RegUsageRange(this, RegisterEnum.R11))
                                {
                                    Emit(X64.mov(X64.r11, dst, (X64Size)size));
                                    Emit(X64.movzx(X64.r11, X64.r11, X64Size.qword, X64Size.@byte));
                                }
                            }
                            else
                            {
                                Emit(X64.movzx(dst, dst, X64Size.qword, X64Size.@byte));
                            }
                        }
                        break;
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                    case "==":
                    case "!=":
                        {
                            EmitCompare(tacInf, tac.op);
                        }
                        break;
                    case "++":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var size = tacInf.oprand0.typeExpr.Size;
                            if(!tacInf.oprand0.IsSSEType()) Emit(X64.inc(dst, (X64Size)size));
                        }
                        break;
                    case "--":
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var size = tacInf.oprand0.typeExpr.Size;
                            if(!tacInf.oprand0.IsSSEType()) Emit(X64.dec(dst, (X64Size)size));
                        }
                        break;
                    case "CAST"://CAST [tmp15] Human [tmp14]   tmp15 = (Human)tmp14
                        {
                            var dst = ParseOperand(tacInf.oprand0);
                            var src = ParseOperand(tacInf.oprand2);

                            var targetType = GType.Parse(tacInf.tac.arg1);
                            var srcType = tacInf.oprand2.typeExpr;

                            var sizeDst = targetType.Size;
                            var sizeSrc = srcType.Size;

                            X64Operand castDst = dst;
                            X64Operand castSrc = src;

                            bool isUsingR11 = false;
                            InstructionNode last = instructions.Last;

                            if(castSrc is X64Immediate imm)
                            {
                                Emit(X64.mov(X64.r11, imm, (X64Size)sizeSrc));
                                castSrc = X64.r11;
                                isUsingR11 = true;
                            }

                            //相同类型
                            if(srcType.ToString() == targetType.ToString())
                            {
                                if(castDst != castSrc)
                                    Emit(X64.mov(castDst, castSrc, (X64Size)sizeDst));
                                break;
                            }
                            //指针类型转换 -> 指针直接赋值  
                            else if(srcType.IsReferenceType && targetType.IsReferenceType)
                            {
                                Emit(X64.mov(castDst, castSrc, X64Size.qword));
                            }
                            //浮点数 -> 浮点数
                            else if(srcType.IsSSE && targetType.IsSSE)
                            {
                                if(srcType.Size == 4 && targetType.Size == 8)
                                    Emit(X64.cvtss2sd(castDst, castSrc));
                                else if(srcType.Size == 8 && targetType.Size == 4)
                                    Emit(X64.cvtsd2ss(castDst, castSrc));
                            }
                            //整数->整数  
                            else if(srcType.IsInteger && targetType.IsInteger)
                            {
                                if(srcType.Size == targetType.Size)
                                {
                                    Emit(X64.mov(castDst, castSrc, (X64Size)sizeDst));
                                }
                                else if(srcType.Size < targetType.Size)//扩展
                                {
                                    if(castSrc is not X64Immediate srcimm && castDst is X64Reg dstreg)
                                    {
                                        if(srcType.IsSigned)
                                            Emit(X64.movsx(castDst, castSrc, (X64Size)sizeDst, (X64Size)sizeSrc));
                                        else
                                            Emit(X64.movzx(castDst, castSrc, (X64Size)sizeDst, (X64Size)sizeSrc));
                                    }
                                    else
                                    {
                                        using(new RegUsageRange(this, RegisterEnum.R10))
                                        {
                                            var tsrc = castSrc;
                                            var tdst = castDst;
                                            if(tdst is not X64Reg)
                                            {
                                                tdst = X64.r10;
                                            }

                                            if(srcType.IsSigned)
                                                Emit(X64.movsx(tdst, tsrc, (X64Size)sizeDst, (X64Size)sizeSrc));
                                            else
                                                Emit(X64.movzx(tdst, tsrc, (X64Size)sizeDst, (X64Size)sizeSrc));

                                            if(tdst is not X64Reg)
                                            {
                                                Emit(X64.mov(castDst, tdst, (X64Size)sizeDst));
                                            }
                                        }
                                    }
                                }
                                else //截断
                                {
                                    Emit(X64.mov(castDst, castSrc, (X64Size)sizeDst));
                                }
                            }
                            //整数->浮点数
                            else if(srcType.IsInteger && targetType.IsSSE)
                            {
                                if(targetType.Size == 4)
                                    Emit(X64.cvtsi2ss(castDst, castSrc, srcIntSize:(X64Size)sizeSrc));
                                else 
                                    Emit(X64.cvtsi2sd(castDst, castSrc, srcIntSize: (X64Size)sizeSrc));
                            }
                            // 浮点 -> 整数 (截断)
                            else if(srcType.IsSSE && targetType.IsInteger)
                            {
                                if(srcType.Size == 4)
                                {
                                    Emit(X64.cvttss2si(castDst, castSrc, dstInstSize: (X64Size)targetType.Size));
                                }
                                else
                                {
                                    Emit(X64.cvttsd2si(castDst, castSrc, dstInstSize: (X64Size)targetType.Size));
                                }
                            }
                            else
                            {
                                throw new GizboxException(ExceptioName.CodeGen, $"cast not support:{srcType.ToString()}->{targetType.ToString()}");
                            }

                            //标记R11中转  
                            var newlast = instructions.Last;
                            if(isUsingR11)
                            {
                                UseScratchRegister(last.Next, newlast, RegisterEnum.R11);
                            }

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

                    if(string.IsNullOrEmpty(lastInstruction.Next.value.label) == false)
                    {
                        throw new GizboxException(ExceptioName.CodeGen, $"label repeat:{lastInstruction.Next.value.label}  &  {tac.label}");
                    }


                    string label = tac.label;
                    if(this.isMainUnit == false && label == "main")
                    {
                        label = "discard_main";
                    }

                    lastInstruction.Next.value.label = UtilsW64.ConvertLabel(label);
                }

                //添加注释
                if(lastInstruction != null)
                {
                    if(lastInstruction.Next != null)
                    {
                        lastInstruction.Next.value.comment = $"(IR-{tacInf.line} : {tac.ToExpression(showlabel: false, indent: false)})" + lastInstruction.Next.value.comment;
                    }
                    else
                    {
                        lastInstruction.value.comment = $"(DiscardIR-{tacInf.line} : {tac.ToExpression(showlabel: false, indent: false)})" + lastInstruction.value.comment;
                    }
                }
                    

                //记录
                lastInstruction = instructions.Last;
            }
        }

        // <summary> 寄存器分配 </summary>
        private void Pass4()
        {
            //栈上参数/全局变量的虚拟寄存器用固定的物理寄存器中转  
            CheckGlobalVarAndStackParamVRegUse();

            //收集变量虚拟寄存器信息  
            var curr = instructions.First;
            X64Operand[] oprands = new X64Operand[2];
            while(curr != null)
            {
                oprands[0] = curr.value.operand0;
                oprands[1] = curr.value.operand1;

                for(int i = 0; i < 2; ++i)
                {
                    var targetOperand = oprands[i];
                    if(targetOperand is X64Reg reg && reg.isVirtual)
                    {
                        //变量的虚拟寄存器 -> 加入待着色寄存器列表
                        if(reg.vRegVar.category == SymbolTable.RecordCatagory.Variable)
                        {
                            var funcEnv = reg.vRegVar.GetAdditionInf().GetFunctionEnv();
                            vRegDict.GetOrCreate(funcEnv).GetOrCreate(reg.vRegVar).Add(new(curr, i, VRegDesc.Kind.Oprand));
                        }
                    }
                    if(targetOperand is X64Mem xMem0)
                    {
                        if(xMem0.baseReg != null && xMem0.baseReg.isVirtual)
                        {
                            if(xMem0.baseReg.vRegVar.category == SymbolTable.RecordCatagory.Variable)
                            {
                                var funcEnv = xMem0.baseReg.vRegVar.GetAdditionInf().GetFunctionEnv();
                                vRegDict.GetOrCreate(funcEnv).GetOrCreate(xMem0.baseReg.vRegVar).Add(new(curr, i, VRegDesc.Kind.OprandBase));
                            }
                        }
                        if(xMem0.indexReg != null && xMem0.indexReg.isVirtual)
                        {
                            if(xMem0.indexReg.vRegVar.category == SymbolTable.RecordCatagory.Variable)
                            {
                                var funcEnv = xMem0.indexReg.vRegVar.GetAdditionInf().GetFunctionEnv();
                                vRegDict.GetOrCreate(funcEnv).GetOrCreate(xMem0.indexReg.vRegVar).Add(new(curr, i, VRegDesc.Kind.OprandIndex));
                            }
                        }
                    }
                }

                curr = curr.Next;
            }


            //函数信息  
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
                foreach(var (_, rec) in func.funcRec.envPtr.GetRecordsRecursive())
                {
                    if(rec.category != SymbolTable.RecordCatagory.Variable)//仅局部变量参与寄存器着色  
                        continue;

                    //(不出现在DEF、USE、IN、OUT中的变量)  
                    if(cfg.varialbeLiveInfos.ContainsKey(rec) == false)
                    {
                        GixConsole.WriteLine($"局部变量 {rec.name} 没有活跃信息");
                        var newNode = gpGraph.AddVarNode(rec);
                        continue;
                    }

                    //活跃区间空（理论不应该出现，理应被优化，需要兜底处理） -> 不进行着色（home到对应内存槽位）  
                    if(cfg.varialbeLiveInfos[rec].mergedRanges == null || cfg.varialbeLiveInfos[rec].mergedRanges.Count == 0)
                    {
                        continue;
                    }

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


                //着色（目前只用 callee-save（非易失）寄存器）    
                //使用 callee-save 只需在函数序言/尾声一次性保存/恢复“实际用到的非易失寄存器”，对调用点零改动，最简单直接。
                gpGraph.Coloring(X64RegisterUtility.GPCalleeSaveRegs.Where(r => r != RegisterEnum.RBP).ToArray());
                sseGraph.Coloring(X64RegisterUtility.XMMCalleeSaveRegs);


                // *** 寄存器分配后的统一重写阶段  *** 

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

                //统计本函数实际用到的寄存器集合（不应包含rbp）  
                var usedGpRegs = new HashSet<RegisterEnum>(gpAssign.Values);
                var usedXmmRegs = new HashSet<RegisterEnum>(sseAssign.Values);

                var funcUsedCalleeGP = new HashSet<RegisterEnum>(usedGpRegs.Where(r => X64RegisterUtility.GPCalleeSaveRegs.Contains(r) && r != RegisterEnum.RBP));
                var funcUsedCalleeXMM = new HashSet<RegisterEnum>(usedXmmRegs.Where(r => X64RegisterUtility.XMMCalleeSaveRegs.Contains(r)));

                var funcUsedCallerGP = new HashSet<RegisterEnum>(usedGpRegs.Where(r => X64RegisterUtility.GPCallerSaveRegs.Contains(r)));
                var funcUsedCallerXMM = new HashSet<RegisterEnum>(usedXmmRegs.Where(r => X64RegisterUtility.XMMCallerSaveRegs.Contains(r)));

                GixConsole.WriteLine($"*****函数{func.funcRec.name}可以使用的寄存器：{string.Join(",", X64RegisterUtility.GPCalleeSaveRegs.Where(r => r != RegisterEnum.RBP).ToArray())}");
                GixConsole.WriteLine($"*****函数{func.funcRec.name}使用到的寄存器：{string.Join(", ", (usedGpRegs).Select(r => r.ToString()))}");


                //caller-save占位展开  
                if(callerSavePlaceHolders.TryGetValue(func.funcRec.envPtr, out var list))
                {
                    foreach(var placeholder in list)
                    {
                        //要移动的标签和注释  
                        var holdlabel = placeholder.value.label;
                        var holdcomment = placeholder.value.comment;

                        var ind = placeholder.Prev;
                        instructions.Remove(placeholder);

                        int bytes = funcUsedCallerGP.Count * 8 + funcUsedCallerXMM.Count * 16;

                        //对齐字节数  
                        if(bytes % 16 != 0)
                        {
                            bytes += 8;
                        }
                        //rsp移动  
                        ind = InsertAfter(ind, X64.sub(X64.rsp, X64.imm(bytes), X64Size.qword));
                        ind.value.label = holdlabel;
                        ind.value.comment = holdcomment + $"    (caller-save of {func.funcRec.name} start)";

                        //保存寄存器
                        int offset = 0;
                        foreach(var reg in funcUsedCallerGP)
                        {
                            ind = InsertAfter(ind, X64.mov(X64.mem(X64.rsp, disp: offset), new X64Reg(reg), X64Size.qword));
                            offset += 8;
                        }
                        foreach(var reg in funcUsedCallerXMM)
                        {
                            ind = InsertAfter(ind, X64.movdqu(X64.mem(X64.rsp, disp: offset), new X64Reg(reg)));
                            offset += 16;
                        }
                        ind.value.comment += $"    (caller-save of {func.funcRec.name} finish)";
                    }
                }
                else
                {
                    //没被调用过
                }

                //caller-restore占位展开  
                if(callerRestorePlaceHolders.TryGetValue(func.funcRec.envPtr, out var restoreList))
                {
                    foreach(var placeholder in restoreList)
                    {
                        //要移动的标签和注释  
                        var holdlabel = placeholder.value.label;
                        var holdcomment = placeholder.value.comment;
                        InstructionNode copyTo = null;

                        var ind = placeholder.Prev;
                        instructions.Remove(placeholder);

                        int bytes = funcUsedCallerGP.Count * 8 + funcUsedCallerXMM.Count * 16;
                        //16字节对齐
                        if(bytes % 16 != 0)
                        {
                            bytes += 8;
                        }
                        //恢复寄存器
                        int offset = 0;
                        foreach(var reg in funcUsedCallerGP)
                        {
                            ind = InsertAfter(ind, X64.mov(new X64Reg(reg), X64.mem(X64.rsp, disp: offset), X64Size.qword));
                            if(copyTo == null)
                                copyTo = ind;
                            
                            offset += 8;
                        }
                        foreach(var reg in funcUsedCallerXMM)
                        {
                            ind = InsertAfter(ind, X64.movdqu(new X64Reg(reg), X64.mem(X64.rsp, disp: offset)));
                            if(copyTo == null)
                                copyTo = ind;

                            offset += 16;
                        }

                        //rsp移动  
                        ind = InsertAfter(ind, X64.add(X64.rsp, X64.imm(bytes), X64Size.qword));
                        if(copyTo == null)
                            copyTo = ind;

                        //移动标签和注释  
                        copyTo.value.label = holdlabel;
                        copyTo.value.comment = holdcomment;

                        copyTo.value.comment += $"    (caller-restore of {func.funcRec.name} start)";
                        ind.value.comment += $"    (caller-restore of {func.funcRec.name} finish)";
                    }
                }
                else
                {
                    //没被调用过
                }

                //callee-save占位展开  
                int localvarsMoveBytes = 0;//局部变量向低地址移动让出空间给callee-save区域    
                if(calleeSavePlaceHolders.TryGetValue(func.funcRec.envPtr, out var saveplaceholder))
                {

                    //要移动的标签和注释  
                    var holdlabel = saveplaceholder.value.label;
                    var holdcomment = saveplaceholder.value.comment;


                    var ind = saveplaceholder.Prev;
                    instructions.Remove(saveplaceholder);

                    int bytes = funcUsedCalleeGP.Count * 8 + funcUsedCalleeXMM.Count * 16;
                    //16字节对齐
                    if(bytes % 16 != 0)
                    {
                        bytes += 8;
                    }
                    localvarsMoveBytes = bytes;

                    //rsp移动  
                    ind = InsertAfter(ind, X64.sub(X64.rsp, X64.imm(bytes), X64Size.qword));
                    ind.value.label = holdlabel;
                    ind.value.comment = holdcomment;
                    ind.value.comment += $"    (callee-save of {func.funcRec.name} start)";

                    //保存寄存器  
                    int offset = 0;
                    foreach(var reg in funcUsedCalleeGP)
                    {
                        ind = InsertAfter(ind, X64.mov(X64.mem(X64.rsp, disp: offset), new X64Reg(reg), X64Size.qword));
                        offset += 8;
                    }
                    foreach(var reg in funcUsedCalleeXMM)
                    {
                        ind = InsertAfter(ind, X64.movdqu(X64.mem(X64.rsp, disp: offset), new X64Reg(reg)));
                        offset += 16;
                    }


                    ind.value.comment += $"    (callee-save of {func.funcRec.name} finish)";

                }
                //局部变量位移  
                foreach(var (recName, rec) in func.funcRec.envPtr.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Variable)
                        continue;

                    rec.addr -= localvarsMoveBytes;
                    GixConsole.WriteLine($"单元 {ir.name} 的函数{func.funcRec.name} 变量位移：{recName}向下位移{localvarsMoveBytes}字节到{rec.addr}");
                }


                //callee-restore占位展开  
                if(calleeRestorePlaceHolders.TryGetValue(func.funcRec.envPtr, out var restorenode))
                {
                    //要移动的标签和注释  
                    var holdlabel = restorenode.value.label;
                    var holdcomment = restorenode.value.comment;
                    InstructionNode copyTo = null;


                    var ind = restorenode.Prev;
                    instructions.Remove(restorenode);

                    int bytes = funcUsedCalleeGP.Count * 8 + funcUsedCalleeXMM.Count * 16;
                    //16字节对齐
                    if(bytes % 16 != 0)
                    {
                        bytes += 8;
                    }
                    //恢复寄存器
                    int offset = 0;
                    foreach(var reg in funcUsedCalleeGP)
                    {
                        ind = InsertAfter(ind, X64.mov(new X64Reg(reg), X64.mem(X64.rsp, disp: offset), X64Size.qword));
                        if(copyTo == null)
                            copyTo = ind;

                        offset += 8;
                    }
                    foreach(var reg in funcUsedCalleeXMM)
                    {
                        ind = InsertAfter(ind, X64.movdqu(new X64Reg(reg), X64.mem(X64.rsp, disp: offset)));
                        if(copyTo == null)
                            copyTo = ind;

                        offset += 16;
                    }

                    //rsp移动  
                    ind = InsertAfter(ind, X64.add(X64.rsp, X64.imm(bytes), X64Size.qword));
                    if(copyTo == null)
                        copyTo = ind;


                    copyTo.value.label = holdlabel;
                    copyTo.value.comment = holdcomment;
                    copyTo.value.comment += $"    (callee-restore of {func.funcRec.name} start)";

                    ind.value.comment += $"    (callee-restore of {func.funcRec.name} finish)";
                }
                instructions.Rebuild();



                //替换虚拟寄存器，生成溢出代码  
                if(this.vRegDict.TryGetValue(func.funcRec.envPtr, out var dict))
                {
                    foreach(var (rec, vregList) in dict)
                    {
                        bool hasAllocReg = false;
                        RegisterEnum regAlloc = RegisterEnum.Undefined;
                        if(gpAssign.ContainsKey(rec))
                        {
                            hasAllocReg = true;
                            regAlloc = gpAssign[rec];
                        }
                        else if(sseAssign.ContainsKey(rec))
                        {
                            hasAllocReg = true;
                            regAlloc = sseAssign[rec];
                        }

                        foreach(var desc in vregList)
                        {
                            InstructionNode instr = desc.targetInstructionNode;
                            X64Operand oprand = desc.oprandIdx == 0 ? instr.value.operand0 : instr.value.operand1;
                            X64Reg vr = null;
                            switch(desc.kind)
                            {
                                case VRegDesc.Kind.Oprand:
                                    vr = oprand as X64Reg;
                                    break;
                                case VRegDesc.Kind.OprandBase:
                                    vr = (oprand as X64Mem).baseReg;
                                    break;
                                case VRegDesc.Kind.OprandIndex:
                                    vr = (oprand as X64Mem).indexReg;
                                    break;
                            }
                            if(vr.isVirtual == false)
                                continue;//已经被分配物理寄存器了

                            if(vr.vRegVar != rec)
                                throw new GizboxException(ExceptioName.Undefine, $"vreg variable error.{vr.vRegVar.name} not match {rec.name}");

                            //分配物理寄存器  
                            if(hasAllocReg)
                            {
                                vr.AllocPhysReg(regAlloc);
                                instr.value.comment += $"    (vreg \"{vr.vRegVar.name}\" at {desc.kind}{desc.oprandIdx} alloc {regAlloc})";
                            }
                            //溢出到内存  
                            else
                            {
                                if(desc.kind == VRegDesc.Kind.Oprand)
                                {
                                    if(desc.oprandIdx == 0)
                                        instr.value.operand0 = X64.mem(X64.rbp, disp: vr.vRegVar.addr);
                                    else
                                        instr.value.operand1 = X64.mem(X64.rbp, disp: vr.vRegVar.addr);

                                    instr.value.comment += $"    (vreg \"{vr.vRegVar.name}\" at {desc.kind}{desc.oprandIdx} home to mem.)";
                                }
                                // 作为地址基址：r11中转
                                else if(desc.kind == VRegDesc.Kind.OprandBase)
                                {
                                    //(（指针 64 位）)
                                    var xmem = (oprand as X64Mem);
                                    var scratch = TryGetIdleScratchReg(instr.Prev, instr, isSSE:false, regPrefer:RegisterEnum.R11);
                                    var newinsn = InsertBefore(instr, X64.mov(new X64Reg(scratch), X64.mem(X64.rbp, disp: vr.vRegVar.addr), X64Size.qword));
                                    xmem.baseReg = new X64Reg(scratch);
                                    UseScratchRegister(newinsn, instr, scratch);

                                    instr.value.comment += $"    (materialize base vreg \"{vr.vRegVar.name}\" -> r11)";
                                }
                                // 作为地址变址：r10中转，并做符号或零扩展到 64 位
                                else if(desc.kind == VRegDesc.Kind.OprandIndex)
                                {
                                    var xmem = (oprand as X64Mem);
                                    var vtype = GType.Parse(vr.vRegVar.typeExpression);
                                    int sz = vtype.Size;

                                    var scratch = TryGetIdleScratchReg(instr.Prev, instr, isSSE: false, regPrefer: RegisterEnum.R10);
                                    if(sz == 8)
                                    {
                                        var newinsn = InsertBefore(instr, X64.mov(new X64Reg(scratch), X64.mem(X64.rbp, disp: vr.vRegVar.addr), X64Size.qword));
                                        UseScratchRegister(newinsn, instr, scratch);
                                    }
                                    else
                                    {
                                        var srcSize = (X64Size)sz;

                                        InstructionNode newinsn;

                                        if(vtype.IsSigned)
                                            newinsn = InsertBefore(instr, X64.movsx(new X64Reg(scratch), X64.mem(X64.rbp, disp: vr.vRegVar.addr), X64Size.qword, srcSize));
                                        else
                                            newinsn = InsertBefore(instr, X64.movzx(new X64Reg(scratch), X64.mem(X64.rbp, disp: vr.vRegVar.addr), X64Size.qword, srcSize));

                                        UseScratchRegister(newinsn, instr, scratch);
                                    }

                                    xmem.indexReg = new X64Reg(scratch);
                                    instr.value.comment += $"    (materialize index vreg \"{vr.vRegVar.name}\" -> r10)";
                                }
                            }
                        }

                    }
                }

            }

            //mem-mem指令合法化  
            LegalizeMemToMem();

            //mov合法化  
            LegalizeMov();

            //最终结果
            if(debugLogAsmInfos)
            {
                Win64Target.Log(GetResult());
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


            //构造函数加入globalFuncs  
            globalFuncsInfos.Add(classTable.name + ".ctor");

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

                foreach(var frec in ir.QueryVTable(classTable.name))
                {
                    vtableData.Add(X64.label(frec.funcfullname));

                    if(ir.globalScope.env.ContainRecordName(frec.className))
                    {
                        if(globalFuncsInfos.Contains(frec.funcfullname) == false)
                            globalFuncsInfos.Add(frec.funcfullname);
                    }
                    else
                    {
                        if(externFuncs.Contains(frec.funcfullname) == false)
                            externFuncs.Add(frec.funcfullname);
                    }
                }

                //填充rodata  
                string rokey = $"vtable_{classRec.name}";
                section_rdata.Add(rokey, vtableData.Select(v => (GType.Parse("(FuncPtr)"), v.name)).ToList());
            }
        }

        private void GenFuncInfo(SymbolTable.Record funcRec)
        {
            var funcTable = funcRec.envPtr;


            if(funcTable == null)
                throw new GizboxException(ExceptioName.CodeGen, $"null func table of {(funcRec?.name ?? "?")}.");

            //参数偏移  
            {
                foreach(var (key, rec) in funcTable.records)
                {
                    if(rec.category != SymbolTable.RecordCatagory.Param)
                        continue;
                    rec.addr = 16 + (rec.index * 8);//16包含rbp槽和返回值地址槽  
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
                case "DEALLOC":
                    {
                        USE(tac.arg0, lineNum, block, envStack);
                    }
                    break;
                case "DEALLOC_ARRAY":
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
                                    var value = (iroperandExpr.segments[1] == "true" || iroperandExpr.segments[1] == "True") ? 1 : 0;
                                    return X64.imm(value);
                                }
                                break;
                            case "%LITINT":
                                {
                                    var value = int.Parse(UtilsW64.GLitToW64Lit(irOperand.typeExpr, iroperandExpr.segments[1]));//int.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "%LITLONG":
                                {
                                    var value = long.Parse(UtilsW64.GLitToW64Lit(irOperand.typeExpr, iroperandExpr.segments[1])); //long.Parse(iroperandExpr.segments[1]);
                                    return X64.imm(value);
                                }
                                break;
                            case "%LITCHAR":
                                {
                                    var charValue = iroperandExpr.segments[1];
                                    if(charValue.Length != 3)//单引号 + 字符 + 单引号
                                        throw new GizboxException(ExceptioName.CodeGen, $"lit char value error.({charValue.Length})");
                                    return X64.imm((long)charValue[1]);
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
                        var rec = irOperand.segmentRecs[0];

                        if(rec == null)
                        {
                            throw new GizboxException(ExceptioName.Undefine, $"null var/param rec in segment recs:{varName}");
                        }

                        return GetRecOperand(rec);
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
                        var baseVreg = GetRecVReg(objRec);
                        // 生成内存操作数
                        return X64.mem(baseVreg, disp: fieldRec.addr);
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
                        var elemType = GType.Parse(arrayRec.typeExpression).ArrayElementType;
                        int elemSize = elemType.Size;

                        var baseV = GetRecVReg(arrayRec);

                        // 所有可能是LITINT或者变量  
                        var idxRec = Query(indexExpr, irOperand.owner.line);
                        if(idxRec != null)
                        {
                            var idxV = GetRecVReg(idxRec);
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
                                return X64.mem(baseV, disp: disp);
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


        /// <summary> 变量或参数分配操作数（如果不是寄存器就包装为虚拟寄存器） </summary>
        public X64Reg GetRecVReg(SymbolRecord rec)
        {
            var oprand = GetRecOperand(rec);
            if(oprand is X64Reg reg)
            {
                return reg;
            }
            else
            {
                //虚拟寄存器  
                var vreg = new X64Reg(rec);
                return vreg;
            }
        }
        /// <summary> 变量或参数分配操作数 </summary>
        public X64Operand GetRecOperand(SymbolRecord rec)
        {
            if(rec.category == SymbolTable.RecordCatagory.Param)
            {
                var type = GType.Parse(rec.typeExpression);
                if(type.IsSSE == false)
                {
                    switch(rec.index)
                    {
                        case 0:
                            return X64.rcx;
                        case 1:
                            return X64.rdx;
                        case 2:
                            return X64.r8;
                        case 3:
                            return X64.r9;
                        default:
                            return X64.mem(X64.rbp, disp: rec.addr); // 超过4个参数走栈 (addr已考虑rbp和返回地址槽位)
                    }
                }
                else
                {
                    switch(rec.index)
                    {
                        case 0:
                            return X64.xmm0;
                        case 1:
                            return X64.xmm1;
                        case 2:
                            return X64.xmm2;
                        case 3:
                            return X64.xmm3;
                        default:
                            return X64.mem(X64.rbp, disp: rec.addr); // 超过4个参数走栈
                    }
                }
            }
            else if(rec.category == SymbolTable.RecordCatagory.Variable)
            {
                if(rec.GetAdditionInf().isGlobal)
                {
                    return X64.rel(rec.name);
                }
                else
                {
                    var xoperand = new X64Reg(rec);
                    return xoperand;
                }
            }
            else
            {
                throw new GizboxException(ExceptioName.CodeGen, "unknown error.");
            }
        }

        #endregion

        #region PASS3

        // Call调用前  
        private void BeforeCall(TACInfo tacInfo, List<TACInfo> tempParamList, out int rspSub, ref List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs)
        {
            int paramCount = tacInfo.CALL_paramCount;
            List<(X64Operand srcOperand, int idx, GType type, bool isRefOfConst, string rokey)> paraminfos = new();
            foreach(var p in tempParamList )
            {
                var tSrcOperand = ParseOperand(p.oprand0);
                int tIdx = p.PARAM_paramidx;
                GType tType = p.oprand0.typeExpr;
                bool tIsConstAddrSemantic = p.oprand0.IsConstAddrSemantic();
                string tRokey = p.oprand0.expr.roDataKey;

                paraminfos.Add((tSrcOperand, tIdx, tType, tIsConstAddrSemantic, tRokey));
            }

            BeforeCall(paramCount, paraminfos, out rspSub, ref homedRegs);

            tempParamList.Clear();
        }
        private void BeforeCall(int paramCount, List<(X64Operand srcOperand, int idx, GType type, bool isConstAddrSemantic, string? rokey)> tempParamInfos, out int rspSub, ref List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs)
        {
            rspSub = 0;
            homedRegs ??= new();
            homedRegs.Clear();
            {
                // 调用者保存寄存器（易失性寄存器需Caller保存） (选择性保存)
                var placeholderSave = X64.placehold("caller_save");
                var placeholderSaveNode = Emit(placeholderSave);

                if(callerSavePlaceHolders.ContainsKey(currFuncEnv) == false)
                    callerSavePlaceHolders.Add(currFuncEnv, new());
                callerSavePlaceHolders[currFuncEnv].Add(placeholderSaveNode);


                // 栈帧16字节对齐 (如果是奇数个参数 -> 需要8字节对齐栈指针)
                // (寄存器保存区自己确保16字节对齐)
                if(paramCount % 2 != 0)
                {
                    // 如果是奇数个参数，先将rsp对齐到16字节
                    rspSub += 8;
                }
                // 其他栈参数空间
                if(paramCount > 4)
                {
                    int onStackParamLen = (paramCount - 4) * 8;
                    rspSub += onStackParamLen;
                }
                // 影子空间(32字节)
                rspSub += 32;

                //移动rsp  
                Emit(X64.sub(X64.rsp, X64.imm(rspSub), X64Size.qword));
                //注释  
                instructions.Last.value.comment += $"    (shadow space and stack-params)";


                var callerParams = currFuncEnv.GetByCategory(SymbolTable.RecordCatagory.Param);
                if(callerParams != null)
                {
                    foreach(var callerParam in callerParams)
                    {
                        //正序的参数idx  
                        int trueParamIndex = callerParam.index;
                        var type = GType.Parse(callerParam.typeExpression);
                        if(trueParamIndex < 4)
                        {
                            var isSse = type.IsSSE;
                            //var size = paraminfo.oprand0.typeExpr.Size;
                            var ssereg = UtilsW64.GetParamReg(trueParamIndex, true);
                            var intreg = UtilsW64.GetParamReg(trueParamIndex, false);
                            if(isSse)
                            {
                                EmitMov(X64.mem(X64.rbp, disp: 16 + (8 * trueParamIndex)), ssereg, X64Size.qword, true);
                                homedRegs.Add((paramIdx: trueParamIndex, reg: ssereg, isSse: true));
                            }
                            else
                            {
                                EmitMov(X64.mem(X64.rbp, disp: 16 + (8 * trueParamIndex)), intreg, X64Size.qword, false);
                                homedRegs.Add((paramIdx: trueParamIndex, reg: intreg, isSse: false));
                            }
                        }
                    }
                }


                // 参数赋值(IR中PARAM指令已经是倒序)  
                foreach(var paraminfo in tempParamInfos)
                {
                    var srcOperand = paraminfo.srcOperand;
                    bool isConstAddrSemantic = paraminfo.isConstAddrSemantic;

                    int trueParamIndex = (tempParamInfos.Count - 1) - paraminfo.idx;

                    //寄存器传参  
                    if(trueParamIndex < 4)
                    {
                        var isSse = paraminfo.type.IsSSE;
                        var size = paraminfo.type.Size;
                        var ssereg = UtilsW64.GetParamReg(trueParamIndex, true);
                        var intreg = UtilsW64.GetParamReg(trueParamIndex, false);

                        //浮点参数  
                        if(isSse)
                        {
                            if(size == 4)
                                Emit(X64.movss(ssereg, srcOperand));
                            else
                                Emit(X64.movsd(ssereg, srcOperand));

                            //浮点参数也需要在整数寄存器rcx,rdx,r8,r9中，以支持可变参数  
                            Emit(X64.mov(intreg, srcOperand, (X64Size)size));
                        }
                        //整型参数  
                        else
                        {
                            if(isConstAddrSemantic)
                            {
                                //Emit(X64.lea(intreg, X64.rel(paraminfo.oprand0.expr.roDataKey)));
                                Emit(X64.lea(intreg, X64.rel(paraminfo.rokey)));
                            }
                            else
                            {
                                //Emit(X64.xor(intreg, intreg));

                                if(size >= 4)
                                {
                                    Emit(X64.mov(intreg, srcOperand, (X64Size)size));
                                }
                                else
                                {
                                    if(srcOperand is X64Immediate imm)
                                    {
                                        Emit(X64.mov(intreg, imm, X64Size.dword));
                                    }
                                    else
                                    {
                                        if(paraminfo.type.IsSigned)
                                            Emit(X64.movsx(intreg, srcOperand, X64Size.dword, (X64Size)size));
                                        else
                                            Emit(X64.movzx(intreg, srcOperand, X64Size.dword, (X64Size)size));
                                    }
                                }
                            }
                        }
                    }


                    //栈帧传参rsp开始 （影子空间也需要赋值）
                    int offset = 8 * (trueParamIndex);
                    if(isConstAddrSemantic)
                    {
                        using(new RegUsageRange(this, RegisterEnum.R11))
                        {
                            Emit(X64.lea(X64.r11, X64.rel(paraminfo.rokey)));
                            EmitMov(X64.mem(X64.rsp, disp: offset), X64.r11, paraminfo.type);
                        }
                    }
                    else
                    {
                        EmitMov(X64.mem(X64.rsp, disp: offset), srcOperand, paraminfo.type);
                    }
                }
            }
        }

        // Call调用后  
        private void AfterCall(int rspSub, List<(int paramIdx, X64Reg reg, bool isSse)> homedRegs)
        {
            // 恢复寄存器参数  
            foreach(var (paramIdx, reg, isSse) in homedRegs)
            {
                EmitMov(reg, X64.mem(X64.rbp, disp: 16 + (paramIdx * 8)), X64Size.qword, isSse);
            }


            // 调用后完整恢复栈
            Emit(X64.add(X64.rsp, X64.imm(rspSub), X64Size.qword));

            //注释  
            instructions.Last.value.comment += $"    (release shadow space and stack-params)";

            //还原保存的寄存器(占位)  
            var placeholderRestore = X64.placehold("caller_restore");
            var placeholderRestoreNode = Emit(placeholderRestore);

            if(callerRestorePlaceHolders.ContainsKey(currFuncEnv) == false)
                callerRestorePlaceHolders.Add(currFuncEnv, new());
            callerRestorePlaceHolders[currFuncEnv].Add(placeholderRestoreNode);
        }

        // 指令附加信息  
        public InstructionAdditionalInfo GetInstructionInfo(X64Instruction instr)
        {
            if(instuctonAdditionalInfos.TryGetValue(instr, out var inf))
            {
                return inf;
            }
            instuctonAdditionalInfos[instr] = new InstructionAdditionalInfo();
            return instuctonAdditionalInfos[instr];
        }

        // 有寄存器占用  
        public bool HasRegUsageAt(X64Instruction instr)
        {
            if(instuctonAdditionalInfos.TryGetValue(instr, out var inf) == false)
            {
                return false;
            }
            return inf.regUsages.Count > 0;
        }

        public RegisterEnum TryGetIdleScratchReg(InstructionNode nodeL, InstructionNode nodeR, bool isSSE, RegisterEnum regPrefer = RegisterEnum.Undefined)
        {
            if(nodeL.Next != nodeR)
                throw new GizboxException(ExceptioName.Undefine, "not support.");

            if(HasRegUsageAt(nodeL.value) && HasRegUsageAt(nodeR.value))
            {
                var usageList1 = GetInstructionInfo(nodeL.value).regUsages;
                var usageList2 = GetInstructionInfo(nodeR.value).regUsages;

                var registers = isSSE ? tempRegistersSSE : tempRegistersGP;

                foreach(var reg in registers)
                {
                    if(usageList1.Contains(reg) && usageList2.Contains(reg))
                        continue;

                    return reg;
                }
                throw new GizboxException(ExceptioName.CodeGen, "no temp register available.");
            }
            else
            {
                if(isSSE)
                    return RegisterEnum.XMM0;
                else
                    return RegisterEnum.R11;
            }
        }

        public void UseScratchRegister(InstructionNode nodeL, InstructionNode nodeR, RegisterEnum reg)
        {
            var curr = nodeL;
            while(curr != null)
            {
                var usagelist = GetInstructionInfo(curr.value).regUsages;
                if(usagelist.Contains(reg))
                    throw new GizboxException(ExceptioName.CodeGen, "exist repeat reg element in usage list.");
                usagelist.Add(reg);

                if(curr == nodeR)
                    break;
                curr = curr.Next;

                if(curr == null)
                    throw new GizboxException(ExceptioName.CodeGen, "instruction order error.");
            }
        }

        private InstructionNode Emit(X64Instruction insn)
        {
            var newinsn = instructions.AddLast(insn);

            foreach(var (regEnum, regBorrowRange) in currBorrowRegisters)
            {
                GetInstructionInfo(newinsn.value).regUsages.Add(regEnum);
            }

            return newinsn;
        }

        private InstructionNode InsertAfter(InstructionNode targetNode, X64Instruction newInstruction)
        {
            var nodeL = targetNode;
            var nodeR = targetNode.Next;

            var newNode = instructions.InsertAfter(targetNode, newInstruction);

            if(nodeR == null)
                return newNode;
            if(HasRegUsageAt(nodeL.value) && HasRegUsageAt(nodeR.value))
            {
                var newNodeInfo = GetInstructionInfo(newNode.value);

                var usageList1 = GetInstructionInfo(nodeL.value).regUsages;
                var usageList2 = GetInstructionInfo(nodeR.value).regUsages;
                foreach(var usage in usageList1)
                {
                    if(usageList2.Contains(usage) == false)
                        continue;
    
                    newNodeInfo.regUsages.Add(usage);
                }
            }
            return newNode;
        }

        private InstructionNode InsertBefore(InstructionNode targetNode, X64Instruction newInstruction)
        {
            var nodeL = targetNode.Prev;
            var nodeR = targetNode;

            var newNode = instructions.InsertBefore(targetNode, newInstruction);

            if(nodeL == null)
                return newNode;
            if(HasRegUsageAt(nodeL.value) && HasRegUsageAt(nodeR.value))
            {
                var newNodeInfo = GetInstructionInfo(newNode.value);

                var usageList1 = GetInstructionInfo(nodeL.value).regUsages;
                var usageList2 = GetInstructionInfo(nodeR.value).regUsages;
                foreach(var usage in usageList1)
                {
                    if(usageList2.Contains(usage) == false)
                        continue;
    
                    newNodeInfo.regUsages.Add(usage);
                }
            }
            return newNode;
        }

        // Mov  
        private void EmitMov(X64Operand dst, X64Operand src, GType type)
        {
            EmitMov(dst, src, (X64Size)type.Size, type.IsSSE);
        }
        private void EmitMov(X64Operand dst, X64Operand src, X64Size size, bool isSSE)
        {
            bool IsRegOrVReg(X64Operand op) => op is X64Reg;

            if(isSSE)
            {
                bool f32 = ((int)size == 4);
                if(IsRegOrVReg(dst) == false && IsRegOrVReg(src) == false)
                {
                    if(f32)
                    {
                        using(new RegUsageRange(this, RegisterEnum.XMM0))
                        {
                            Emit(X64.movss(X64.xmm0, src));
                            Emit(X64.movss(dst, X64.xmm0));
                        }
                    }
                    else
                    {
                        using(new RegUsageRange(this, RegisterEnum.XMM0))
                        {
                            Emit(X64.movsd(X64.xmm0, src));
                            Emit(X64.movsd(dst, X64.xmm0));
                        }
                    }
                }
                else
                {
                    Emit(f32 ? X64.movss(dst, src) : X64.movsd(dst, src));
                }
            }
            else
            {
                if(IsRegOrVReg(dst) == false && IsRegOrVReg(src) == false)
                {
                    using(new RegUsageRange(this, RegisterEnum.R11))
                    {
                        Emit(X64.mov(X64.r11, src, size));
                        Emit(X64.mov(dst, X64.r11, size));
                    }
                }
                else
                {
                    Emit(X64.mov(dst, src, size));
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
                        Emit(SseOp(dst, b));
                    }
                    else
                    {
                        // dst <- a; dst = dst op b
                        Emit(f32 ? X64.movss(dst, a) : X64.movsd(dst, a));
                        Emit(SseOp(dst, b));
                    }
                }
                else
                {
                    // dst(内存) = (a op b) via xmm0
                    using(new RegUsageRange(this, RegisterEnum.XMM0))
                    {
                        if(f32)
                            Emit(X64.movss(X64.xmm0, a));
                        else
                            Emit(X64.movsd(X64.xmm0, a));
                        Emit(SseOp(X64.xmm0, b));
                        EmitMov(dst, X64.xmm0, type);
                    }
                }
            }
            else
            {
                X64Size size = (X64Size)type.Size;

                // 整数/指针
                if(op == "/" || op == "%")
                {
                    // rdx:rax / b
                    Emit(X64.mov(X64.rax, a, size));
                    Emit(X64.cqo());
                    Emit(X64.idiv(b, size));
                    EmitMov(dst, op == "/" ? (X64Operand)X64.rax : X64.rdx, type);
                    return;
                }

                X64Instruction IntOp(string o, X64Operand d, X64Operand s)
                {
                    switch(o)
                    {
                        case "+":
                            return X64.add(d, s, size);
                        case "-":
                            return X64.sub(d, s, size);
                        case "*":
                            return X64.imul_2(d, s, size);
                    }
                    throw new GizboxException(ExceptioName.CodeGen, $"未知操作符: {o}");
                }

                if(IsRegOrVReg(dst))
                {
                    if(Equals(dst, a))
                    {
                        Emit(IntOp(op, dst, b));
                    }
                    else
                    {
                        // dst <- a; dst = dst op b
                        Emit(X64.mov(dst, a, size));
                        Emit(IntOp(op, dst, b));
                    }
                }
                else
                {
                    // dst(内存) = (a op b) via r11
                    using(new RegUsageRange(this, RegisterEnum.R11))
                    {
                        Emit(X64.mov(X64.r11, a, size));
                        Emit(IntOp(op, X64.r11, b));
                        EmitMov(dst, X64.r11, type);
                    }
                }
            }
        }

        // 比较运算  
        private void EmitCompare(TACInfo tacInf, string op)
        {
            var boolDst = ParseOperand(tacInf.oprand0); // 布尔目标
            var a = ParseOperand(tacInf.oprand1);
            var b = ParseOperand(tacInf.oprand2);
            var aType = tacInf.oprand1.typeExpr;
            bool isSse = tacInf.oprand1.IsSSEType();

            if(isSse)
            {
                bool isF32 = aType.Size == 4;

                // 需要第一个操作数在 XMM 寄存器
                using(new RegUsageRange(this, RegisterEnum.XMM0))
                {

                    X64Operand aReg = a;
                    if(a is X64Reg r && r.isVirtual == false && r.IsXXM() == false)
                    {
                        throw new GizboxException(ExceptioName.CodeGen, "compare lhs cant be gpr!");
                    }


                    // imm或其它 -> 加载
                    if(!(a is X64Reg) && !(a is X64Mem) && !(a is X64Rel))
                    {
                        using(new RegUsageRange(this, RegisterEnum.XMM0))
                        {
                            if(isF32)
                                Emit(X64.movss(X64.xmm0, a));
                            else
                                Emit(X64.movsd(X64.xmm0, a));
                            aReg = X64.xmm0;
                        }
                    }
                    // mem操作数 -> 加载  
                    else if(a is X64Mem || a is X64Rel)
                    {
                        if(isF32)
                            Emit(X64.movss(X64.xmm0, a));
                        else
                            Emit(X64.movsd(X64.xmm0, a));
                        aReg = X64.xmm0;
                    }

                    // 第二操作数可为 reg/mem
                    if(isF32)
                        Emit(X64.ucomiss(aReg, b));
                    else
                        Emit(X64.ucomisd(aReg, b));
                }

                // 暂不处理 NaN（unordered）细节，默认与普通有序比较一致（TODO）
                switch(op)
                {
                    case "<": Emit(X64.setb(boolDst)); break;   // CF=1 && ZF=0
                    case "<=": Emit(X64.setbe(boolDst)); break; // CF=1 || ZF=1
                    case ">": Emit(X64.seta(boolDst)); break;   // CF=0 && ZF=0
                    case ">=": Emit(X64.setae(boolDst)); break; // CF=0
                    case "==": Emit(X64.sete(boolDst)); break;  // ZF=1 (NaN 会产生 ZF=1/PF=1 -> 未区分)
                    case "!=": Emit(X64.setne(boolDst)); break; // ZF=0
                }
                //把低8位结果零扩展到64位
                if(boolDst is not X64Reg)
                {
                    using(new RegUsageRange(this, RegisterEnum.R11))
                    {
                        Emit(X64.mov(X64.r11, boolDst, X64Size.@byte));
                        Emit(X64.movzx(X64.r11, X64.r11, X64Size.qword, X64Size.@byte));
                        Emit(X64.mov(boolDst, X64.r11, X64Size.qword));
                    }
                }
                else
                {
                    Emit(X64.movzx(boolDst, boolDst, X64Size.qword, X64Size.@byte));
                }
                
                return;
            }
            // 整数/指针比较
            else
            {
                // cmp 要求第一个操作数不可为立即数；若 a 是立即数使用 r11 中转
                bool aIsImm = a is X64Immediate;
                if(aIsImm)
                {
                    using(new RegUsageRange(this, RegisterEnum.R11))
                    {
                        Emit(X64.mov(X64.r11, a, (X64Size)aType.Size));
                        Emit(X64.cmp(X64.r11, b));
                    }
                }
                else
                {
                    // 直接 cmp a,b（mem-mem 会在后续合法化阶段修正）
                    Emit(X64.cmp(a, b));
                }

                switch(op)
                {
                    case "<":
                        Emit(X64.setl(boolDst));
                        break;
                    case "<=":
                        Emit(X64.setle(boolDst));
                        break;
                    case ">":
                        Emit(X64.setg(boolDst));
                        break;
                    case ">=":
                        Emit(X64.setge(boolDst));
                        break;
                    case "==":
                        Emit(X64.sete(boolDst));
                        break;
                    case "!=":
                        Emit(X64.setne(boolDst));
                        break;
                }
            }
        }
        // 复合赋值
        private void EmitCompoundAssign(X64Operand dst, X64Operand rhs, GType type, string op)
        {
            if(type.IsSSE)
            {
                bool f32 = type.Size == 4;

                using(new RegUsageRange(this, RegisterEnum.XMM0))
                {

                    if(f32)
                        Emit(X64.movss(X64.xmm0, dst));
                    else
                        Emit(X64.movsd(X64.xmm0, dst));

                    switch(op)
                    {
                        case "+=":
                            if(f32)
                                Emit(X64.addss(X64.xmm0, rhs));
                            else
                                Emit(X64.addsd(X64.xmm0, rhs));
                            break;
                        case "-=":
                            if(f32)
                                Emit(X64.subss(X64.xmm0, rhs));
                            else
                                Emit(X64.subsd(X64.xmm0, rhs));
                            break;
                        case "*=":
                            if(f32)
                                Emit(X64.mulss(X64.xmm0, rhs));
                            else
                                Emit(X64.mulsd(X64.xmm0, rhs));
                            break;
                        case "/=":
                            if(f32)
                                Emit(X64.divss(X64.xmm0, rhs));
                            else
                                Emit(X64.divsd(X64.xmm0, rhs));
                            break;
                        case "%=":
                            throw new GizboxException(ExceptioName.CodeGen, "浮点数不支持%");
                        default:
                            throw new GizboxException(ExceptioName.CodeGen, $"未知复合操作符: {op}");
                    }
                    EmitMov(dst, X64.xmm0, type);
                }
            }
            else
            {
                X64Size size = (X64Size)type.Size;
                switch(op)
                {
                    case "+=":
                    case "-=":
                    case "*=":
                        {
                            using(new RegUsageRange(this, RegisterEnum.R11))
                            {
                                Emit(X64.mov(X64.r11, dst, size));
                                if(op == "+=")
                                    Emit(X64.add(X64.r11, rhs, size));
                                else if(op == "-=")
                                    Emit(X64.sub(X64.r11, rhs, size));
                                else
                                    Emit(X64.imul_2(X64.r11, rhs, size));
                                EmitMov(dst, X64.r11, type);
                            }
                            break;
                        }
                    case "/=":
                        {
                            using(new RegUsageRange(this, RegisterEnum.RAX))
                            {
                                Emit(X64.mov(X64.rax, dst, size));
                                Emit(X64.cqo());
                                Emit(X64.idiv(rhs, size));
                                EmitMov(dst, X64.rax, type);
                                break;
                            }
                        }
                    case "%=":
                        {
                            using(new RegUsageRange(this, RegisterEnum.RAX))
                            {
                                Emit(X64.mov(X64.rax, dst, size));
                                Emit(X64.cqo());
                                using(new RegUsageRange(this, RegisterEnum.RDX))
                                {
                                    Emit(X64.idiv(rhs, size));
                                    EmitMov(dst, X64.rdx, type);
                                }
                            }
                            break;
                        }
                    default:
                        throw new GizboxException(ExceptioName.CodeGen, $"未知复合操作符: {op}");
                }
            }
        }
        #endregion


        #region PASS4

        // 检查 栈参数/全局变量 的虚拟寄存器使用  
        private void CheckGlobalVarAndStackParamVRegUse()
        {
            var curr = instructions.First;
            X64Operand[] oprands = new X64Operand[2];
            while(curr != null)
            {
                oprands[0] = curr.value.operand0;
                oprands[1] = curr.value.operand1;

                for(int i = 0; i < 2; ++i)
                {
                    var targetOperand = oprands[i];
                    if(targetOperand is X64Reg reg && reg.isVirtual)
                    {
                        var vartype = GType.Parse(reg.vRegVar.typeExpression);

                        if(reg.vRegVar.category == SymbolTable.RecordCatagory.Param)
                        {
                            var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, vartype.IsSSE);
                            reg.AllocPhysReg(scratchReg);
                            var newinsn = InsertBefore(curr, X64.mov(new X64Reg(scratchReg), X64.mem(X64.rbp, disp: (reg.vRegVar.addr)), X64Size.qword));
                            UseScratchRegister(newinsn, curr, scratchReg);
                        }
                        else if(reg.vRegVar.category == SymbolTable.RecordCatagory.Variable && reg.vRegVar.GetAdditionInf().isGlobal)
                        {
                            var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, vartype.IsSSE);
                            reg.AllocPhysReg(scratchReg);

                            InstructionNode newinsn;
                            if(UtilsW64.IsConstAddrSemantic(reg.vRegVar))
                            {
                                newinsn = InsertBefore(curr, X64.lea(new X64Reg(scratchReg), X64.rel(reg.vRegVar.name)));
                            }
                            else
                            {
                                newinsn = InsertBefore(curr, X64.mov(new X64Reg(scratchReg), X64.rel(reg.vRegVar.name), (X64Size)vartype.Size));
                            }
                            UseScratchRegister(newinsn, curr, scratchReg);
                        }
                    }
                    if(targetOperand is X64Mem xMem)
                    {
                        if(xMem.baseReg != null && xMem.baseReg.isVirtual)
                        {
                            var baseregVarType = GType.Parse(xMem.baseReg.vRegVar.typeExpression);

                            if(xMem.baseReg.vRegVar.category == SymbolTable.RecordCatagory.Param)
                            {
                                var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, isSSE:false, regPrefer:RegisterEnum.R11);
                                xMem.baseReg.AllocPhysReg(scratchReg);
                                InstructionNode newinsn;
                                if(UtilsW64.IsConstAddrSemantic(xMem.baseReg.vRegVar))//通常不会出现这种情况
                                {
                                    newinsn = InsertBefore(curr, X64.lea(new X64Reg(scratchReg), X64.mem(X64.rbp, disp: (xMem.baseReg.vRegVar.addr))));
                                }
                                else
                                {
                                    newinsn = InsertBefore(curr, X64.mov(new X64Reg(scratchReg), X64.mem(X64.rbp, disp: (xMem.baseReg.vRegVar.addr)), X64Size.qword));
                                }
                                UseScratchRegister(newinsn, curr, scratchReg);
                            }
                            else if(xMem.baseReg.vRegVar.category == SymbolTable.RecordCatagory.Variable && xMem.baseReg.vRegVar.GetAdditionInf().isGlobal)
                            {
                                var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, isSSE: false, regPrefer:RegisterEnum.R11);
                                xMem.baseReg.AllocPhysReg(scratchReg);

                                InstructionNode newinsn;
                                if(UtilsW64.IsConstAddrSemantic(xMem.baseReg.vRegVar))//通常不会出现这种情况
                                {
                                    newinsn = InsertBefore(curr, X64.lea(new X64Reg(scratchReg), X64.rel(xMem.baseReg.vRegVar.name)));
                                }
                                else
                                {
                                    newinsn = InsertBefore(curr, X64.mov(new X64Reg(scratchReg), X64.rel(xMem.baseReg.vRegVar.name), X64Size.qword));
                                }
                                    
                                UseScratchRegister(newinsn, curr, scratchReg);
                            }
                        }
                        if(xMem.indexReg != null && xMem.indexReg.isVirtual)
                        {
                            if(xMem.indexReg.vRegVar.category == SymbolTable.RecordCatagory.Param)
                            {
                                var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, isSSE:false, regPrefer:RegisterEnum.R10);
                                xMem.indexReg.AllocPhysReg(scratchReg);
                                var newinsn = InsertBefore(curr, X64.mov(new X64Reg(scratchReg), X64.mem(X64.rbp, disp: (xMem.indexReg.vRegVar.addr)), X64Size.qword));
                                UseScratchRegister(newinsn, curr, scratchReg);
                            }
                            else if(xMem.indexReg.vRegVar.category == SymbolTable.RecordCatagory.Variable && xMem.indexReg.vRegVar.GetAdditionInf().isGlobal)
                            {
                                var scratchReg = TryGetIdleScratchReg(curr.Prev, curr, isSSE: false, regPrefer:RegisterEnum.R10);
                                xMem.indexReg.AllocPhysReg(scratchReg);
                                var newinsn = InsertBefore(curr, X64.lea(new X64Reg(scratchReg), X64.rel(xMem.indexReg.vRegVar.name)));
                                UseScratchRegister(newinsn, curr, scratchReg);
                            }
                        }
                    }
                }

                curr = curr.Next;
            }
        }

        // mem-mem 合法化
        private void LegalizeMemToMem()
        {
            var node = instructions.First;
            while(node != null)
            {
                var instr = node.value;

                var size = instr.sizeMark;

                switch(instr.type)
                {
                    // 普通 mov：不允许 mem-mem
                    case InstructionKind.mov:
                        if(UtilsW64.IsMemOperand(instr.operand0) && UtilsW64.IsMemOperand(instr.operand1))
                        {
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:false);
                            var newinsn = InsertBefore(node, X64.mov(new X64Reg(scratch), instr.operand1, size));
                            instr.operand1 = new X64Reg(scratch);
                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // 浮点/128bit 搬运：不允许 mem-mem
                    case InstructionKind.movss:
                    case InstructionKind.movsd:
                    case InstructionKind.movaps:
                    case InstructionKind.movapd:
                    case InstructionKind.movups:
                    case InstructionKind.movupd:
                    case InstructionKind.movdqa:
                    case InstructionKind.movdqu:
                        if(UtilsW64.IsMemOperand(instr.operand0) && UtilsW64.IsMemOperand(instr.operand1))
                        {
                            if(instr.type == InstructionKind.movss)
                            {
                                var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:true);
                                var newinsn = InsertBefore(node, X64.movss(new X64Reg(scratch), instr.operand1));
                                instr.operand1 = new X64Reg(scratch);
                                UseScratchRegister(newinsn, node, scratch);
                            }
                            else if(instr.type == InstructionKind.movsd)
                            {
                                var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE: true);
                                var newinsn = InsertBefore(node, X64.movsd(new X64Reg(scratch), instr.operand1));
                                instr.operand1 = new X64Reg(scratch);
                                UseScratchRegister(newinsn, node, scratch);
                            }
                            else
                            {
                                // 统一用 movdqu 做中转加载（无对齐要求）
                                var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE: true);
                                var newinsn = InsertBefore(node, X64.movdqu(new X64Reg(scratch), instr.operand1));
                                instr.operand1 = new X64Reg(scratch);
                                UseScratchRegister(newinsn, node, scratch);
                            }
                        }
                        break;

                    // 整数二元：不允许 mem-mem
                    case InstructionKind.add:
                    case InstructionKind.sub:
                    case InstructionKind.and:
                    case InstructionKind.or:
                    case InstructionKind.xor:
                        if(UtilsW64.IsMemOperand(instr.operand0) && UtilsW64.IsMemOperand(instr.operand1))
                        {
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:false);
                            var newinsn = InsertBefore(node, X64.mov(new X64Reg(scratch), instr.operand1, size));
                            instr.operand1 = new X64Reg(scratch);
                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // imul（二操作数形式）：目的必须是寄存器
                    case InstructionKind.imul:
                        {
                            bool dstIsMem = UtilsW64.IsMemOperand(instr.operand0);
                            bool srcIsMem = UtilsW64.IsMemOperand(instr.operand1);
                            X64Operand origDst = instr.operand0;

                            RegisterEnum dstScratch = default;
                            if(dstIsMem)
                            {
                                dstScratch = TryGetIdleScratchReg(node.Prev, node, isSSE: false, regPrefer:RegisterEnum.R11);
                                var insn = InsertBefore(node, X64.mov(new X64Reg(dstScratch), instr.operand0, size));
                                instr.operand0 = new X64Reg(dstScratch);
                                UseScratchRegister(insn, node, dstScratch);
                            }
                            if(srcIsMem)
                            {
                                // 若目的已占用 R11，则用 R10 做第二中转
                                var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE: false);
                                var newinsn = InsertBefore(node, X64.mov(new X64Reg(scratch), instr.operand1, size));
                                instr.operand1 = new X64Reg(scratch);
                                UseScratchRegister(newinsn, node, scratch);
                            }
                            if(dstIsMem)
                            {
                                InsertAfter(node, X64.mov(origDst, new X64Reg(dstScratch), size));//无需标记区间，InserBefore自动标记
                            }
                            break;
                        }

                    // SSE 标量算术：目的必须是 XMM
                    case InstructionKind.addss:
                    case InstructionKind.subss:
                    case InstructionKind.mulss:
                    case InstructionKind.divss:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var dst = instr.operand0;
                            var scratch = TryGetIdleScratchReg(node.Prev, node.Next, isSSE:true);
                            var newinsn1 = InsertBefore(node, X64.movss(new X64Reg(scratch), dst));
                            instr.operand0 = new X64Reg(scratch);
                            var newinsn2 = InsertAfter(node, X64.movss(dst, new X64Reg(scratch)));
                            UseScratchRegister(newinsn1, newinsn2, scratch);
                        }
                        break;
                    case InstructionKind.addsd:
                    case InstructionKind.subsd:
                    case InstructionKind.mulsd:
                    case InstructionKind.divsd:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var dst = instr.operand0;
                            var scratch = TryGetIdleScratchReg(node.Prev, node.Next, isSSE: true);
                            var newinsn1 = InsertBefore(node, X64.movsd(new X64Reg(scratch), dst));
                            instr.operand0 = new X64Reg(scratch);
                            var newinsn2 = InsertAfter(node, X64.movsd(dst, new X64Reg(scratch)));
                            UseScratchRegister(newinsn1, newinsn2, scratch);
                        }
                        break;

                    // 比较/测试：不允许 mem-mem
                    case InstructionKind.cmp:
                    case InstructionKind.test:
                        if(UtilsW64.IsMemOperand(instr.operand0) && UtilsW64.IsMemOperand(instr.operand1))
                        {
                            // 保持 op0 为原内存，载入 op1 到 R11
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:false);
                            var newinsn = InsertBefore(node, X64.mov(new X64Reg(scratch), instr.operand1, size));
                            instr.operand1 = new X64Reg(scratch);
                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // lea：目的必须是寄存器
                    case InstructionKind.lea:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE: false);
                            var newinsn = InsertBefore(node, X64.lea(new X64Reg(scratch), instr.operand1));
                            // 当前指令转为 mov [mem], r11
                            instr.type = InstructionKind.mov;
                            instr.operand1 = new X64Reg(scratch);
                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // movzx/movsx：目的必须是寄存器
                    case InstructionKind.movzx:
                    case InstructionKind.movsx:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var sizeOprand0 = instr.sizeMark;
                            var sizeOprand1 = instr.sizeMarkSrc;

                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:false);
                            var newinsn = InsertBefore(node, instr.type == InstructionKind.movzx 
                                ? X64.movzx(new X64Reg(scratch), instr.operand1, sizeOprand0, sizeOprand1) 
                                : X64.movsx(new X64Reg(scratch), instr.operand1, sizeOprand0, sizeOprand1));
                            instr.type = InstructionKind.mov;
                            instr.operand1 = new X64Reg(scratch);
                            UseScratchRegister(newinsn, node, scratch);

                            //movzx、movsx 目的操作数必须是寄存器，源操作数可以是寄存器或内存  
                            if(instr.operand0 is X64Immediate imm)
                            {
                                var scratch2 = TryGetIdleScratchReg(newinsn.Prev, newinsn, isSSE: false);
                                var newinsn2 = InsertBefore(newinsn, X64.mov(new X64Reg(scratch2), instr.operand0, sizeOprand0));
                                newinsn.value.operand1 = new X64Reg(scratch2);
                                UseScratchRegister(newinsn2, newinsn, scratch2);
                            }

                            //隐含size已改变
                            instr.sizeMarkSrc = instr.sizeMark = sizeOprand0;
                        }
                        break;

                    // 整数->浮点：目的必须 XMM
                    case InstructionKind.cvtsi2ss:
                    case InstructionKind.cvtsi2sd:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            // 先算到 xmm0，再存回
                            var intsize = instr.sizeMarkSrc;
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:true);
                            InstructionNode newinsn;

                            if(instr.type == InstructionKind.cvtsi2ss)
                                newinsn = InsertBefore(node, X64.cvtsi2ss(new X64Reg(scratch), instr.operand1, srcIntSize: intsize));
                            else
                                newinsn = InsertBefore(node, X64.cvtsi2sd(new X64Reg(scratch), instr.operand1, srcIntSize: intsize));

                            instr.type = (instr.type == InstructionKind.cvtsi2ss) ? InstructionKind.movss : InstructionKind.movsd;
                            instr.operand1 = new X64Reg(scratch);

                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // 浮点<->浮点：目的必须 XMM
                    case InstructionKind.cvtss2sd:
                    case InstructionKind.cvtsd2ss:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE: true);
                            InstructionNode newinsn;

                            var t = instr.type;
                            if(t == InstructionKind.cvtss2sd)
                                newinsn = InsertBefore(node, X64.cvtss2sd(new X64Reg(scratch), instr.operand1));
                            else
                                newinsn = InsertBefore(node, X64.cvtsd2ss(new X64Reg(scratch), instr.operand1));

                            instr.type = (t == InstructionKind.cvtss2sd) ? InstructionKind.movsd : InstructionKind.movss;
                            instr.operand1 = new X64Reg(scratch);

                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;

                    // 浮点->整数：目的必须 GP
                    case InstructionKind.cvttss2si:
                    case InstructionKind.cvttsd2si:
                        if(UtilsW64.IsMemOperand(instr.operand0))
                        {
                            var t = instr.type;
                            var scratch = TryGetIdleScratchReg(node.Prev, node, isSSE:true);
                            var newinsn = InsertBefore(node, X64.cvttss2si(new X64Reg(scratch), instr.operand1, instr.sizeMark));
                            instr.type = InstructionKind.mov;
                            instr.operand1 = new X64Reg(scratch);

                            UseScratchRegister(newinsn, node, scratch);
                        }
                        break;
                }

                node = node.Next;
            }

            instructions.Rebuild();
        }
        
        // mov 合法化  
        private void LegalizeMov()
        {
            var node = instructions.First;
            while(node != null)
            {
                var instr = node.value;

                var size = instr.sizeMark;

                switch(instr.type)
                {
                    case InstructionKind.mov:
                        {
                            bool oprand0IsXXM = (instr.operand0 is X64Reg reg0 && reg0.IsXXM());
                            bool oprand1IsXXM = (instr.operand1 is X64Reg reg1 && reg1.IsXXM());

                            if(oprand0IsXXM != oprand1IsXXM)
                            {
                                if(instr.sizeMark == X64Size.dword)
                                {
                                    instr.type = InstructionKind.movd;
                                }
                                else if(instr.sizeMark == X64Size.qword)
                                {
                                    instr.type = InstructionKind.movq;
                                }
                            }

                            if(oprand0IsXXM == true && oprand1IsXXM == true)
                            {
                                if(instr.sizeMark == X64Size.dword)
                                {
                                    instr.type = InstructionKind.movss;
                                }
                                else if(instr.sizeMark == X64Size.qword)
                                {
                                    instr.type = InstructionKind.movsd;
                                }
                            }
                        }
                        break;
                }

                node = node.Next;
            }

            //instructions.Rebuild();//没有其他指令插入   
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
        

        public (int index, VTable.Record rec) QueryVTable(string cname, string fname)
        {
            foreach(var unit in allunits)
            {
                if(unit.vtables.TryGetValue(cname, out var table))
                {
                    return table.Query(fname);
                }
            }
            return default;
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




        public class RegUsageRange : IDisposable
        {
            private Win64CodeGenContext context;
            public RegisterEnum regUsed { get; private set; }
            public RegUsageRange(Win64CodeGenContext context, RegisterEnum regUsed)
            {
                this.context = context;
                this.regUsed = regUsed;
                context.currBorrowRegisters.Add(this.regUsed, this);
            }
            public void Dispose()
            {
                context.currBorrowRegisters.Remove(this.regUsed);
            }
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
        public IROperand CTOR_CALL_TargetObject;//对象参数（如果是构造函数调用指令）
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
                
                if(funcRec.name.EndsWith(".ctor"))
                {
                    if(context.externFuncs.Contains(funcRec.name) == false)
                        context.externFuncs.Add(funcRec.name);
                }
                if(funcRec.GetAdditionInf().table != context.ir.globalScope.env)
                {
                    if(context.externFuncs.Contains(funcRec.name) == false)
                        context.externFuncs.Add(funcRec.name);
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
                        context.externVars.Add(rec.name);
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
                        //var objClass = owner.MCALL_methodTargetObject.typeExpr;
                        //rec = context.QueryMember(objClass.ToString(), segments[0]);
                        //segmentRecs[0] = rec;
                        //typeExpr = GType.Parse(rec.typeExpression);
                        //if(rec == null)
                        //    throw new GizboxException(ExceptioName.Undefine, "未找到构造函数");
                        //else
                        //    GixConsole.WriteLine("构造函数：" + rec.name);

                        string className = segments[0].Substring(0, segments[0].Length - 5);
                        var classRec = context.Query(className, owner.line);
                        segmentRecs[0] = classRec.envPtr.GetRecord(segments[0]);
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
                    else if(owner.tac.op == "EXTERN_IMPL")
                    {
                        typeExpr = GType.Parse("(other)");
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
        public bool IsConstAddrSemantic()
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

        public RecAdditionInfo(SymbolRecord rec)
        {
            target = rec;
        }
        public SymbolTable GetFunctionEnv()
        {
            var curr = table;
            while(curr != null)
            {
                if(curr.tableCatagory == SymbolTable.TableCatagory.FuncScope)
                    return curr;

                curr = curr.parent;
            }

            throw new GizboxException(ExceptioName.CodeGen, $"function env of {target.name} not found. curr env:{table.name}");
        }
    }
    public class FunctionAdditionInfo
    {
        public SymbolTable.Record funcRec;
        public int irLineStart;
        public int irLineEnd;
    }

    public class InstructionAdditionalInfo
    {
        public X64Instruction target;
        public List<RegisterEnum> regUsages = new(2);//通常不会超过2个  
    }


    public class VRegDesc
    {
        public enum Kind
        {
            Oprand,
            OprandBase,
            OprandIndex,
        }

        public InstructionNode targetInstructionNode;

        public int oprandIdx;

        public Kind kind;

        public VRegDesc(InstructionNode targetInstructionNode, int oprandIdx, Kind kind)
        {
            this.targetInstructionNode = targetInstructionNode;
            this.oprandIdx = oprandIdx;
            this.kind = kind;
        }
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

        public static string ConvertLabel(string label)
        {
            if(label.StartsWith("entry"))
            {
                return label.Substring(6);
            }
            else if(label.StartsWith("exit"))
            {
                return label;
            }
            return label;
        }

        public static bool IsMemOperand(X64Operand op) 
            => op is X64Mem || op is X64Rel;

        public static string LegalizeName(string input)
        {
            if(input is null)
                return null;
            var result = input.Replace("::", "__");
            result = input.Replace(":", "_");
            return result;
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
                        StringBuilder strb = new();
                        foreach(char c in DecodeEscapes(gLiterial))
                        {
                            strb.Append(EncodeCharToNasmUnicode(c));
                            strb.Append(',');
                        }
                        strb.Append('0');
                        return strb.ToString();
                    }
                    break;
                case GType.Kind.Other:
                    {
                        if(gType.ToString() == "(FuncPtr)")
                        {
                            return LegalizeName(gLiterial);//label
                        }
                        throw new GizboxException(ExceptioName.Undefine, $"not support lit type:{gType.ToString()}");
                    }
                    break;
                default:
                    {

                    }
                    throw new GizboxException(ExceptioName.Undefine, $"not support lit type:{gType.ToString()}");
                    
            }
            return "";
        }

        public static string DecodeEscapes(string rawString)
        {
            if(string.IsNullOrEmpty(rawString))
                return string.Empty;

            int start = 0;
            int end = rawString.Length;

            // 去掉首尾引号（如果有）
            if(end - start >= 2 && rawString[start] == '"' && rawString[end - 1] == '"')
            {
                start++;
                end--;
            }

            StringBuilder strb = new StringBuilder(end - start);

            for(int i = start; i < end; i++)
            {
                char c = rawString[i];
                if(c != '\\')
                {
                    strb.Append(c);
                    continue;
                }

                // 处理转义
                if(i + 1 >= end)
                {
                    // 悬挂的反斜杠，按字面量输出
                    strb.Append('\\');
                    break;
                }

                char esc = rawString[++i];
                switch(esc)
                {
                    case 'n':
                        strb.Append('\n');
                        break;
                    case 'r':
                        strb.Append('\r');
                        break;
                    case 't':
                        strb.Append('\t');
                        break;
                    case 'b':
                        strb.Append('\b');
                        break;
                    case 'f':
                        strb.Append('\f');
                        break;
                    case 'v':
                        strb.Append('\v');
                        break;
                    case 'a':
                        strb.Append('\a');
                        break;
                    case '\\':
                        strb.Append('\\');
                        break;
                    case '"':
                        strb.Append('\"');
                        break;
                    case '\'':
                        strb.Append('\'');
                        break;
                    default:
                        {
                            strb.Append('?');
                            break;
                        }
                }
            }

            return strb.ToString();
        }

        public static string EncodeCharToNasmUnicode(char c)
        {
            switch(c)
            {
                case '\0':
                    return "0";   // NUL
                case '\n':
                    return "10";  // LF
                case '\r':
                    return "13";  // CR
                case '\t':
                    return "9";   // TAB
                case '\b':
                    return "8";   // BS
                case '\f':
                    return "12";  // FF
                case '\v':
                    return "11";  // VT
                case '\a':
                    return "7";   // BEL
                case '\\':
                    return "92";
                case '\'':
                    return "39";
                case '\"':
                    return "34";
            }

            if(c >= ' ' && c <= '~' && c != '\'')
                return $"'{c}'";

            return ((int)c).ToString();
        }

        public static string GetX64DefineType(GType gtype, string val)
        {
            X64DefSize x64DefineType = X64DefSize.undefined;
            switch(gtype.Category)
            {
                case GType.Kind.Void:
                case GType.Kind.Bool:
                    x64DefineType = (X64DefSize)1;
                    break;
                case GType.Kind.String://字符串常量  
                    x64DefineType = (X64DefSize)2;
                    break;
                case GType.Kind.Char://UNICODE char  
                    x64DefineType = (X64DefSize)2;
                    break;
                case GType.Kind.Int:
                case GType.Kind.Float:
                    x64DefineType = (X64DefSize)4;
                    break;
                case GType.Kind.Long:
                case GType.Kind.Double:
                    x64DefineType = (X64DefSize)8;
                    break;
                case GType.Kind.Array:
                default:
                    x64DefineType = (X64DefSize)8;//引用类型指针  
                    break;
            }

            string x64ValExpr = GLitToW64Lit(gtype, val);

            return $"{x64DefineType.ToString()}  {x64ValExpr}";
        }

        public static string GetX64ReserveDefineType(GType gtype)
        {
            X64ResSize resSize = X64ResSize.undefined;
            switch(gtype.Category)
            {
                case GType.Kind.Void:
                case GType.Kind.Bool:
                    resSize =(X64ResSize)1;
                    break;
                case GType.Kind.Char://字符串常量  
                    resSize = (X64ResSize)2;
                    break;
                case GType.Kind.Int:
                case GType.Kind.Float:
                    resSize = (X64ResSize)4;
                    break;
                case GType.Kind.Long:
                case GType.Kind.Double:
                    resSize = (X64ResSize)8;
                    break;
                case GType.Kind.Array://引用类型
                case GType.Kind.Object://引用类型
                case GType.Kind.String://非常量字符串
                    resSize = (X64ResSize)8;
                    break;
                default:
                    throw new GizboxException(ExceptioName.Undefine, $"not support .bss type:{gtype.ToString()}");
                    break;
            }

            return $"{resSize.ToString()} 1";
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

        public static bool IsConstAddrSemantic(SymbolRecord rec)// 地址即值的全局静态常量（目前只有字符串全局常量）（使用lea指令加载地址） 
        {
            if(rec.GetAdditionInf().isGlobal && GType.Parse(rec.typeExpression).Category == GType.Kind.String)
                return true;
            else
                return false;
        }
    }
}
