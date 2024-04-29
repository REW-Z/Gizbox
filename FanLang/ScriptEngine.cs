using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanLang;
using FanLang.IR;


namespace FanLang.ScriptEngine
{
    //全局内存区  
    public class GlobalMemory
    {
        public Dictionary<string, object> data = new Dictionary<string, object>();
    }
    //堆区    
    public class HeapMemory
    {
        public Dictionary<string, object> data = new Dictionary<string, object>();
    }
    //虚拟栈帧（活动记录）  
    public class Frame
    {
        public FanLang.Stack<object> args = new FanLang.Stack<object>();//由Caller压栈(从后往前)  
        public int returnPtr;//返回地址  
        public Dictionary<string, object> localVariables = new Dictionary<string, object>();//局部变量和临时变量  
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

    // Fan对象  
    public class FanObject
    {
        private static int currentMaxId = 0;

        public int instanceID = 0;
        public Dictionary<string, object> fields = new Dictionary<string, object>();
        
        public FanObject()
        {
            this.instanceID = currentMaxId++;
        }
        public override string ToString()
        {
            return "FanObect(instanceID:" + this.instanceID + ")";
        }
    }




    //脚本引擎  
    public class ScriptEngine
    {
        //编译器上下文  
        public Compiler compilerContext;

        //符号表堆栈  
        public FanLang.Stack<SymbolTable> envStack;

        //中间代码  
        public FanLang.IR.IntermediateCodes ir;
        private int current;

        //全局内存  
        private GlobalMemory globalMemory = new GlobalMemory();

        //堆区    
        private HeapMemory heap = new HeapMemory();

        //调用堆栈  
        private CallStack callStack = new CallStack();

        //返回值寄存器（虚拟）(实际在x86架构通常为EAX寄存器 x86-64架构通常为RAX寄存器)
        private object retRegister = null;

        //临时数据  
        private List<SymbolTable> envsHitTemp = new List<SymbolTable>();//所在的所有符号表  
        private int previous = 0;

        //外部调用相关  
        private static Dictionary<string, Type> typeCache = new Dictionary<string, Type>();



        public ScriptEngine(Compiler compiler, FanLang.IR.IntermediateCodes ir)
        {
            this.compilerContext = compiler;
            this.ir = ir;

            List<string> distinceOps = new List<string>();
            foreach(var tac in ir.codes)
            {
                if(distinceOps.Contains(tac.op) == false)
                {
                    distinceOps.Add(tac.op);
                }
            }
            EngineLog("不重复的指令列表：");
            foreach(var op in distinceOps)
            {
                EngineLog(op);
            }
            
        }

        public void Execute()
        {
            System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            //调用堆栈  
            this.callStack = new CallStack();

            //符号表链    
            this.envStack = new Stack<SymbolTable>();
            ResetEnv();
            
            //入口
            this.current = ir.codeEntry;

            //留空  
            Console.WriteLine(new string('\n', 20));

            while(this.current < ir.codes.Count)
            {
                Interpret(ir.codes[this.current]);
            }

            watch.Stop();

            Console.WriteLine(new string('\n', 3));
            Console.WriteLine("执行时间：" + watch.ElapsedMilliseconds + "ms");
        }

        /*

         */

        private void Interpret(TAC tac)
        {
            //设置符号表链（用于解析符号）  
            {
                if (this.ir.NeedRefreshStack(previous, current))
                {
                    ResetEnv();
                }
                previous = current;
            }
            

            //开始指向本条指令  
            EngineLog(new string('>', 10) + tac.ToExpression(showlabel:false));
            switch(tac.op)
            {
                case "": break;
                case "JUMP":
                    {
                        this.current = ir.labelDic[tac.arg1];
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
                        current = this.callStack[callStack.Top].returnPtr;

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
                        current = this.callStack[callStack.Top].returnPtr;

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
                        int exitLine = ir.labelDic["exit:" + envStack.Peek().name];
                        int endLine = exitLine - 1;    
                        
                        if(ir.codes[endLine].op != "METHOD_END" && ir.codes[endLine].op != "FUNC_END")
                        {
                            throw new Exception("函数或方法的END没有紧接exit标签");  
                        }
                        this.current = endLine;

                        return;
                    }
                case "EXTERN_IMPL":
                    {
                        List<object> arguments = callStack[callStack.Top].args.ToList();
                        arguments.Reverse();

                        var result = ExternCall(tac.arg1, arguments);

                        retRegister = result;

                        int exitLine = ir.labelDic["exit:" + envStack.Peek().name];
                        int endLine = exitLine - 1;

                        if (ir.codes[endLine].op != "METHOD_END" && ir.codes[endLine].op != "FUNC_END")
                        {
                            throw new Exception("函数或方法的END没有紧接exit标签");
                        }
                        this.current = endLine;

                        return;
                    }
                    break;
                case "PARAM":
                    {
                        object arg = GetValue(tac.arg1); if (arg == null) throw new Exception("null实参");
                        this.callStack[this.callStack.Top + 1].args.Push(arg);
                    }
                    break;
                case "CALL":
                    {
                        string funcFullName = TrimName(tac.arg1);
                        int argCount = int.Parse(tac.arg2);

                        this.callStack[this.callStack.Top + 1].returnPtr = (current + 1);

                        string label = "entry:" + funcFullName;

                        if (ir.labelDic.ContainsKey(label))
                        {
                            current = ir.labelDic[label];
                        }
                        

                        return;
                    }
                case "=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, GetValue(tac.arg2)); 
                    }
                    break;
                case "+=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, Calculator.CalBinary("+", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "-=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, Calculator.CalBinary("-", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "*=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, Calculator.CalBinary("*", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "/=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, Calculator.CalBinary("/", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "%=":
                    {
                        if (tac.arg3 != null) throw new Exception("赋值语句只有包含两个地址！");

                        SetValue(tac.arg1, Calculator.CalBinary("%", GetValue(tac.arg1), GetValue(tac.arg2)));
                    }
                    break;
                case "+":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("+", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "-":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("-", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "*":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("*", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "/":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("/", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "%":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("%", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "ALLOC":
                    {
                        FanObject newfanObj = new FanObject();
                        this.heap.data["1"] = newfanObj;

                        SetValue(tac.arg1, newfanObj);
                    }
                    break;
                case "IF_FALSE_JUMP":
                    {
                        bool conditionTrue = (bool)GetValue(tac.arg1);
                        if(conditionTrue == false)
                        {
                            current = ir.labelDic[tac.arg2];
                            return;
                        }
                        else
                        {
                            break;
                        }
                    }
                case "<":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("<", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "<=":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("<=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case ">":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary(">", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case ">=":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary(">=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "==":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("==", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "!=":
                    {
                        SetValue(tac.arg1, Calculator.CalBinary("!=", GetValue(tac.arg2), GetValue(tac.arg3)));
                    }
                    break;
                case "++":
                    {
                        SetValue(tac.arg1, (int)GetValue(tac.arg1) + 1);
                    }
                    break;
                case "--":
                    {
                        SetValue(tac.arg1, (int)GetValue(tac.arg1) - 1);
                    }
                    break;
                case "CAST":
                    {
                        SetValue(tac.arg1, Calculator.Cast(tac.arg2, GetValue(tac.arg3)));
                    }
                    break;
            }


            this.current++;
        }


        private void ResetEnv()
        {
            this.ir.EnvHits(current, this.envsHitTemp);
            this.envsHitTemp.Sort((e1, e2) => e1.depth - e2.depth);
            this.envStack.Clear();
            foreach (var env in this.envsHitTemp)
            {
                this.envStack.Push(env);
            }
        }

        private SymbolTable.Record Query(string symbolName, out SymbolTable tableFound)
        {
            for (int i = envStack.Count - 1; i > -1; --i)
            {
                if(envStack[i].ContainRecordName(symbolName))
                {
                    tableFound = envStack[i];
                    return envStack[i].GetRecord(symbolName);
                }
            }

            Console.WriteLine("在符号表链中未找到：" + symbolName + "符号表链：" + string.Concat(envStack.ToList().Select(e => e.name + " - ")) + " 当前行数：" + current);
            tableFound = null;
            return null;
        }

        private object GetValue(string str)
        {
            return AccessData(str, write:false);
        }
        private void SetValue(string str, object v)
        {
            AccessData(str, write: true, value: v);
        }

        private object AccessData(string str, bool write, object value = null)
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
                bool isAccess = expr.Contains('.');

                //普通变量访问  
                if (isAccess == false)
                {
                    string name = expr;

                    return AccessVariable(name, write:write, value);
                }
                //对象成员访问  
                else
                {
                    string name = expr.Split('.')[0];

                    object obj = AccessVariable(name, write:false);

                    string fieldName = expr.Split('.')[1];

                    if (obj == null) throw new Exception("找不到对象" + name + "！");
                    if ((obj is FanObject) == false) throw new Exception("对象" + name + "不是FanObject类型！而是" + obj.GetType().Name);

                    if (write)
                    {
                        (obj as FanObject).fields[fieldName] = value;
                        EngineLog("对象" + name + "(InstanceID:" + (obj as FanObject).instanceID + ")字段" + fieldName + "写入：" + (value != null ? value : "null"));
                        return null;
                    }
                    else
                    {
                        if ((obj as FanObject).fields.ContainsKey(fieldName) == false) throw new Exception("对象" + name + "字段未初始化" + fieldName);
                        return (obj as FanObject).fields[fieldName];
                    }
                }
            }
            //常量(只读)  
            else if(str.Contains(':') && write == false)
            {
                string baseType = str.Split(':')[0];
                string lex = str.Split(':')[1];
                switch (baseType)
                {
                    case "LITBOOL": return bool.Parse(lex);
                    case "LITINT": return int.Parse(lex);
                    case "LITFLOAT": return float.Parse(lex.Substring(0, lex.Length - 1));//去除F标记  
                    case "LITSTRING": return lex.Substring(1, lex.Length - 2);//去除双引号
                }

                throw new Exception("未知的参数：" + str);
            }

            throw new Exception("无法识别：" + str);
        }

        private object AccessVariable(string varname, bool write, object value = null)
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
                    if (idxInParams < 0) throw new Exception("形参列表中未找到：" + name);

                    if (write)
                    {
                        currentFrame.args[currentFrame.args.Top - idxInParams] = value;
                        EngineLog("参数" + name + "写入：" + (value != null ? value : "null"));
                        return null;
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
                        EngineLog("局部变量" + name + "写入：" + (value != null ? value : "null"));
                        return null;
                    }
                    else
                    {
                        if (currentFrame.localVariables.ContainsKey(name) == false)
                            throw new Exception("局部变量 " + name + "还未初始化！");

                        return currentFrame.localVariables[name];
                    }
                }
                else
                {
                    throw new Exception("不是参数也不是局部变量！：" + name);
                }
            }
            //查找全局符号表  
            else if (compilerContext.globalSymbolTable.ContainRecordName(name))
            {
                var gloabalRec = compilerContext.globalSymbolTable.GetRecord(name);

                if (write)
                {
                    globalMemory.data[name] = value;
                    EngineLog("全局变量" + name + "写入：" + (value != null ? value : "null"));
                    return null;
                }
                else
                {
                    if (globalMemory.data.ContainsKey(name) == false)
                        throw new Exception("全局变量 " + name + " 还未初始化！");

                    return globalMemory.data[name];
                }
            }
            else
            {
                throw new Exception("栈帧和全局符号都不包含：" + name);
            }
        }


        private string TrimName(string input)
        {
            if (input[0] != ('[')) throw new Exception("无法Trim:" + input);

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


        private static void EngineLog(object content)
        {
            return;
            Console.WriteLine("ScriptEngine >>" + content);
        }

        public static void Log(object content)
        {
            Console.WriteLine("FanLang >>" + content);
        }

        private static object ExternCall(string funcName, List<object> argments)
        {
            //静态方法调用  
            if(funcName.Contains('_'))
            {
                int idx = funcName.IndexOf('_');
                string className = funcName.Substring(0, idx);
                string staticMemberFuncName = funcName.Substring(idx + 1);
                
                Type targetType = null;
                if(typeCache.ContainsKey(className))
                {
                    targetType = typeCache[className];
                }
                else
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type type = null;
                        foreach(var t in asm.GetTypes())
                        {
                            if(t.Name == className)
                            {
                                type = t;
                                targetType = t;
                                typeCache[className] = t;
                                break;
                            }
                        }
                        if (type != null) break;
                    }
                }

                if(targetType != null)
                {
                    var methodInfoOverloads = targetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .Where(m => m.Name == staticMemberFuncName);

                    foreach(var overload in methodInfoOverloads)
                    {
                        var parameters = overload.GetParameters();
                        if (parameters.Length != argments.Count) continue;

                        bool nextOverload = false;
                        for (int i = 0; i < parameters.Length; ++i)
                        {
                            if(parameters[i].ParameterType != argments[i].GetType())
                            {
                                nextOverload = true;
                                break;
                            }
                        }
                        if (nextOverload) continue;

                        return overload.Invoke(null, argments.ToArray());
                    }
                }
                else
                {
                    throw new Exception("找不到静态方法：" + staticMemberFuncName);
                }
            }
            //预定义方法调用  
            else
            {
                var funcInfo = typeof(ScriptEngine).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == funcName);
                if (funcInfo != null)
                {
                    return funcInfo.Invoke(null, argments.ToArray());
                }
            }

            return null;
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

        private static bool IsNumberType(Type type)
        {
            if (type == typeof(int) || type == typeof(float))
                return true;

            return false;
        }
        public static object CalNegtive(object v)
        {
            Type t = v.GetType();
            if (IsNumberType(t))
            {
                if (t == typeof(float))
                {
                    return -(float)v;
                }
                else if (t == typeof(int))
                {
                    return -(int)v;
                }
            }
            return null;
        }
        public static object CalBinary(string op, object v1, object v2)
        {
            Type t1 = v1.GetType();
            Type t2 = v2.GetType();

            if (t1 != t2) throw new Exception("类型不同的值不能进行二元运算！");

            //数字计算  
            if (IsNumberType(t1))
            {
                //算数运算 
                if(arithmeticOperators.Contains(op))
                {
                    if (t1 == typeof(float))
                    {
                        return CalBinaryFloat(op, (float)v1, (float)v2);
                    } 
                    else if (t1 == typeof(int))
                    {
                        return CalBinaryInt(op, (int)v1, (int)v2);
                    }
                }
                //布尔运算 
                else if (comparisonOperators.Contains(op))
                {
                    if (t1 == typeof(float))
                    {
                        return CalCompareFloat(op, (float)v1, (float)v2);
                    }
                    else if (t1 == typeof(int))
                    {
                        return CalCompareInt(op, (int)v1, (int)v2);
                    }
                }
            }
            else if ((t1 == typeof(string) || t2 == typeof(string)) && op == "+")
            {
                return v1.ToString() + v2.ToString();
            }
            return null;
        }

        public static object CalBinaryInt(string op, int v1, int v2)
        {
            switch(op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }
        public static object CalBinaryFloat(string op, float v1, float v2)
        {
            switch (op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }


        public static object CalCompareInt(string op, int v1, int v2)
        {
            switch (op)
            {
                case ">": return v1 > v2;
                case "<": return v1 < v2;
                case ">=": return v1 >= v2;
                case "<=": return v1 <= v2;
                case "==": return v1 == v2;
                case "!=": return v1 != v2;
                default: return null;
            }
        }
        public static object CalCompareFloat(string op, float v1, float v2)
        {
            switch (op)
            {
                case ">": return v1 > v2;
                case "<": return v1 < v2;
                case ">=": return v1 >= v2;
                case "<=": return v1 <= v2;
                case "==": return v1 == v2;
                case "!=": return v1 != v2;
                default: return null;
            }
        }

        public static object Cast(string toType, object val)
        {
            switch (toType)
            {
                case "bool": return System.Convert.ToBoolean(val);
                case "int": return System.Convert.ToInt32(val);
                case "float": return System.Convert.ToSingle(val);
                case "string": return val.ToString();
                default:return null;
            }
        }
    }
}
