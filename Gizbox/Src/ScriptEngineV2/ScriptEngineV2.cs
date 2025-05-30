using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using Gizbox;
using Gizbox.IR;
using Gizbox.ScriptEngineV2;




///约定：（主要参考X64） 
///栈帧以16位对齐。  
///数组的对齐方式与数组中某个元素的对齐方式相同。
///结构或联合的开头的对齐方式是任何单个成员的最大对齐方式。结构或联合中的每个成员都必须按照上表中定义的正确对齐方式放置，这可能需要隐式内部填充，具体取决于前一个成员。
///结构大小必须是其对齐方式的整数倍，这可能需要在最后一个成员之后填充。由于结构和联合可以分组到数组中，因此结构或联合的每个数组元素必须以先前确定的正确对齐方式开始和结束。
///超过8字节的用引用传递。  
///前面几个参数使用寄存器传递，但是在堆栈上依然保留。    
///在堆栈上传递的所有参数都是 8 字节对齐的。（超过8字节仅传递指针）


///----------------------
///        参数2(8字节值或者指针)
///---------------------- 
///        参数1(8字节值或者指针)
///----------------------
///       返回地址
///----------------------  新栈帧开始位置
///        保存FP
///----------------------
///       局部变量1
///----------------------
///       局部变量2...
///----------------------


namespace Gizbox.ScriptEngineV2
{
    //栈帧  
    public unsafe class FrameInfo
    {
        public long startPtr;//高内存位  

        public FrameInfo(long startPtr)
        {
            this.startPtr = startPtr;
        }
        public void Reset()
        {
            this.startPtr = default;
        }
    }

    //调用堆栈  
    public unsafe class CallStack
    {
        public FrameInfo[] frameInfos;
        private int top;

        public CallStack(int frameMax)
        {
            frameInfos = new FrameInfo[frameMax];
            top = -1;
        }

        public FrameInfo Push(ref byte* currSP, ref byte* currFP, long retAddrLine)
        {
            if(top >= frameInfos.Length)
            {
                throw new GizboxException(ExceptioName.StackOverflow, "调用堆栈溢出，最大帧数：" + frameInfos.Length);
            }

            //上一帧末尾写入返回地址  
            Mem.write<long>(currSP, retAddrLine);

            //sp移动到栈起始位置
            currSP = currSP + 8;

            //旧的fp指针保存  
            Mem.write<long>(currSP, (long)currFP);

            //fp指针指向新的栈帧起始位置
            currFP = currSP;


            top++;
            if(frameInfos[top] == null)
            {
                //如果是第一次使用这个栈帧  
                frameInfos[top] = new FrameInfo((long)currSP);
            }
            else
            {
                //如果是复用这个栈帧  
                frameInfos[top].Reset();
                frameInfos[top].startPtr = (long)currSP;
            }


            return frameInfos[top];
        }
        public void Pop()
        {
            if(top <= 0)
                return;
            frameInfos[top].Reset();
            top--;
        }
    }


    public unsafe class ScriptEngineV2
    {
        //运行时单元  
        public RuntimeUnitV2 mainUnit;

        //调用堆栈  
        private CallStack callStack;


        //符号表堆栈  
        public GStack<SymbolTable> envStack;

        //帧指针  
        private byte* fp;

        //栈指针  
        private byte* sp;

        //返回值寄存  
        private readonly byte[] ret;

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
            byte* basePtr;
            long stackSize;
            Mem.Instance.GetStackBasePtrAndSize(out basePtr, out stackSize);
            sp = (byte*)MemUtility.AlignDown(((long)basePtr) + stackSize, 16);

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
                        //新的栈帧  
                        int low4 = curr;
                        int high4 = currUnit;
                        long ret = ((long)low4) | (((long)high4) << 32);
                        this.callStack.Push(ref sp, ret);
                    }
                    break;
                case "FUNC_END":
                    {
                        //存的上一个fp  
                        var old_fp = (byte*)Mem.read<long>(fp);

                        //返回地址  
                        var retAddr = Mem.read<long>(sp - 8);
                        int retUnit = (int)(retAddr >> 32);
                        int retLine = (int)(retAddr & 0xFFFFFFFF);

                        //还原fp
                        fp = old_fp;
                        //还原sp

                        //返回调用函数的指令地址
                        this.currUnit = retUnit;
                        this.curr = retLine;
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
