using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Gizbox;
using Gizbox.IL;

namespace Gizbox.ScriptEngine
{
    //堆区  
    public class Heap
    {
        public List<object> data = new List<object>();

        public long Alloc(object obj)
        {
            data.Add(obj);
            return (data.Count - 1);
        }

        public object Read(long addr)
        {
            return data[(int)addr];
        }

        public void Print()
        {
            Console.WriteLine("堆区(" + this.data.Count + ")");
            for (int i = 0; i < this.data.Count; ++i)
            {
                Console.WriteLine("(" + i + ")" + this.data[i]);
            }
        }
    }


    //虚拟栈帧（活动记录）  
    public class Frame
    {
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
                if(frames[idx] == null)
                {
                    frames[idx] = new Frame();
                }
                return frames[idx];
            }
        }

        public void DestoryFrame(int idx)
        {
            this.frames[idx] = null;
        }
    }

    //脚本引擎  
    public class ScriptEngine
    {
        //符号表堆栈  
        public GStack<SymbolTable> envStack;

        //中间代码  
        public Gizbox.IL.ILUnit mainUnit;

        //C#互操作上下文  
        public Gizbox.Interop.CSharp.InteropContext csharpInteropContext;

        //堆区  
        private Heap heap = new Heap();

        //调用堆栈  
        private CallStack callStack = new CallStack();

        //返回值寄存器（虚拟）(实际在x86架构通常为EAX寄存器 x86-64架构通常为RAX寄存器)
        private Value retRegister = Value.Void;


        //计算工具  
        private Calculator caculator;

        //临时数据  
        private int currUnit = -1;
        private int curr = 0;
        private int prevUnit = -1;
        private int prev = 0;

        //DEBUG    
        private static bool analysisTime = false;
        private System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private long prevTicks;
        private List<long> timeList = new List<long>(10000);
        private List<int> lineList = new List<int>(10000);





        public ScriptEngine()
        {
            caculator = new Calculator(this);
            csharpInteropContext = new Interop.CSharp.InteropContext(this);
        }

        public void Execute(Gizbox.IL.ILUnit ir)
        {
            this.mainUnit = ir;

            //调用堆栈  
            this.callStack = new CallStack();
            
            //入口
            this.curr = ir.codeEntry;

            //符号栈  
            this.envStack = ir.GetEnvStack(-1 , 0);

            //留空  
            Console.WriteLine("开始执行");
            Console.WriteLine(new string('\n', 20));

            watch.Start();

            while ((this.currUnit == -1 && this.curr >= ir.codes.Count) == false)
            {
                if(this.currUnit == -1)
                {
                    Interpret(ir.codes[this.curr]);
                }
                else
                {
                    Interpret(ir.dependencies[this.currUnit].codes[this.curr]);
                }
            }

            watch.Stop();

            if(analysisTime == true)
            {
                if(Compiler.enableLogScriptEngine == false)
                {

                    Console.WriteLine(new string('\n', 3));
                    Console.WriteLine("总执行时间：" + watch.ElapsedMilliseconds + "ms");

                    Console.ReadKey();
                    ProfilerLog();
                }
                else
                {

                    Console.WriteLine(new string('\n', 3));
                    Console.WriteLine("总执行时间：(由于开启log无法预估)");
                }
            }

        }

        private void ProfilerLog()
        {
            Console.WriteLine(new string('\n', 3));
            Console.WriteLine(" ---- 性能剖析 ---- ");

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
                Console.WriteLine("line:" + kv.Key + "[" + mainUnit.codes[kv.Key].ToExpression(false) + "]" + "  avgTicks:" + kv.Value);
            }
        }

        private void Interpret(TAC tac)
        {
            //DEBUG信息  
            if (analysisTime)
            {
                //Console.WriteLine("Exe:  " + currUnit + " {" + curr + "}" + tac.ToExpression(false));
                //Console.ReadKey();
                lineList.Add(prev);
                timeList.Add(watch.ElapsedTicks - prevTicks);
                prevTicks = watch.ElapsedTicks;
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
                Log(new string('>', 10) + tac.ToExpression(showlabel:false));
            
            switch(tac.op)
            {
                case "": break;
                case "JUMP":
                    {
                        var jaddr = mainUnit.QueryLabel(tac.arg1, currUnit);
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
                        //进入类作用域、方法作用域    
                        string className = tac.arg1.Split('.')[0];
                        string methodName = tac.arg1.Split('.')[1];

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
                        var retValue = tac.arg1;
                        if(retValue != null)
                        {
                            retRegister = GetValue(tac.arg1);
                        }
                        var jumpAddr = mainUnit.QueryLabel("exit:" + envStack.Peek().name, currUnit);
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

                        Value result = csharpInteropContext.ExternCall(tac.arg1, arguments);

                        retRegister = result;

                        var jumpAddr = mainUnit.QueryLabel("exit:" + envStack.Peek().name, currUnit); 
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
                        Value arg = GetValue(tac.arg1); 
                        
                        if (arg.Type == GizType.Void)
                            throw new RuntimeException(GetCurrentCode(), "null实参");
                        
                        this.callStack[this.callStack.Top + 1].args.Push(arg);
                    }
                    break;
                case "CALL":
                    {
                        string funcMangledName = TrimName(tac.arg1);
                        int argCount = int.Parse(tac.arg2);

                        this.callStack[this.callStack.Top + 1].returnPtr = new Tuple<int, int>(this.currUnit, (this.curr + 1));

                        string funcFinalName = funcMangledName;

                        string label = "entry:" + funcFinalName;

                        var jumpAddr = mainUnit.QueryLabel(label, currUnit);
                        this.curr = jumpAddr.Item2;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "MCALL":
                    {
                        string funcMangledName = TrimName(tac.arg1);
                        int argCount = int.Parse(tac.arg2);

                        this.callStack[this.callStack.Top + 1].returnPtr = new Tuple<int, int>(this.currUnit, (this.curr + 1));


                        //运行时多态  
                        //获取this参数  
                        if (this.callStack[this.callStack.Top + 1].args.Count == 0) throw new RuntimeException(GetCurrentCode(), "成员方法调用没有this参数！");
                        var arg_this = this.callStack[this.callStack.Top + 1].args.Peek();
                        if (arg_this.Type != GizType.GizObject) throw new RuntimeException(GetCurrentCode(), "成员方法调用没有this参数！(第一个参数不是this参数)");
                        string trueType = (this.DeReference(arg_this.AsPtr)as GizObject).truetype;


                        //新方法：虚函数表vtable    
                        var vrec = (this.DeReference(arg_this.AsPtr) as GizObject).vtable.Query(funcMangledName);
                        string funcFinalName = vrec.funcfullname;


                        string label = "entry:" + funcFinalName;

                        var jumpAddr = mainUnit.QueryLabel(label, currUnit);
                        this.curr = jumpAddr.Item2;
                        this.currUnit = jumpAddr.Item1;

                        return;
                    }
                case "=":
                    {
                        SetValue(tac.arg1, GetValue(tac.arg2)); 
                    }
                    break;
                case "+=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("+", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "-=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("-", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "*=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("*", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "/=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("/", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "%=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("%", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "+":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("+", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "-":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("-", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "*":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("*", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "/":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("/", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "%":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("%", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "ALLOC":
                    {
                        string className = tac.arg2;
                        SymbolTable tableFound; 
                        var rec = Query(className, out tableFound);
                        GizObject newfanObj = new GizObject(className, rec.envPtr, mainUnit.QueryVTable(className));
                        var ptr = heap.Alloc(newfanObj);
                        SetValue(tac.arg1, Value.FromGizObjectPtr(ptr));
                    }
                    break;
                case "DEL":
                    {
                        SetValue(tac.arg1, Value.Void);
                    }
                    break;
                case "ALLOC_ARRAY":
                    {
                        Value[] arr = new Value[GetValue(tac.arg2).AsInt];
                        var ptr = heap.Alloc(arr);
                        SetValue(tac.arg1, Value.FromArrayPtr(ptr));
                    }
                    break;
                case "IF_FALSE_JUMP":
                    {
                        bool conditionTrue = GetValue(tac.arg1).AsBool;
                        if(conditionTrue == false)
                        {
                            var jumpAddr = mainUnit.QueryLabel(tac.arg2, currUnit);
                            
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
                        SetValue(tac.arg1, caculator.CalBinary("<", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "<=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("<=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case ">":
                    {
                        SetValue(tac.arg1, caculator.CalBinary(">", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case ">=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary(">=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "==":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("==", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "!=":
                    {
                        SetValue(tac.arg1, caculator.CalBinary("!=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "++":
                    {
                        SetValue(tac.arg1, GetValue(tac.arg1) + 1);
                    }
                    break;
                case "--":
                    {
                        SetValue(tac.arg1, GetValue(tac.arg1) - 1);
                    }
                    break;
                case "CAST":
                    {
                        SetValue(tac.arg1, caculator.Cast(tac.arg2, GetValue(tac.arg3), this));
                    }
                    break;
            }


            this.curr++;
        }

        // ----------------------- Interfaces ---------------------------
        public TAC GetCurrentCode()
        {
            return mainUnit.QueryCode(currUnit, curr);
        }


        // ----------------------- Operations ---------------------------

        private Value GetValue(string str)
        {
            return Access(str, write:false);
        }

        private void SetValue(string str, Value v)
        {
            Access(str, write: true, value: v);
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
                return this.heap.data[(int)ptr];
            }
            else
            {
                var v = mainUnit.QueryUnit(this.currUnit).ReadConst((int)(-ptr));
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

        private Value Access(string str, bool write, Value value = default)
        {
            //虚拟寄存器  
            if(str == "RET")
            {
                if(write)
                {
                    retRegister = value;
                }
                else
                {
                    return retRegister;
                }
                
            }
            //符号表查找  
            else if (str[0] == '[')
            {
                string expr = TrimName(str);
                return AccessExprRecursive(expr, write, value);
            }
            //字面量  
            else if(str.StartsWith("LIT") && str.Contains(':') && write == false)
            {
                return AccessLiteral(str);

                throw new RuntimeException(GetCurrentCode(), "未知的参数：" + str);
            }
            //常量(只读)  
            else if (str.StartsWith("CONST") && str.Contains(':') && write == false)
            {
                return AccessConst(str);

                throw new RuntimeException(GetCurrentCode(), "未知的参数：" + str);
            }

            throw new RuntimeException(GetCurrentCode(), "无法识别：" + str);
        }

        private Value AccessExprRecursive(string expr, bool write, Value value = default)
        {
            bool isLiterial = false;
            bool isConst = false;
            bool isMemberAccess = false;
            bool isElementAccess = false;
            if(expr[expr.Length - 1] == ']')
            {
                isElementAccess = true;
            }
            else if(expr.Contains('.'))
            {
                isMemberAccess = true;
            }
            else if(expr.Contains(':'))
            {
                if (expr.StartsWith("CONST"))
                    isConst = true;
                else if (expr.StartsWith("LIT"))
                    isLiterial = true;
            }

            //字面量访问  
            if(isLiterial)
            {
                if(write)
                {
                    throw new RuntimeException(GetCurrentCode(), "不能对字面量赋值！");
                }
                else
                {
                    return AccessLiteral(expr);

                }
            }
            //常量  
            else if (isConst)
            {
                if (write)
                {
                    throw new RuntimeException(GetCurrentCode(), "不能对常量赋值！");
                }
                else
                {
                    return AccessConst(expr);

                }
            }
            //普通变量访问  
            else if (isMemberAccess == false && isElementAccess == false)
            {
                string name = expr;
                var varible = AccessVariable(name, write: write, value);
                return varible;
            }
            //数组元素访问    
            else if (isElementAccess == true)
            {
                int lbracket = expr.IndexOf('[');
                int rbracket = expr.IndexOf(']');

                string arrVarExpr = expr.Substring(0, lbracket);
                var array = AccessExprRecursive(arrVarExpr, write: false);
                string idxStr = expr.Substring(lbracket, (rbracket - lbracket) + 1);

                int idx = GetValue(idxStr).AsInt;

                if (write)
                {
                    var arr = (Value[])(this.DeReference(array.AsPtr));
                    arr[idx] = value;
                    return Value.Void;
                }
                else
                {
                    var arr = (Value[])(this.DeReference(array.AsPtr));
                    return arr[idx];
                }
            }
            //对象成员访问  
            else if (isMemberAccess == true)
            {
                int lDot = expr.LastIndexOf('.');
                var variableExpr = expr.Substring(0, lDot);
                Value obj = AccessExprRecursive(variableExpr, write: false);

                string fieldName = expr.Split('.')[1];

                if ((obj.IsVoid)) throw new RuntimeException(GetCurrentCode(), "找不到对象" + variableExpr + "！");
                if (obj.Type != GizType.GizObject) throw new RuntimeException(GetCurrentCode(), "对象" + variableExpr + "不是FanObject类型！而是" + obj.GetType().Name);


                if (write)
                {
                    (this.DeReference(obj.AsPtr) as GizObject).fields[fieldName] = value;
                    if (Compiler.enableLogScriptEngine) Log("对象" + variableExpr + "(InstanceID:" + (this.DeReference(obj.AsPtr) as GizObject).instanceID + ")字段" + fieldName + "写入：" + value.ToString());
                    return Value.Void;
                }
                else
                {
                    if ((this.DeReference(obj.AsPtr) as GizObject).fields.ContainsKey(fieldName) == false) throw new RuntimeException(GetCurrentCode(), "对象" + variableExpr + "字段未初始化" + fieldName);
                    return (this.DeReference(obj.AsPtr) as GizObject).fields[fieldName];
                }
            }
            return Value.Void;
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
                    if (idxInParams < 0) throw new RuntimeException(GetCurrentCode(), "形参列表中未找到：" + name);

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
                            throw new RuntimeException(GetCurrentCode(), "局部变量 " + name + "还未初始化！");//TODO:判断用户是否成员变量没加this.  

                        return currentFrame.localVariables[name];
                    }
                }
                else
                {
                    throw new RuntimeException(GetCurrentCode(), "不是参数也不是局部变量！：" + name);
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
                        throw new RuntimeException(GetCurrentCode(), "全局变量 " + name + " 还未初始化！");

                    return mainUnit.ReadGlobalVar(name);
                }
            }
            else
            {
                throw new RuntimeException(GetCurrentCode(), "栈帧和全局符号都不包含：" + name);
            }
        }

        private Value AccessLiteral(string str)
        {
            int splitIndex = str.IndexOf(':');
            string baseType = str.Substring(0, splitIndex);
            string lex = str.Substring(splitIndex + 1);
            switch (baseType)
            {
                case "LITNULL": return Value.Void;
                case "LITBOOL": return bool.Parse(lex);
                case "LITINT": return int.Parse(lex);
                case "LITFLOAT": return float.Parse(lex.Substring(0, lex.Length - 1));//去除F标记  
                case "LITDOUBLE": return double.Parse(lex.Substring(0, lex.Length - 1));//去除F标记  
                case "LITCHAR": return lex[1];
                //case "LITSTRING": return Value.Void;//字符串字面量已经移除  
            }
            throw new RuntimeException(GetCurrentCode(), "未知的字面量" + str + "！");
        }
        private Value AccessConst(string str)
        {
            int splitIndex = str.IndexOf(':');
            string baseType = str.Substring(0, splitIndex);
            string lex = str.Substring(splitIndex + 1);
            switch (baseType)
            {
                case "CONSTSTRING":
                    {
                        //Console.WriteLine("字符串常量：" + lex);
                        int ptr = int.Parse(lex);
                        return Value.FromConstStringPtr(ptr);
                    }
            }
            throw new RuntimeException(GetCurrentCode(), "未知的常量" + str + "！");
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
            foreach (var lib in this.mainUnit.dependencies)
            {
                var rec = lib.globalScope.env.GetRecord(symbolName);
                if (rec != null)
                {
                    tableFound = lib.globalScope.env;
                    return rec;
                }
            }


            Console.WriteLine("在符号表链中未找到：" + symbolName + "符号表链：" + string.Concat(envStack.ToList().Select(e => e.name + " - ")) + " 当前行数：" + curr);
            tableFound = null;
            return null;
        }

        private string TrimName(string input)
        {
            if (input[0] != ('[')) throw new RuntimeException(GetCurrentCode(), "无法Trim:" + input);

            return input.Substring(1, input.Length - 2);
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
            Console.WriteLine("ScriptEngine >>" + content);
        }


        private System.Diagnostics.Stopwatch profileW = new System.Diagnostics.Stopwatch();
        private string profileName = "";
        private void ProfileBegin(string name)
        {
            profileName = name;
            profileW.Restart();
        }
        private void ProfileEnd()
        {
            profileW.Stop();
            Console.WriteLine(profileName + ": " + profileW.ElapsedTicks);
        }

        private void PrintCallStack()
        {
            for (int i = 0; i <= callStack.Top; ++i)
            {
                Console.WriteLine(i.ToString() + "(top:" + callStack.Top + ")" + new string('-', 20));
                Console.WriteLine("Arguments:");
                foreach(var k in callStack[i].args.ToList())
                {
                    Console.WriteLine(k.GetType().Name + ":" + k.ToString());
                }
                Console.WriteLine("LOCALs:");
                foreach (var k in callStack[i].localVariables.Keys)
                {
                    var v = callStack[i].localVariables[k];
                    Console.WriteLine(v.GetType().Name + ":" + v.ToString());
                }
                Console.WriteLine(new string('-', 20));
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

        public Value CalNegtive(Value v)
        {
            switch(v.Type)
            {
                case GizType.Int: return -(v.AsInt);
                case GizType.Float: return -(v.AsFloat);
                default: throw new RuntimeException(engine.GetCurrentCode(), v.Type + "类型不能进行求负数操作!");
            }
        }
        public Value CalBinary(string op, Value v1, Value v2)
        {
            //固有的操作符重载    
            if(v1.Type == GizType.String && v2.Type == GizType.String)
            {
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
                                    throw new GizboxException("不能从GizboxObject转换" + toType + "！");
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
                                    throw new GizboxException("不能从GizboxArray转换" + toType + "！");
                                }
                            default:
                                {
                                    throw new GizboxException("数组转换为其他类型未实现！");
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



