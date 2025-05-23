using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Gizbox;
using Gizbox.ScriptEngineV2;
using Gizbox.IR;

#if DEBUG
using System.Runtime.InteropServices;
#endif

namespace Gizbox.ScriptEngineV2
{
    public class Operand
    {
        public string str;

        public Operand(string argStr)
        {
            this.str = argStr;
        }

        public static string GetString(Operand operand)
        {
            if((operand is OperandString) == false)
                throw new RuntimeException(ExceptioName.ScriptRuntimeError, null, "not StringOperand type !");
            return ((OperandString)operand).str;
        }

        public static Operand Parse(string argStr)
        {
            if(string.IsNullOrEmpty(argStr))
            {
                return null;
            }
            else if(argStr == "RET")
            {
                return new OperandRegister(argStr);
            }
            else if(argStr.StartsWith("CONST"))
            {
                return new OperandConst(argStr);
            }
            else if(argStr.StartsWith("LIT"))
            {
                return new OperandLiteralValue(argStr);
            }
            else if(argStr[0] == '[' && argStr[argStr.Length - 1] == ']')
            {
                string expr = argStr.Substring(1, argStr.Length - 2);
                if(expr[expr.Length - 1] == ']')
                {
                    return new OperandElementAccess(expr);
                }
                else if(expr.Contains("."))
                {
                    return new OperandMemberAccess(expr);
                }
                else
                {
                    return new OperandVariable(expr);
                }
            }
            else
            {
                return new OperandString(argStr);
            }

            throw new GizboxException(ExceptioName.TacError);
        }
    }
    public class OperandRegister : Operand
    {
        public string registerName;
        public OperandRegister(string str) : base(str)
        {
            this.registerName = str;
        }
    }
    public class OperandString : Operand
    {
        public OperandString(string s) : base(s)
        {
        }
    }
    public class OperandConst : Operand
    {
        public long oldPtr;
        public long linkedPtr;

        public string giztype;

        public OperandConst(string str) : base(str)
        {
            int splitIndex = str.IndexOf(':');
            string baseType = str.Substring(0, splitIndex);
            string lex = str.Substring(splitIndex + 1);
            switch(baseType)
            {
                case "CONSTSTRING":
                    {
                        this.oldPtr = long.Parse(lex);
                        this.giztype = "string";
                    }
                    break;
                default:
                    throw new GizboxException(ExceptioName.UnknownConstant, str);
            }
        }
    }
    public class OperandLiteralValue : Operand
    {
        public Value val;
        public OperandLiteralValue(string str) : base(str)
        {
            int splitIndex = str.IndexOf(':');
            string baseType = str.Substring(0, splitIndex);
            string lex = str.Substring(splitIndex + 1);
            switch(baseType)
            {
                case "LITNULL":
                    this.val = Value.NULL;
                    break;
                case "LITBOOL":
                    this.val = bool.Parse(lex);
                    break;
                case "LITINT":
                    this.val = int.Parse(lex);
                    break;
                case "LITFLOAT":
                    this.val = float.Parse(lex.Substring(0, lex.Length - 1));
                    break;//去除F标记  
                case "LITDOUBLE":
                    this.val = double.Parse(lex.Substring(0, lex.Length - 1));
                    break;//去除F标记  
                case "LITCHAR":
                    this.val = lex[1];
                    break;
                //case "LITSTRING": return Value.Void;//字符串字面量已经移除
                default:
                    throw new GizboxException(ExceptioName.UnknownLiteral, str);
            }
        }
    }
    public class OperandElementAccess : Operand
    {
        public Operand array;
        public Operand index;

        public OperandElementAccess(string expr) : base(expr)
        {
            int lbracket = expr.IndexOf('[');
            int rbracket = expr.IndexOf(']');

            string arrVarExpr = expr.Substring(0, lbracket);
            string idxExpr = expr.Substring(lbracket + 1, (rbracket - lbracket) - 1);


            if(arrVarExpr[arrVarExpr.Length - 1] == ']')
            {
                this.array = new OperandElementAccess(arrVarExpr);
            }
            else if(arrVarExpr.Contains("."))
            {
                this.array = new OperandMemberAccess(arrVarExpr);
            }
            else
            {
                this.array = new OperandVariable(arrVarExpr);
            }

            this.index = Operand.Parse(idxExpr);
        }
    }
    public class OperandMemberAccess : Operand
    {
        public Operand obj;
        public string fieldname;

        public OperandMemberAccess(string expr) : base(expr)
        {
            int lastDot = expr.LastIndexOf('.');
            var variableExpr = expr.Substring(0, lastDot);
            string fieldName = expr.Substring(lastDot + 1);


            if(variableExpr[variableExpr.Length - 1] == ']')
            {
                this.obj = new OperandElementAccess(variableExpr);
            }
            else if(variableExpr.Contains("."))
            {
                this.obj = new OperandMemberAccess(variableExpr);
            }
            else
            {
                this.obj = new OperandVariable(variableExpr);
            }

            this.fieldname = fieldName;
        }
    }
    public class OperandVariable : Operand
    {
        public string name;
        public OperandVariable(string str) : base(str)
        {
            this.name = str;
        }
    }





    public class RuntimeCodeV2
    {
        public string labelpart0;
        public string labelpart1;

        public string op;

        public Operand arg1;
        public Operand arg2;
        public Operand arg3;

        public RuntimeCodeV2(TAC tac)
        {
            if(string.IsNullOrEmpty(tac.label))
            {
                labelpart0 = "";
                labelpart1 = "";
            }
            else if(tac.label.StartsWith("entry:"))
            {
                labelpart0 = "entry";
                labelpart1 = tac.label.Substring(6);
            }
            else if(tac.label.StartsWith("exit:"))
            {
                labelpart0 = "exit";
                labelpart1 = tac.label.Substring(5);
            }
            else
            {
                labelpart0 = tac.label;
            }

            this.op = tac.op;

            this.arg1 = Operand.Parse(tac.arg1);
            this.arg2 = Operand.Parse(tac.arg2);
            this.arg3 = Operand.Parse(tac.arg3);
        }

        public string ToExpression(bool showlabel = true)
        {
            string str = "";
            if(showlabel)
            {
                if(string.IsNullOrEmpty(labelpart0) == false)
                {
                    str += labelpart0;
                }
                if(string.IsNullOrEmpty(labelpart1) == false)
                {
                    str += ":" + labelpart1;
                }
            }

            str += "\t";
            str += op;

            if(arg1 != null)
            {
                str += "\t";
                str += arg1.str;
            }
            if(arg2 != null)
            {
                str += "\t";
                str += arg2.str;
            }
            if(arg3 != null)
            {
                str += "\t";
                str += arg3.str;
            }
            return str;
        }
    }


    public class RuntimeAddr
    {
        public const int align = 16;//x86-64

        public AddrType addrType;

        public long addrOffset;

        public long addrReal;//大结构体  
    }


    public class RuntimeUnitV2
    {
        public const long __constdataOffset = 10000000000L;

        public int id = -1;
        public string name;
        public List<RuntimeUnitV2> directlyDependencies = new List<RuntimeUnitV2>();
        public List<RuntimeCodeV2> codes = new List<RuntimeCodeV2>();
        public int[] scopeStatusArr;
        public List<Scope> scopes = new List<Scope>();
        public Scope globalScope;
        public Dictionary<string, VTable> vtables = new Dictionary<string, VTable>();
        private Dictionary<string, int> label2Line = new Dictionary<string, int>();
        public Dictionary<int, Gizbox.GStack<SymbolTable>> stackDic;
        public List<object> constData = new List<object>();


        private Dictionary<string, int> entryLabels = new Dictionary<string, int>();
        private Dictionary<string, int> exitLabels = new Dictionary<string, int>();

        public Dictionary<SymbolTable.Record, RuntimeAddr> symbolInfoCacheDict = new();

        public List<RuntimeUnitV2> allUnits = new List<RuntimeUnitV2>();
        public Dictionary<string, Value> globalData = new Dictionary<string, Value>();




        //构造    
        public RuntimeUnitV2(ScriptEngineV2 engineContext, ILUnit ilunit)
        {
            this.name = ilunit.name;

            foreach(var depName in ilunit.dependencies)
            {
                var libIr = engineContext.LoadLib(depName);
                this.directlyDependencies.Add(new RuntimeUnitV2(engineContext, libIr));
            }

            foreach(var tac in ilunit.codes)
            {
                var newCode = new RuntimeCodeV2(tac);
                this.codes.Add(newCode);
            }

            this.scopeStatusArr = ilunit.scopeStatusArr;
            this.scopes = ilunit.scopes;
            this.globalScope = ilunit.globalScope;
            this.vtables = ilunit.vtables;
            this.label2Line = ilunit.label2Line;
            this.stackDic = ilunit.stackDic;
            this.constData = ilunit.constData;


            foreach(var kv in ilunit.label2Line)
            {
                if(kv.Key.StartsWith("entry:"))
                {
                    var entryKey = kv.Key.Substring(6);
                    entryLabels[entryKey] = kv.Value;
                }
                else if(kv.Key.StartsWith("exit:"))
                {
                    var exitKey = kv.Key.Substring(5);
                    exitLabels[exitKey] = kv.Value;
                }
            }

            //Init Symbol infos  
            InitSymbolInfos();
        }

        private void InitSymbolInfos()
        {
            foreach(var scope in scopes)
            {
                switch(scope.env.tableCatagory)
                {
                    //类成员内存布局  
                    case SymbolTable.TableCatagory.ClassScope:
                        {
                            var fieldRecs = scope.env.records.Where(pair => pair.Value.category == SymbolTable.RecordCatagory.Variable).Select(pair => pair.Value).ToArray();
                            (int size, int align)[] sizeAlignInfos = fieldRecs.Select(rec => GetSizeAndAlign(rec)).ToArray();
                            long[] addrs;
                            long spmovement;
                            SimMemUtility.MemLayout(0, sizeAlignInfos, out addrs, out spmovement);
                            for(int i = 0; i < fieldRecs.Length; i++)
                            {
                                symbolInfoCacheDict[fieldRecs[i]] = new RuntimeAddr()
                                {
                                    addrType = AddrType.Stack,
                                    addrOffset = addrs[i],
                                };
                            }
                        }
                        break;
                    //局部变量内存布局
                    case SymbolTable.TableCatagory.FuncScope:
                        {
                            var parameters = scope.env.records.Where(p => p.Value.category == SymbolTable.RecordCatagory.Param).Select(p => p.Value).ToArray();
                            var localVariables = scope.env.records.Where(p => p.Value.category == SymbolTable.RecordCatagory.Variable).Select(p => p.Value).ToArray();

                            AddrType regTemp1 = AddrType.RCX;
                            AddrType regTemp2 = AddrType.XMM0;

                            foreach(var param in parameters)
                            {
                                if(ShouldAllocOnRegXXM(param) && regTemp2 <= AddrType.XMM3)
                                {
                                    symbolInfoCacheDict[param] = new RuntimeAddr()
                                    {
                                        addrType = regTemp2,
                                    };
                                    regTemp2 += 1;
                                }
                                else if(reg)
                                {
                                    symbolInfoCacheDict[param] = new RuntimeAddr()
                                    {
                                        addrType = regTemp1,
                                    };
                                    regTemp1++;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }

            }
        }



        //动态链接（递归）（处理常量数据区的访问方式）  
        public void MainUnitLinkLibs()
        {
            //编号所有引用的库  &&  重定向指向常量数据的指针    
            this.allUnits.Add(this);
            this.id = 0;

            //常量指针偏移  
            foreach(var code in this.codes)
            {
                SetPtrOffset(code.arg1, this.id);
                SetPtrOffset(code.arg2, this.id);
                SetPtrOffset(code.arg3, this.id);
            }

            //依赖链接    
            foreach(var dep in directlyDependencies)
            {
                dep.LinkToMainUnitCursive(this);
            }
        }

        private void LinkToMainUnitCursive(RuntimeUnitV2 mainUnit)
        {
            if(mainUnit.allUnits.Contains(this))
                return;
            if(mainUnit.allUnits.Any(lib => lib.name == this.name))
                return;

            //加入集合并编号  
            mainUnit.allUnits.Add(this);
            this.id = mainUnit.allUnits.Count - 1;

            //常量指针  
            foreach(var code in this.codes)
            {
                SetPtrOffset(code.arg1, this.id);
                SetPtrOffset(code.arg2, this.id);
                SetPtrOffset(code.arg3, this.id);
            }

            //依赖  
            foreach(var dep in this.directlyDependencies)
            {
                dep.LinkToMainUnitCursive(mainUnit);
            }
        }

        private void SetPtrOffset(Operand operand, int unitId)
        {
            switch(operand)
            {
                case OperandConst cnst:
                    cnst.linkedPtr = (__constdataOffset * unitId) + (cnst.oldPtr % __constdataOffset);
                    break;


                case OperandElementAccess eleAccess:
                    SetPtrOffset(eleAccess.array, unitId);
                    SetPtrOffset(eleAccess.index, unitId);
                    break;
                case OperandMemberAccess membAccess:
                    SetPtrOffset(membAccess.obj, unitId);
                    break;
            }
        }



        public (int size, int align) GetSizeAndAlign(SymbolTable.Record rec)
        {
            if(rec.category != SymbolTable.RecordCatagory.Variable && rec.category != SymbolTable.RecordCatagory.Constant)
                return default;

            switch(rec.typeExpression)
            {
                case "int":
                    return (4, 4);
                case "float":
                    return (4, 4);
                case "double":
                    return (8, 8);
                case "bool":
                    return (1, 1);
                case "char":
                    return (2, 2);
                case "string":
                    return (8, 8); // 指针
                default:
                    return (8, 8);// 类类型，指针
            }
        }
        public bool ShouldAllocOnRegXXM(SymbolTable.Record rec)
        {
            return rec.typeExpression switch
            {
                "double" => true,
                "float" => true,
                _ => false
            };
        }

        //读取常量值    
        public object ReadConst(long ptr)
        {
            if(ptr == 0)
                return null;
            if(ptr > 0)
                throw new GizboxException(ExceptioName.NotPointerToConstant);

            long truePtr = Math.Abs(ptr) - 1;
            int unitId = (int)(truePtr / __constdataOffset);
            int valueIdx = (int)(truePtr % __constdataOffset);
            return allUnits[unitId].constData[valueIdx];
        }

        //读取全局变量  
        public Value ReadGlobalVar(string name)
        {
            if(this.globalData.ContainsKey(name))
            {
                return this.globalData[name];
            }
            return Value.Void;
        }
        //写入全局变量  
        public void WriteGlobalVar(string name, Value val)
        {
            this.globalData[name] = val;
        }

        //刷新  
        public bool NeedResetStack(int prevUnit, int prev, int currUnit, int curr)
        {
            if(prevUnit != currUnit)
                return true;

            if(currUnit == -1)
            {
                return scopeStatusArr[curr] != scopeStatusArr[prev];
            }
            else
            {
                return this.allUnits[currUnit].scopeStatusArr[curr] != this.allUnits[currUnit].scopeStatusArr[prev];
            }
        }

        // 获取堆栈  
        public Gizbox.GStack<SymbolTable> GetEnvStack(int currentUnit, int currentLine)
        {
            if(currentUnit == -1)
            {
                var status = this.scopeStatusArr[currentLine];
                return stackDic[status];
            }
            else
            {
                var extStatus = this.allUnits[currentUnit].scopeStatusArr[currentLine];
                return this.allUnits[currentUnit].stackDic[extStatus];
            }
        }


        //缓存符号表栈  
        private void CacheEnvStack()
        {
            this.stackDic = new Dictionary<int, GStack<SymbolTable>>();

            List<SymbolTable> tempList = new List<SymbolTable>();

            int prevStatus = -1;
            for(int i = 0; i < this.codes.Count; ++i)
            {
                if(scopeStatusArr[i] != prevStatus)
                {
                    int newstate = scopeStatusArr[i];
                    if(stackDic.ContainsKey(newstate) == false)
                    {
                        EnvHits(i, tempList);
                        tempList.Sort((e1, e2) => e1.depth - e2.depth);
                        var newEnvStack = new GStack<SymbolTable>();
                        foreach(var env in tempList)
                        {
                            newEnvStack.Push(env);
                        }
                        stackDic[newstate] = newEnvStack;
                    }
                    prevStatus = newstate;
                }
            }
        }

        //所在符号表链  
        private void EnvHits(int currentLine, List<SymbolTable> envs)
        {
            envs.Clear();
            envs.Add(globalScope.env);
            foreach(var scope in scopes)
            {
                if(scope.lineFrom <= currentLine && currentLine <= scope.lineTo)
                {
                    envs.Add(scope.env);
                }
            }
        }


        //查询标号  
        public Tuple<int, int> QueryLabel(string labelp0, string labelp1, int priorityUnit)
        {
            //入口标签
            if(labelp0 == "entry")
            {
                //优先查找的库  
                if(priorityUnit != -1)
                {
                    if(allUnits[priorityUnit].entryLabels.ContainsKey(labelp1))
                    {
                        return new Tuple<int, int>(priorityUnit, allUnits[priorityUnit].entryLabels[labelp1]);
                    }
                }
                //正常查找顺序  
                if(entryLabels.ContainsKey(labelp1) == true)
                {
                    return new Tuple<int, int>(-1, entryLabels[labelp1]);
                }
                else
                {
                    for(int i = 0; i < allUnits.Count; ++i)
                    {
                        if(allUnits[i].entryLabels.ContainsKey(labelp1))
                        {
                            return new Tuple<int, int>(i, allUnits[i].entryLabels[labelp1]);
                        }
                    }
                }
            }
            //出口标签  
            else if(labelp0 == "exit")
            {
                //优先查找的库  
                if(priorityUnit != -1)
                {
                    if(allUnits[priorityUnit].exitLabels.ContainsKey(labelp1))
                    {
                        return new Tuple<int, int>(priorityUnit, allUnits[priorityUnit].exitLabels[labelp1]);
                    }
                }
                //正常查找顺序  
                if(exitLabels.ContainsKey(labelp1) == true)
                {
                    return new Tuple<int, int>(-1, exitLabels[labelp1]);
                }
                else
                {
                    for(int i = 0; i < allUnits.Count; ++i)
                    {
                        if(allUnits[i].exitLabels.ContainsKey(labelp1))
                        {
                            return new Tuple<int, int>(i, allUnits[i].exitLabels[labelp1]);
                        }
                    }
                }
            }
            //其他标签  
            else
            {
                string label = labelp0;
                //优先查找的库  
                if(priorityUnit != -1)
                {
                    if(allUnits[priorityUnit].label2Line.ContainsKey(label))
                    {
                        return new Tuple<int, int>(priorityUnit, allUnits[priorityUnit].label2Line[label]);
                    }
                }

                //正常查找顺序  
                if(label2Line.ContainsKey(label) == true)
                {
                    return new Tuple<int, int>(-1, label2Line[label]);
                }
                else
                {
                    for(int i = 0; i < allUnits.Count; ++i)
                    {
                        if(allUnits[i].label2Line.ContainsKey(label))
                        {
                            return new Tuple<int, int>(i, allUnits[i].label2Line[label]);
                        }
                    }
                }
            }

            throw new GizboxException(ExceptioName.LabelNotFound, labelp0);
        }

        // 查询库  
        public RuntimeUnitV2 QueryUnit(int unitIdx)
        {
            if(unitIdx == -1)
                return this;
            else
                return allUnits[unitIdx];
        }

        // 查询类  
        public SymbolTable.Record QueryClass(string className)
        {
            if(globalScope.env.ContainRecordName(className))
            {
                return globalScope.env.GetRecord(className);
            }

            foreach(var dep in allUnits)
            {
                if(dep.globalScope.env.ContainRecordName(className))
                {
                    return dep.globalScope.env.GetRecord(className);
                }
            }

            return null;
        }

        //查询代码  
        public RuntimeCodeV2 QueryCode(int unitIdx, int line)
        {
            if(unitIdx == -1)
            {
                return this.codes[line];
            }
            else
            {
                return this.allUnits[unitIdx].codes[line];
            }
        }

        // 查询虚函数表  
        public VTable QueryVTable(string className)
        {
            if(this.vtables.ContainsKey(className))
            {
                return this.vtables[className];
            }

            foreach(var dep in allUnits)
            {
                if(dep.vtables.ContainsKey(className))
                {
                    return dep.vtables[className];
                }
            }

            return null;
        }
    }

    public static class BasicTypeInfo
    {
#if DEBUG
        public static void PrintBasicTypeSizes()
        {
            Console.WriteLine("C# 基本数据类型的字节大小和内存对齐：");
            PrintTypeInfo<bool>("bool");
            PrintTypeInfo<byte>("byte");
            PrintTypeInfo<sbyte>("sbyte");
            PrintTypeInfo<short>("short");
            PrintTypeInfo<ushort>("ushort");
            PrintTypeInfo<int>("int");
            PrintTypeInfo<uint>("uint");
            PrintTypeInfo<long>("long");
            PrintTypeInfo<ulong>("ulong");
            PrintTypeInfo<char>("char");
            PrintTypeInfo<float>("float");
            PrintTypeInfo<double>("double");
            PrintTypeInfo<decimal>("decimal");
        }

        private static void PrintTypeInfo<T>(string typeName)
        {
            int size = Marshal.SizeOf<T>();
            Console.WriteLine($"{typeName,-8}: Size = {size} bytes");
            // .NET 默认对齐方式依赖于平台和结构体布局，基础类型通常对齐到其自身大小或平台字长
        }
#endif
    }
}
