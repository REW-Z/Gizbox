using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using Gizbox;
using Gizbox.IR;
using Gizbox.ScriptEngineV2;



/// 调用者的栈操作步骤  
/// - **Step 1**: 计算结构体大小（含填充），并确保其满足对齐要求。
///   - 例如，结构体大小为20字节（需要8字节对齐），实际分配时会扩展为24字节（8的倍数）。
/// - **Step 2**: 调整栈指针（SP）以预留空间，并保证栈指针的值在压入参数前满足对齐。

/// 如果调用者（Caller）严格按照ABI规则处理对齐，被调函数（Callee）不需要再关心大结构体的对齐问题。被调函数可以假设调用者已正确对齐参数，并通过固定的偏移量访问参数。


namespace Gizbox.ScriptEngineV2
{
    //栈帧  
    public unsafe class Frame
    {
        public readonly long startPtr;//高内存位  
        public readonly long endPtr;//低内存位  

        public Frame(long startPtr, long size)
        {
            this.startPtr = startPtr;
            this.endPtr = this.startPtr + size - 1;
        }
    }

    //调用堆栈  
    public unsafe class CallStack
    {
        public Frame[] frames;


        public CallStack(int frameMax)
        {
            frames = new Frame[frameMax];
        }
    }

    
    public unsafe class ScriptEngineV2
    {
        //运行时单元  
        public RuntimeUnitV2 mainUnit;

        //模拟内存
        public SimMemory mem;

        //调用堆栈  
        private CallStack callStack;


        //符号表堆栈  
        public GStack<SymbolTable> envStack;


        //C#互操作上下文  
        public Gizbox.Interop.CSharp.InteropContext csharpInteropContext;


        //返回值寄存器（虚拟）(实际在x86架构通常为EAX寄存器 x86-64架构通常为RAX寄存器)
        private Value retRegister = Value.Void;

        //GC相关  
        private bool gcEnabled = false;

        //临时数据  
        private bool executing = false;
        private int currUnit = -1;
        private int curr = 0;
        private int prevUnit = -1;
        private int prev = 0;

        //DEBUG    
        private static bool analysisTime = true;
        private System.Diagnostics.Stopwatch analysisWatch = new System.Diagnostics.Stopwatch();
        private long prevTicks;
        private List<long> timeList = new List<long>(10000);
        private List<int> lineList = new List<int>(10000);





        public ScriptEngineV2()
        {
            mem = new SimMemory(10, 10);
            //csharpInteropContext = new Interop.CSharp.InteropContext(this);
        }

        public void Load(ILUnit ir)
        {
            Log("载入主程序");
            //运行时
            this.mainUnit = new RuntimeUnitV2(this, ir);
            this.mainUnit.MainUnitLinkLibs();


            Log("主程序的依赖链接完毕，总共：" + this.mainUnit.allUnits.Count);

            //调用堆栈  
            this.callStack = new CallStack(100);

            //入口
            this.curr = 0;

            //符号栈  
            this.envStack = this.mainUnit.GetEnvStack(-1, 0);
        }


        public void Execute(ILUnit ir)
        {
            Load(ir);

            Execute();
        }
        public void Execute()
        {
            if(this.mainUnit == null)
                throw new GizboxException(ExceptioName.NoInstructionsToExecute);

            //留空  
            Log("开始执行");
            Log(new string('\n', 20));

            executing = true;

            analysisWatch.Start();

            while((this.currUnit == -1 && this.curr >= mainUnit.codes.Count) == false)
            {
                if(this.currUnit == -1)
                {
                    Interpret(mainUnit.codes[this.curr]);
                }
                else
                {
                    Interpret(mainUnit.allUnits[this.currUnit].codes[this.curr]);
                }
            }

            analysisWatch.Stop();

            if(analysisTime == true)
            {
                if(Compiler.enableLogScriptEngine == false)
                {
                    Log(new string('\n', 3));
                    Log("总执行时间：" + analysisWatch.ElapsedMilliseconds + "ms");

                    Compiler.Pause("");
                    ProfilerLog();
                }
                else
                {

                    Log(new string('\n', 3));
                    Log("总执行时间：(由于开启log无法预估)");
                }
            }


            executing = false;
        }


        private void Interpret(RuntimeCodeV2 code)
        {
            //DEBUG信息  
            if(analysisTime)
            {
                //仅记录主模块的执行代码  
                if(prevUnit == -1)
                {
                    lineList.Add(prev);
                    timeList.Add(analysisWatch.ElapsedTicks - prevTicks);
                }
                prevTicks = analysisWatch.ElapsedTicks;
            }

            //GC  
            if(gcEnabled)
            {
                if(NeedGC())
                {
                    GCCollect();
                }
            }

            //设置符号表链（用于解析符号）  
            if(this.mainUnit.NeedResetStack(this.currUnit, this.curr, this.prevUnit, this.prev))
            {
                this.envStack = this.mainUnit.GetEnvStack(this.currUnit, this.curr);
            }
            prev = curr;
            prevUnit = currUnit;


            //开始指向本条指令  
            if(Compiler.enableLogScriptEngine)
                Log(new string('>', 10) + code.ToExpression(showlabel: false));


            switch(code.op)
            {
                case "":
                    break;
                case "JUMP":
                    {
                        var jaddr = mainUnit.QueryLabel(((OperandString)code.arg1).str, "", currUnit);
                        this.currUnit = jaddr.Item1;
                        this.curr = jaddr.Item2;
                        return;
                    }
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
                default: Log("未实现指令：" + code.ToExpression()); break;
            }

            this.curr++;
        }
        // ----------------------  Load Libs ---------------------------

        private List<string> libSearchDirectories = new List<string>();
        private List<ILUnit> libsCached = new List<ILUnit>();

        public void AddLibSearchDirectory(string dir)
        {
            this.libSearchDirectories.Add(dir);
        }

        public ILUnit LoadLib(string libname)
        {
            foreach(var libCache in libsCached)
            {
                if(libCache.name == libname)
                {
                    return libCache;
                }
            }
            if(this.libSearchDirectories.Count == 0)
                throw new GizboxException(ExceptioName.LibraryLoadPathNotSet);
            foreach(var dir in this.libSearchDirectories)
            {
                System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(dir);
                foreach(var f in dirInfo.GetFiles())
                {
                    if(System.IO.Path.GetFileNameWithoutExtension(f.Name) == libname)
                    {
                        if(System.IO.Path.GetExtension(f.Name).EndsWith("gixlib"))
                        {
                            var unit = Gizbox.IR.ILSerializer.Deserialize(f.FullName);
                            Log("导入库文件：" + f.Name + " 库名：" + unit.name);
                            return unit;
                        }
                    }
                }
            }

            throw new GizboxException(ExceptioName.LibraryFileNotFound, libname + ".gixlib");
        }

        // ----------------------- Interfaces ---------------------------

        //...

        // 

        //GC    
        public void GCEnable(bool enable)
        {
            this.gcEnabled = enable;
        }

        private bool NeedGC()
        {
            return false;
        }
        public void GCCollect()
        {
            if(gcEnabled == false)
                return;
        }


        // ----------------------- LOG ---------------------------

        private System.Diagnostics.Stopwatch profileW = new System.Diagnostics.Stopwatch();
        private string profileName = "";

        private static void Log(object content)
        {
            if(!Compiler.enableLogScriptEngine)
                return;
            GixConsole.LogLine("ScriptEngineV2 >>" + content);
        }

        //Profiler
        private void ProfilerLog()
        {
            Log(new string('\n', 3));
            Log(" ---- 性能剖析 ---- ");

            Dictionary<int, List<long>> lineToTicksList = new Dictionary<int, List<long>>();
            for(int i = 0; i < timeList.Count; ++i)
            {
                int line = lineList[i];
                long time = timeList[i];

                if(lineToTicksList.ContainsKey(line) == false)
                    lineToTicksList[line] = new List<long>(100);

                lineToTicksList[line].Add(time);
            }


            List<KeyValuePair<int, double>> line_Time_List = new List<KeyValuePair<int, double>>();
            foreach(var k in lineToTicksList.Keys)
            {
                var avg = lineToTicksList[k].Average();
                line_Time_List.Add(new KeyValuePair<int, double>(k, avg));
            }

            line_Time_List.Sort((kv1, kv2) => (int)kv1.Value - (int)kv2.Value);

            foreach(var kv in line_Time_List)
            {
                Log("line:" + kv.Key + "[" + mainUnit.codes[kv.Key].ToExpression(false) + "]" + "  avgTicks:" + kv.Value);
            }
        }

        private void ProfileBegin(string name)
        {
            profileName = name;
            profileW.Restart();
        }
        private void ProfileEnd()
        {
            profileW.Stop();
            Log(profileName + ": " + profileW.ElapsedTicks);
        }

    }
}
