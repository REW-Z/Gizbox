using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Gizbox;
using Gizbox.IL;
using Gizbox.Interop.CSharp;
using System.Runtime.CompilerServices;

namespace Gizbox.ScriptEngine
{
    //堆区  
    public class Heap
    {
        private List<object> data = new List<object>(100);

        public int HeapSize => data.Count;

        //对象数量统计  
        private int objCount = 0;

        //触发GC的阈值data    
        private int gcThreshold = 50;


        public object Read(long addr)
        {
            if (addr < 0 || addr >= data.Count) return null;
            return data[(int)addr];
        }

        public long Alloc(object obj)
        {
            for (int i = 0; i < data.Count; ++i)
            {
                if (data[i] == null)
                {
                    data[i] = obj;
                    objCount++;
                    return i;
                }
            }

            data.Add(obj);
            objCount++;
            return (data.Count - 1);
        }

        public object Write(long addr, object val)
        {
            if (addr < 0 || addr >= data.Count) throw new GizboxException(ExceptionType.InvalidHeapWrite);

            if (data[(int)addr] == null && val != null)//写入新对象  
            {
                objCount++;
            }
            else if (data[(int)addr] != null && val == null)//删除对象  
            {
                objCount--;
            }


            return data[(int)addr] = val;
        }


        public int GetObjectCount()
        {
            int count = 0;
            for (int i = 0; i < data.Count; ++i)
            {
                if (data[i] != null)
                {
                    count++;
                }
            }
            return count;
        }

        public bool NeedGC()
        {
            if(objCount > gcThreshold)
            {
                return true;
            }
            return false;
        }

        public void FinishGC()
        {
            //GC之后的对象数量依然比阈值多 -> 放宽阈值  
            if(objCount > gcThreshold)
            {
                gcThreshold *= 2;
            }
        }


        public void Print()
        {
            GixConsole.LogLine("堆区(" + this.data.Count + ")");
            for (int i = 0; i < this.data.Count; ++i)
            {
                GixConsole.LogLine("(" + i + ")" + this.data[i]);
            }
        }
    }


    //虚拟栈帧（活动记录）  
    public class Frame
    {
        public string name;
        public Gizbox.GStack<Value> args = new Gizbox.GStack<Value>();//由Caller压栈(从后往前)  
        public Tuple<int, int> returnPtr;//返回地址  
        public Dictionary<string, Value> localVariables = new Dictionary<string, Value>();//局部变量和临时变量  
    }
    //调用堆栈  
    public class CallStack
    {
        private Frame[] frames;
        private int top;    

        public CallStack()
        {
            frames = new Frame[1000];
            top = 0;
        }

        public int Top
        {
            get { return top; }
            set { top = value; }
        }

        public Frame this[int idx]
        {
            get 
            {
                if(idx > frames.Length - 1)
                    throw new GizboxException(ExceptionType.StackOverflow, string.Concat( frames.Skip(frames.Length - 30).Select(f => "\n" +  (string.IsNullOrEmpty(f.name) ? "no name" : f.name)) ));

                if(frames[idx] == null)
                {
                    frames[idx] = new Frame();
                }
                return frames[idx];
            }
        }

        public void DestoryFrame(int idx)
        {
            if (idx > frames.Length - 1)
                throw new GizboxException(ExceptionType.StackOverflow, string.Concat(frames.Skip(frames.Length - 30).Select(f => "\n" + (string.IsNullOrEmpty(f.name) ? "no name" : f.name))));

            this.frames[idx] = null;
        }
    }

    //脚本引擎  
    public class ScriptEngine
    {
        //运行时  
        public Gizbox.ScriptEngine.RuntimeUnit mainUnit;

        //符号表堆栈  
        public GStack<SymbolTable> envStack;

        //C#互操作上下文  
        public Gizbox.Interop.CSharp.InteropContext csharpInteropContext;

        //堆区  
        public Heap heap = new Heap();

        //调用堆栈  
        private CallStack callStack = new CallStack();

        //返回值寄存器（虚拟）(实际在x86架构通常为EAX寄存器 x86-64架构通常为RAX寄存器)
        private Value retRegister = Value.Void;


        //计算工具  
        private Calculator caculator;


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





        public ScriptEngine()
        {
            caculator = new Calculator(this);
            csharpInteropContext = new Interop.CSharp.InteropContext(this);
        }


        public void Load(Gizbox.IL.ILUnit ir)
        {
            Log("载入主程序");
            //运行时
            this.mainUnit = new RuntimeUnit(this, ir);
            this.mainUnit.MainUnitLinkLibs();


            Log("主程序的依赖链接完毕，总共：" + this.mainUnit.allUnits.Count);

            //调用堆栈  
            this.callStack = new CallStack();

            //入口
            this.curr = 0;

            //符号栈  
            this.envStack = this.mainUnit.GetEnvStack(-1, 0); 
        }

        public void Execute(Gizbox.IL.ILUnit ir)
        {
            Load(ir);

            Execute();
        }

        public void Execute()
        {
            if (this.mainUnit == null) throw new GizboxException(ExceptionType.NoInstructionsToExecute);

            //留空  
            Log("开始执行");
            Log(new string('\n', 20));

            executing = true;

            analysisWatch.Start();

            while ((this.currUnit == -1 && this.curr >= mainUnit.codes.Count) == false)
            {
                if (this.currUnit == -1)
                {
                    Interpret(mainUnit.codes[this.curr]);
                }
                else
                {
                    Interpret(mainUnit.allUnits[this.currUnit].codes[this.curr]);
                }
            }

            analysisWatch.Stop();

            if (analysisTime == true)
            {
                if (Compiler.enableLogScriptEngine == false)
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

        private void Interpret(RuntimeCode code)
        {
            //DEBUG信息  
            if (analysisTime)
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
                if(heap.NeedGC())
                {
                    GCCollect();
                }
            }

            //设置符号表链（用于解析符号）  
            if (this.mainUnit.NeedResetStack(this.currUnit, this.curr, this.prevUnit, this.prev))
            {
                this.envStack = this.mainUnit.GetEnvStack(this.currUnit, this.curr);
            }
            prev = curr;
            prevUnit = currUnit;


            //开始指向本条指令  
            if (Compiler.enableLogScriptEngine) 
                Log(new string('>', 10) + code.ToExpression(showlabel:false));
            
            switch(code.op)
            {
                case "": break;
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
                        this.callStack.Top += 1;
                    }
                    break;
                case "FUNC_END":
                    {
                        //返回地址  
                        this.currUnit = this.callStack[callStack.Top].returnPtr.Item1;
                        this.curr = this.callStack[callStack.Top].returnPtr.Item2;

                        //返回原栈帧  
                        this.callStack.Top -= 1;

                        //销毁移出栈栈帧  
                        this.callStack.DestoryFrame(this.callStack.Top + 1);
                        
                        return;
                    }
                case "METHOD_BEGIN":
                    {
                        ////进入类作用域、方法作用域    
                        //string className = code.arg1.Split('.')[0];
                        //string methodName = code.arg1.Split('.')[1];

                        //新的栈帧
                        this.callStack.Top += 1;
                    }
                    break;
                case "METHOD_END":
                    {
                        //返回地址  
                        this.currUnit = this.callStack[callStack.Top].returnPtr.Item1;
                        this.curr = this.callStack[callStack.Top].returnPtr.Item2;

                        //返回原栈帧  
                        this.callStack.Top -= 1;

                        //销毁移出栈栈帧  
                        this.callStack.DestoryFrame(this.callStack.Top + 1);

                        return;
                    }
                case "RETURN":
                    {
                        var retValue = code.arg1;
                        if(retValue != null)
                        {
                            retRegister = GetValue(code.arg1);
                        }
                        var jumpAddr = mainUnit.QueryLabel("exit", envStack.Peek().name, currUnit);
                        int exitLine = jumpAddr.Item2;
                        int endLine = exitLine - 1;    
                        
                        //不能使用ir判断
                        //if(ir.codes[endLine].op != "METHOD_END" && ir.codes[endLine].op != "FUNC_END")
                        //{
                        //    throw new Exception("函数或方法的END没有紧接exit标签");  
                        //}
                        this.curr = endLine;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "EXTERN_IMPL":
                    {
                        List<Value> arguments = callStack[callStack.Top].args.ToList();
                        arguments.Reverse();

                        Value result = csharpInteropContext.ExternCall(OperandString.GetString(code.arg1), arguments);

                        retRegister = result;

                        var jumpAddr = mainUnit.QueryLabel("exit", envStack.Peek().name, currUnit); 
                        int exitLine = jumpAddr.Item2;
                        int endLine = exitLine - 1;

                        //if (ir.codes[endLine].op != "METHOD_END" && ir.codes[endLine].op != "FUNC_END")
                        //{
                        //    throw new Exception("函数或方法的END没有紧接exit标签");
                        //}
                        this.curr = endLine;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "PARAM":
                    {
                        Value arg = GetValue(code.arg1); 
                        
                        if (arg.Type == GizType.Void)
                            throw new RuntimeException(ExceptionType.ScriptRuntimeError, GetCurrentCode(), "null argument");
                        
                        this.callStack[this.callStack.Top + 1].args.Push(arg);
                    }
                    break;
                case "CALL":
                    {
                        string funcMangledName = code.arg1.str;

                        if (GetValue(code.arg2).Type != GizType.Int) throw new GizboxException(ExceptionType.Unknown, "arg count not integer");
                        int argCount = GetValue(code.arg2).AsInt;

                        this.callStack[this.callStack.Top + 1].returnPtr = new Tuple<int, int>(this.currUnit, (this.curr + 1));

                        string funcFinalName = funcMangledName;

                        this.callStack[this.callStack.Top + 1].name = funcFinalName;

                        var jumpAddr = mainUnit.QueryLabel("entry", funcFinalName, currUnit);
                        this.curr = jumpAddr.Item2;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "MCALL":
                    {
                        string funcMangledName = code.arg1.str;

                        if (GetValue(code.arg2).Type != GizType.Int) throw new GizboxException(ExceptionType.Unknown, "arg count not integer");
                        int argCount = GetValue(code.arg2).AsInt;

                        this.callStack[this.callStack.Top + 1].returnPtr = new Tuple<int, int>(this.currUnit, (this.curr + 1));


                        //运行时多态  
                        //获取this参数  
                        if (this.callStack[this.callStack.Top + 1].args.Count == 0) throw new RuntimeException(ExceptionType.ScriptRuntimeError, GetCurrentCode(), "method call doesnt has \"this\" argument！");
                        var arg_this = this.callStack[this.callStack.Top + 1].args.Peek();
                        if (arg_this.Type != GizType.GizObject) throw new RuntimeException(ExceptionType.ScriptRuntimeError, GetCurrentCode(), "method call doesnt has \"this\" argument！");
                        string trueType = (this.DeReference(arg_this.AsPtr)as GizObject).truetype;


                        //新方法：虚函数表vtable    
                        var vrec = (this.DeReference(arg_this.AsPtr) as GizObject).vtable.Query(funcMangledName);
                        string funcFinalName = vrec.funcfullname;


                        this.callStack[this.callStack.Top + 1].name = funcMangledName;

                        var jumpAddr = mainUnit.QueryLabel("entry", funcFinalName, currUnit);
                        this.curr = jumpAddr.Item2;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "=":
                    {
                        SetValue(code.arg1, GetValue(code.arg2)); 
                    }
                    break;
                case "+=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("+", GetValue(code.arg1), GetValue(code.arg2)));
                    }
                    break;
                case "-=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("-", GetValue(code.arg1), GetValue(code.arg2)));
                    }
                    break;
                case "*=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("*", GetValue(code.arg1), GetValue(code.arg2)));
                    }
                    break;
                case "/=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("/", GetValue(code.arg1), GetValue(code.arg2)));
                    }
                    break;
                case "%=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("%", GetValue(code.arg1), GetValue(code.arg2)));
                    }
                    break;
                case "+":
                    {
                        SetValue(code.arg1, caculator.CalBinary("+", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "-":
                    {
                        SetValue(code.arg1, caculator.CalBinary("-", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "*":
                    {
                        SetValue(code.arg1, caculator.CalBinary("*", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "/":
                    {
                        SetValue(code.arg1, caculator.CalBinary("/", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "%":
                    {
                        SetValue(code.arg1, caculator.CalBinary("%", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "NEG":
                    {
                        SetValue(code.arg1, caculator.CalNegtive(GetValue(code.arg2)));
                    }
                    break;
                case "!":
                    {
                        SetValue(code.arg1, caculator.CalNot(GetValue(code.arg2)));
                    }
                    break;
                case "ALLOC":
                    {
                        string className = OperandString.GetString(code.arg2);
                        SymbolTable tableFound; 
                        var rec = Query(className, out tableFound);
                        GizObject newfanObj = new GizObject(className, rec.envPtr, mainUnit.QueryVTable(className));
                        var ptr = heap.Alloc(newfanObj);
                        SetValue(code.arg1, Value.FromGizObjectPtr(ptr));
                    }
                    break;
                case "DEL":
                    {
                        var objPtr = GetValue(code.arg1);
                        if(objPtr.IsPtr && objPtr.AsPtr >= 0)
                        {
                            this.heap.Write(objPtr.AsPtr, null);
                        }
                        else
                        {
                            throw new RuntimeException(ExceptionType.OnlyHeapObjectsCanBeFreed, GetCurrentCode(), "");
                        }
                    }
                    break;
                case "ALLOC_ARRAY":
                    {
                        Value[] arr = new Value[GetValue(code.arg2).AsInt];
                        var ptr = heap.Alloc(arr);
                        SetValue(code.arg1, Value.FromArrayPtr(ptr));
                    }
                    break;
                case "IF_FALSE_JUMP":
                    {
                        bool conditionTrue = GetValue(code.arg1).AsBool;
                        if(conditionTrue == false)
                        {
                            var jumpAddr = mainUnit.QueryLabel(OperandString.GetString(code.arg2), "", currUnit);
                            
                            this.curr = jumpAddr.Item2;
                            this.currUnit = jumpAddr.Item1;
                            return;
                        }
                        else
                        {
                            break;
                        }
                    }
                case "<":
                    {
                        SetValue(code.arg1, caculator.CalBinary("<", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "<=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("<=", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case ">":
                    {
                        SetValue(code.arg1, caculator.CalBinary(">", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case ">=":
                    {
                        SetValue(code.arg1, caculator.CalBinary(">=", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "==":
                    {
                        SetValue(code.arg1, caculator.CalBinary("==", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "!=":
                    {
                        SetValue(code.arg1, caculator.CalBinary("!=", GetValue(code.arg2), GetValue(code.arg3)));
                    }
                    break;
                case "++":
                    {
                        SetValue(code.arg1, GetValue(code.arg1) + 1);
                    }
                    break;
                case "--":
                    {
                        SetValue(code.arg1, GetValue(code.arg1) - 1);
                    }
                    break;
                case "CAST":
                    {
                        SetValue(code.arg1, caculator.Cast(OperandString.GetString(code.arg2), GetValue(code.arg3), this));
                    }
                    break;
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
            if (this.libSearchDirectories.Count == 0) throw new GizboxException(ExceptionType.LibraryLoadPathNotSet);
            foreach (var dir in this.libSearchDirectories)
            {
                System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(dir);
                foreach (var f in dirInfo.GetFiles())
                {
                    if (System.IO.Path.GetFileNameWithoutExtension(f.Name) == libname)
                    {
                        if (System.IO.Path.GetExtension(f.Name).EndsWith("gixlib"))
                        {
                            var unit = Gizbox.IL.ILSerializer.Deserialize(f.FullName);
                            Log("导入库文件：" + f.Name + " 库名：" + unit.name);
                            return unit;
                        }
                    }
                }
            }

            throw new GizboxException(ExceptionType.LibraryFileNotFound,  libname + ".gixlib" );
        }



        // ----------------------- Interfaces ---------------------------

        public RuntimeCode GetCurrentCode()
        {
            return mainUnit.QueryCode(currUnit, curr);
        }

        public bool CheckFunctionEntry(string functionName, params System.Type[] argTypes)
        {
            //查找是否有函数  
            string[] argGizTypes = argTypes.Select(a => InteropCSharp.GetGizType(a)).ToArray();
            string funcFinalName = Utils.Mangle(functionName, argGizTypes);
            var jumpAddr = mainUnit.QueryLabel("entry", funcFinalName, currUnit);
            if(jumpAddr != null)
            {
                return true;
            }
            return false;
        }

        public object Call(string functionName, params object[] args)
        {
            if (executing) return null;

            //查找是否有函数  
            string[] argGizTypes = args.Select(a => InteropCSharp.GetGizType(a)).ToArray();
            string funcFinalName = Utils.Mangle(functionName, argGizTypes);
            var jumpAddr = mainUnit.QueryLabel("entry", funcFinalName, currUnit);
            if (jumpAddr == null) return null;


            //记录当前状态    
            int tmpCurrUnit = currUnit;
            int tmpCurr = curr;
            var currentStack = callStack[callStack.Top]; 
            

            //参数入栈  
            for (int i = args.Length - 1; i >= 0; --i)
            {
                Value arg = csharpInteropContext.Marshal2Giz(args[i]);
                if (arg.Type == GizType.Void) throw new GizboxException(ExceptionType.ArgumentError);
                this.callStack[this.callStack.Top + 1].args.Push(arg);
            }

            //调用  
            {
                this.callStack[this.callStack.Top + 1].returnPtr = new Tuple<int, int>(tmpCurrUnit, tmpCurr);//返回到最后  

                this.curr = jumpAddr.Item2;
                this.currUnit = jumpAddr.Item1;
            }

            //执行  
            while ((this.currUnit == tmpCurrUnit && this.curr == tmpCurr) == false)
            {
                if (this.currUnit == -1)
                {
                    Interpret(mainUnit.codes[this.curr]);
                }
                else
                {
                    Interpret(mainUnit.allUnits[this.currUnit].codes[this.curr]);
                }
            }

            return csharpInteropContext.Marshal2CSharp(retRegister);
        }

        // ----------------------- Operations ---------------------------

        private Value GetValue(Operand operand)
        {
            //Log("获取值：" + operand.str);
            switch (operand)
            {
                case OperandRegister r:
                    {
                        switch(r.registerName)
                        {
                            case "RET": return retRegister;
                            default: throw new GizboxException(ExceptionType.Unknown, "unknown register:" + r.registerName);
                        }
                    }
                case OperandConst c:
                    {
                        switch(c.giztype)
                        {
                            case "string":
                                {
                                    return Value.FromConstStringPtr(c.linkedPtr);
                                }
                        }
                        throw new RuntimeException(ExceptionType.UnknownConstant, GetCurrentCode(), "");
                    }
                case OperandLiteralValue l: return l.val;
                case OperandVariable varible:
                    {
                        return AccessVariable(varible.name, write:false);
                    }
                case OperandElementAccess ele:
                    {
                        int idx = GetValue(ele.index).AsInt;

                        var arr = (Value[])(this.DeReference(GetValue(ele.array).AsPtr));
                        return arr[idx];
                    }
                case OperandMemberAccess memb:
                    {
                        var objptr = GetValue(memb.obj);
                        string fieldName = memb.fieldname;

                        if ((objptr.IsVoid)) throw new RuntimeException(ExceptionType.AccessedObjectNotFound, GetCurrentCode(), "when access member \"" + fieldName +  "\"");
                        if (objptr.Type != GizType.GizObject) throw new RuntimeException(ExceptionType.ObjectTypeError, GetCurrentCode(), "not gizbox object");

                        var fields = (this.DeReference(objptr.AsPtr) as GizObject).fields;
                        if (fields.ContainsKey(fieldName) == false) throw new RuntimeException(ExceptionType.ObjectFieldNotInitialized,GetCurrentCode(), fieldName);
                        return (this.DeReference(objptr.AsPtr) as GizObject).fields[fieldName];

                    }
                default:
                    throw new GizboxException(ExceptionType.AccessError, operand.GetType().Name);
            }
        }

        private void SetValue(Operand operand, Value val)
        {
            //Log("设置值：" + operand.str + "  =  " + val + "    操作数类型：" + operand.GetType().Name);
            switch (operand)
            {
                case OperandRegister r:
                    {
                        switch (r.registerName)
                        {
                            case "RET": retRegister = val; break;
                            default: throw new RuntimeException(ExceptionType.ScriptRuntimeError, null, "unknown register :" + r.registerName);
                        }
                    }
                    break;
                case OperandConst c:
                    {
                        throw new RuntimeException(ExceptionType.CannotAssignToConstant, null, "");
                    }
                    break;
                case OperandLiteralValue l:
                    {
                        throw new RuntimeException(ExceptionType.CannotAssignToLiteral, null, "");
                    }
                    break;
                case OperandVariable varible:
                    {
                        AccessVariable(varible.name, write: true, value:val);
                    }
                    break;
                case OperandElementAccess ele:
                    {
                        int idx = GetValue(ele.index).AsInt;

                        var arr = (Value[])(this.DeReference(GetValue(ele.array).AsPtr));
                        
                        arr[idx] = val;
                    }
                    break;
                case OperandMemberAccess memb:
                    {
                        var objptr = GetValue(memb.obj);
                        string fieldName = memb.fieldname;

                        if ((objptr.IsVoid)) throw new RuntimeException(ExceptionType.AccessedObjectNotFound, GetCurrentCode(), "when acess member \"" + fieldName + "\"");
                        if (objptr.Type != GizType.GizObject) throw new RuntimeException(ExceptionType.ScriptRuntimeError, GetCurrentCode(), "not gizbox object");


                        (this.DeReference(objptr.AsPtr) as GizObject).fields[fieldName] = val;
                        //Log((this.DeReference(objptr.AsPtr) as GizObject).truetype + "类型的" + memb.str + "的值被设置为" + val);
                    }
                    break;
                default:
                    throw new RuntimeException(ExceptionType.AccessError, GetCurrentCode(), "");
            }
        }





        public Value UnBoxCsObject2GizValue(object o)
        {
            switch(o)
            {
                case Boolean b: return b;
                case Int32 i: return i;
                case Single f: return f;
                case Double d: return d;
                case Char c: return c;
                default:
                    {
                        if(o is String)
                        {
                            long ptr = heap.Alloc(o);
                            return Value.FromStringPtr(ptr);
                        }
                        else if(o is System.Array)
                        {
                            long ptr = heap.Alloc(o);
                            return Value.FromArrayPtr(ptr);
                        }
                        else if(o is GizObject)
                        {
                            long ptr = heap.Alloc(o);
                            return Value.FromGizObjectPtr(ptr);
                        }
                        return Value.Void; 
                    }
            }
        }
        public object BoxGizValue2CsObject(Value v)
        {
            switch (v.Type)
            {
                case GizType.Bool: return v.AsBool;
                case GizType.Int: return v.AsInt;
                case GizType.Float: return v.AsFloat;
                case GizType.Double: return v.AsDouble;
                case GizType.Char: return v.AsChar;

                case GizType.String:
                    {
                        return this.DeReference(v.AsPtr);
                    }
                default:
                    {
                        return this.DeReference(v.AsPtr);
                    }

            }
        }
        public object DeReference(long ptr)
        {
            if (ptr >= 0)
            {
                return this.heap.Read(ptr);
            }
            else
            {
                var v = mainUnit.ReadConst(ptr);
                if(v is char[])
                {
                    return new string((char[])v);
                }
                else
                {
                    return v;
                }
            }
        }


        public Value NewString(string str)
        {
            Log("创建新的动态字符串：" + str);

            long ptr = this.heap.Alloc(str);
            return Value.FromStringPtr(ptr);
        }

        private Value AccessVariable(string varname, bool write, Value value = default)
        {
            string name = varname;

            var currentEnv = this.envStack.Peek();
            var currentFrame = this.callStack[this.callStack.Top];

            SymbolTable envFount;

            var rec = Query(name, out envFount);

            //查找局部符号表  
            if (rec != null && envFount.tableCatagory != SymbolTable.TableCatagory.GlobalScope)
            {
                //是参数  
                if (rec.category == SymbolTable.RecordCatagory.Param)
                {
                    var allParams = envFount.GetByCategory(SymbolTable.RecordCatagory.Param);
                    int idxInParams = allParams.FindIndex(p => p.name == rec.name);
                    if (idxInParams < 0) throw new RuntimeException(ExceptionType.ParameterListParameterNotFound, GetCurrentCode(), name);

                    if (write)
                    {
                        currentFrame.args[currentFrame.args.Top - idxInParams] = value;
                        if (Compiler.enableLogScriptEngine) Log("参数" + name + "写入：" + value.ToString());
                        return Value.Void;
                    }
                    else
                    {
                        return currentFrame.args[currentFrame.args.Top - idxInParams];
                    }
                }
                //局部变量  
                else if (rec.category == SymbolTable.RecordCatagory.Var)
                {
                    if (write)
                    {
                        currentFrame.localVariables[name] = value;
                        if (Compiler.enableLogScriptEngine) Log("局部变量" + name + "写入：" + value.ToString());
                        return Value.Void;
                    }
                    else
                    {
                        if (currentFrame.localVariables.ContainsKey(name) == false)
                            throw new RuntimeException(ExceptionType.LocalVariableNotInitialized, GetCurrentCode(), name);

                        return currentFrame.localVariables[name];
                    }
                }
                else
                {
                    throw new RuntimeException(ExceptionType.NotParameterOrLocalVariable, GetCurrentCode(), name);
                }
            }
            //查找全局符号表  
            else if (this.mainUnit.globalScope.env.ContainRecordName(name))
            {
                var gloabalRec = this.mainUnit.globalScope.env.GetRecord(name);

                if (write)
                {
                    mainUnit.WriteGlobalVar(name, value);

                    if (Compiler.enableLogScriptEngine) Log("全局变量" + name + "写入：" + value.ToString());
                    return Value.Void;
                }
                else
                {
                    if (mainUnit.ReadGlobalVar(name).Type == GizType.Void)
                        throw new RuntimeException(ExceptionType.GlobalVariableNotInitialized, GetCurrentCode(), name);

                    return mainUnit.ReadGlobalVar(name);
                }
            }
            else
            {
                throw new RuntimeException(ExceptionType.StackFrameAndGlobalSymbolTableNameNotFound, GetCurrentCode(), name);
            }
        }


        private SymbolTable.Record Query(string symbolName, out SymbolTable tableFound)
        {
            //本编译单元查找  
            for (int i = envStack.Count - 1; i > -1; --i)
            {
                if (envStack[i].ContainRecordName(symbolName))
                {
                    tableFound = envStack[i];
                    return envStack[i].GetRecord(symbolName);
                }
            }

            //导入库查找  
            foreach (var lib in this.mainUnit.allUnits)
            {
                if (lib.globalScope.env.ContainRecordName(symbolName))
                {
                    tableFound = lib.globalScope.env;
                    return lib.globalScope.env.GetRecord(symbolName);
                }
            }


            Log("在符号表链中未找到：" + symbolName + "符号表链：" + string.Concat(envStack.ToList().Select(e => e.name + " - ")) + " 当前行数：" + curr);
            tableFound = null;
            return null;
        }

        private bool IsBaseType(string typeExpr)
        {
            switch(typeExpr)
            {
                case "bool": return true;
                case "int": return true;
                case "float": return true;
                case "string": return true;
                default: return false;
            }
        }
        private bool IsFuncType(string typeExpr)
        {
            if(typeExpr.Contains("->"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        private static void Log(object content)
        {
            if (!Compiler.enableLogScriptEngine) return;
            GixConsole.LogLine("ScriptEngine >>" + content);
        }

        //Profiler
        private void ProfilerLog()
        {
            Log(new string('\n', 3));
            Log(" ---- 性能剖析 ---- ");

            Dictionary<int, List<long>> lineToTicksList = new Dictionary<int, List<long>>();
            for (int i = 0; i < timeList.Count; ++i)
            {
                int line = lineList[i];
                long time = timeList[i];

                if (lineToTicksList.ContainsKey(line) == false)
                    lineToTicksList[line] = new List<long>(100);

                lineToTicksList[line].Add(time);
            }


            List<KeyValuePair<int, double>> line_Time_List = new List<KeyValuePair<int, double>>();
            foreach (var k in lineToTicksList.Keys)
            {
                var avg = lineToTicksList[k].Average();
                line_Time_List.Add(new KeyValuePair<int, double>(k, avg));
            }

            line_Time_List.Sort((kv1, kv2) => (int)kv1.Value - (int)kv2.Value);

            foreach (var kv in line_Time_List)
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


        //GC    
        public void GCEnable(bool enable)
        {
            this.gcEnabled = enable;
        }
        public void GCCollect()
        {
            if (gcEnabled == false) return;  

            List<long> accessableList = new List<long>();
            List<long> danglingPtrList = new List<long>();
            Dictionary<long, long> redirectTable = new Dictionary<long, long>();

            System.Func<Value, Value> MarkAccessable = (v) => 
            {
                if (accessableList.Contains(v.AsPtr) == false && this.heap.Read(v.AsPtr) != null)
                {
                    accessableList.Add(v.AsPtr);
                }

                return v;
            };
            System.Func<Value, Value> Redirect = (v) => 
            {
                if (redirectTable.ContainsKey(v.AsPtr))
                {
                    long newptr = redirectTable[v.AsPtr];
                    v.AsPtr = newptr;
                }
                return v;
            };

            //从根集获取所有可达对象    
            TraversalFromRoot(MarkAccessable);


            //删除垃圾对象  
            {
                //所有地址的集合  
                HashSet<long> heapAddresses = new HashSet<long>();
                for (long i = 1; i < this.heap.HeapSize; i++)
                {
                    heapAddresses.Add(i);
                }
                // 取补集，找出需要回收的垃圾对象的地址
                HashSet<long> unreachableAddresses = new HashSet<long>(heapAddresses);
                foreach (long addr in accessableList)
                {
                    unreachableAddresses.Remove(addr);
                }
                foreach(var addr in unreachableAddresses)
                {
                    this.heap.Write(addr, null);
                }
            }




            //堆对象移动    
            int left = 0;
            int right = 0;
            while (right < this.heap.HeapSize)
            {
                if (this.heap.Read(left) != null) left++;
                if (right <= left) right = left + 1;
                if (right >= this.heap.HeapSize) break;

                if (this.heap.Read(left) == null)
                {
                    if (this.heap.Read(right) != null)
                    {
                        this.heap.Write(left, this.heap.Read(right));
                        this.heap.Write(right, null);
                        redirectTable.Add(right, left);

                        left++;
                        right++;
                    }
                    else
                    {
                        right++;
                    }
                }
                else
                {
                    left++;
                }

                if (right >= this.heap.HeapSize) break;
            }


            Log("----------- GC --------------");
            int objCount = this.heap.GetObjectCount();
            Log("堆对象数量：" + objCount + "个");
            Log("不重复引用数量：" + accessableList.Count + "个");

            Log(redirectTable.Keys.Count + "个对象需要重定向:");
            foreach(var kv in redirectTable)
            {
                Log(kv.Key + " -> " + kv.Value);
            }
            Log("-----------------------------");

            //重定向指针    
            TraversalFromRoot(Redirect);


            //完成GC  
            this.heap.FinishGC();
        }
        private void TraversalFromRoot(System.Func< Value, Value> op)
        {
            foreach (var key in this.mainUnit.globalData.Keys)
            {
                if (this.mainUnit.globalData[key].IsPtr)
                {
                    var newVal = TraversingObject(this.mainUnit.globalData[key], op);
                    this.mainUnit.globalData[key] = newVal;
                }
            }

            for (int i = 0; i <= this.callStack.Top; ++i)
            {
                var argList = this.callStack[i].args.ToList();
                for (int a = 0; a < argList.Count; ++a)
                {
                    if (argList[a].IsPtr)
                    {
                        var newVal = TraversingObject(argList[a], op);
                        argList[a] = newVal;
                    }
                }
                foreach (var key in this.callStack[i].localVariables.Keys)
                {
                    if (this.callStack[i].localVariables[key].IsPtr)
                    {
                        var newVal = TraversingObject(this.callStack[i].localVariables[key], op);
                        this.callStack[i].localVariables[key] = newVal;
                    }
                }
            }
        }
        private Value TraversingObject(Value objPtr, System.Func<Value, Value> op)
        {
            var obj = DeReference(objPtr.AsPtr);
            if (obj is GizObject)
            {
                var fieldDic = (obj as GizObject).fields;
                foreach (var key in fieldDic.Keys)
                {
                    if (fieldDic[key].IsPtr)
                    {
                        var newVal = TraversingObject(fieldDic[key], op);
                        fieldDic[key] = newVal;
                    }
                }
            }
            else if(obj is Value[])
            {
                var arr = (obj as Value[]);
                for (int i = 0; i < arr.Length; ++i)
                {
                    if (arr[i].IsPtr)
                    {
                        var newVal = TraversingObject(arr[i], op);
                        arr[i] = newVal;
                    }
                }
            }

            var newPtrVal  = op(objPtr);
            return newPtrVal;
        }


        //DEBUG  
        private System.Diagnostics.Stopwatch profileW = new System.Diagnostics.Stopwatch();
        private string profileName = "";
        private void PrintCallStack()
        {
            for (int i = 0; i <= callStack.Top; ++i)
            {
                Log(i.ToString() + "(top:" + callStack.Top + ")" + new string('-', 20));
                Log("Arguments:");
                foreach(var k in callStack[i].args.ToList())
                {
                    Log(k.GetType().Name + ":" + k.ToString());
                }
                Log("LOCALs:");
                foreach (var k in callStack[i].localVariables.Keys)
                {
                    var v = callStack[i].localVariables[k];
                    Log(v.GetType().Name + ":" + v.ToString());
                }
                Log(new string('-', 20));
            }
        }

    }

    public class Calculator
    {
        private static string[] arithmeticOperators = new string[] { "+", "-", "*", "/", "%" };
        private static string[] comparisonOperators = new string[] { ">", "<", ">=", "<=", "==", "!=" };


        private ScriptEngine engine;

        public Calculator(ScriptEngine engineContext)
        {
            this.engine = engineContext;
        }

        public Value CalNot(Value v)
        {
            if(v.Type == GizType.Bool)
            {
                return !(v.AsBool);
            }
            else
            {
                throw new RuntimeException(ExceptionType.OnlyBooleanValuesCanBeNegated, engine.GetCurrentCode(), "");
            }
        }

        public Value CalNegtive(Value v)
        {
            switch(v.Type)
            {
                case GizType.Int: return -(v.AsInt);
                case GizType.Float: return -(v.AsFloat);
                default: throw new RuntimeException(ExceptionType.TypeCannotBeNegative, engine.GetCurrentCode(), v.Type.ToString());
            }
        }

        public Value CalBinary(string op, Value v1, Value v2)
        {
            //固有的操作符重载    
            if(v1.Type == GizType.String && v2.Type == GizType.String)
            {
                //Log("字符串连接：str1: " + (string)engine.DeReference(v1.AsPtr) + " ptr1 : " + v1.AsPtr + "  str2: " + (string)engine.DeReference(v2.AsPtr) + "  ptr2 : " + v2.AsPtr);
                string newstring = (string)engine.DeReference(v1.AsPtr) + (string)engine.DeReference(v2.AsPtr);
                return engine.NewString(newstring);
            }


            //查询其他操作符重载......    

            switch (op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;

                case ">": return v1 > v2;
                case "<": return v1 < v2;
                case ">=": return v1 >= v2;
                case "<=": return v1 <= v2;
                case "==": return v1 == v2;
                case "!=": return v1 != v2;

                default: return Value.Void;
            }
        }

        public Value Cast(string toType, Value val, ScriptEngine engine)
        {
            //强制转换重载查询.....


            //转换为string  
            if(toType == "string")
            {
                return ToString(val);
            }


            //其他转换  
            switch(val.Type)
            {
                case GizType.Bool:
                    {
                        switch(toType)
                        {
                            case "bool": return val;
                        }
                    }
                    break;
                case GizType.Int:
                    {
                        switch (toType)
                        {
                            case "int": return val;
                            case "float": return ((float)val.AsInt);
                            case "double": return ((double)val.AsInt);
                        }
                    }
                    break;
                case GizType.Float:
                    {
                        switch (toType)
                        {
                            case "int": return ((int)val.AsFloat);
                            case "float": return val;
                            case "double": return ((double)val.AsFloat);
                        }
                    }
                    break;
                case GizType.Double:
                    {
                        switch (toType)
                        {
                            case "int": return ((int)val.AsDouble);
                            case "float": return ((float)val.AsDouble);
                            case "double": return val;
                        }
                    }
                    break;
                case GizType.String:
                    {
                        switch (toType)
                        {
                            case "string": return val;
                        }
                    }
                    break;
                case GizType.GizObject:
                    {
                        switch (toType)
                        {
                            case "void": 
                            case "bool": 
                            case "int": 
                            case "float": 
                            case "double": 
                            case "char":
                                {
                                    throw new GizboxException(ExceptionType.TypeConversionError, "GizboxObject to " + toType);
                                }
                            default:
                                {
                                    return val;//里氏替换
                                }
                        }
                    }
                    break;
                case GizType.GizArray:
                    {
                        switch (toType)
                        {
                            case "void": 
                            case "bool": 
                            case "int": 
                            case "float": 
                            case "double": 
                            case "char":
                                {
                                    throw new GizboxException(ExceptionType.TypeConversionError, "GizboxArray to " + toType);
                                }
                            default:
                                {
                                    throw new GizboxException(ExceptionType.TypeConversionError, "GizboxArray to other");
                                }
                        }
                    }
                    break;
            }

            return Value.Void;
        }

        public Value ToString(Value value)
        {
            string str = "";
            switch(value.Type)
            {
                case GizType.String:
                    {
                        return value;
                    }
                    break;
                case GizType.Void:
                case GizType.Bool:
                case GizType.Int:
                case GizType.Float:
                case GizType.Double:
                case GizType.Char:
                case GizType.GizObject:
                case GizType.GizArray:
                    {
                        str = value.ToString();
                    }
                    break;
            }

            return engine.NewString(str);
        }
    }
}



