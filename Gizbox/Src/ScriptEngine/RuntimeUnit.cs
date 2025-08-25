using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Gizbox;
using Gizbox.ScriptEngine;
using Gizbox.IR;

namespace Gizbox.ScriptEngine
{
    public class Operand
    {
        public string str;

        public Operand(string argStr)
        {
            this.str = argStr;
        }


        public static Operand Parse(string argStr, ILUnit unit, int line)
        {
            if(string.IsNullOrEmpty(argStr))
            {
                return null;
            }
            else if(argStr == "%RET")
            {
                return new OperandRegister(argStr);
            }
            else if(argStr.StartsWith("%CONST"))
            {
                return new OperandConst(argStr);
            }
            else if(argStr.StartsWith("%LIT"))
            {
                return new OperandLiteralValue(argStr);
            }
            else if(argStr.StartsWith("%LABEL:"))
            {
                return new OperandLabel(argStr);
            }
            else if(argStr.Contains("->"))
            {
                return new OperandMemberAccess(argStr, unit, line);
            }
            else if(argStr.EndsWith("]"))
            {
                return new OperandElementAccess(argStr, unit, line);
            }
            else 
            {
                return new OperandSingleIdentifier(argStr, unit, line);
            }

            throw new GizboxException(ExceptioName.TacError);
        }
    }
    public class OperandRegister : Operand
    {
        public string registerName;
        public OperandRegister(string str) : base(str)
        {
            this.registerName = str.Substring(1);
        }
    }
    public class OperandLabel : Operand
    {
        public string label;
        public OperandLabel(string s) : base(s)
        {
            if(s.StartsWith("%LABEL:") == false)
                throw new GizboxException(ExceptioName.TacError, "OperandLabel must start with 'LABEL:'");

            label = s.Substring(7);
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
            switch (baseType)
            {
                case "%CONSTSTRING":
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
            switch (baseType)
            {
                case "%LITNULL": this.val =  Value.NULL; break;
                case "%LITBOOL": this.val = bool.Parse(lex); break;
                case "%LITINT": this.val = int.Parse(lex); break;
                case "%LITLONG": this.val = long.Parse(lex.Substring(0, lex.Length - 1)); break;
                case "%LITFLOAT": this.val = float.Parse(lex.Substring(0, lex.Length - 1)); break;//去除F标记  
                case "%LITDOUBLE": this.val = double.Parse(lex.Substring(0, lex.Length - 1)); break;//去除F标记  
                case "%LITCHAR": this.val = lex[1]; break;
                //case "LITSTRING": return Value.Void;//字符串字面量已经移除
                default: throw new GizboxException(ExceptioName.UnknownLiteral,  str);
            }
        }
    }
    public class OperandElementAccess : Operand
    {
        public Operand array;
        public Operand index;

        public OperandElementAccess(string expr, ILUnit unit, int line) : base(expr)
        {
            int lbracket = expr.IndexOf('[');
            int rbracket = expr.IndexOf(']');

            string arrVarExpr = expr.Substring(0, lbracket);
            string idxExpr = expr.Substring(lbracket + 1, (rbracket - lbracket) - 1);


            if (arrVarExpr[arrVarExpr.Length - 1] == ']')
            {
                this.array = new OperandElementAccess(arrVarExpr, unit, line);
            }
            else if (arrVarExpr.Contains("->"))
            {
                this.array = new OperandMemberAccess(arrVarExpr, unit, line);
            }
            else
            {
                this.array = new OperandSingleIdentifier(arrVarExpr, unit, line);
            }

            this.index = Operand.Parse(idxExpr, unit, line);
        }
    }
    public class OperandMemberAccess :Operand
    {
        public Operand obj;
        public string fieldname;

        public OperandMemberAccess (string expr, ILUnit unit, int line) : base(expr)
        {
            var (start, end) = expr.GetSubStringIndex("->");
            var variableExpr = expr.Substring(0, start);
            string fieldName = expr.Substring(end + 1);


            if (variableExpr[variableExpr.Length - 1] == ']')
            {
                this.obj = new OperandElementAccess(variableExpr, unit, line);
            }
            else if(variableExpr.Contains("->"))
            {
                this.obj = new OperandMemberAccess(variableExpr, unit, line);
            }
            else
            {
                this.obj = new OperandSingleIdentifier(variableExpr, unit, line);
            }
            
            this.fieldname = fieldName;
        }
    }
    public class OperandSingleIdentifier : Operand
    {
        public string name;

        public SymbolTable.Record record;

        public OperandSingleIdentifier(string str, ILUnit unit, int line) : base(str)
        {
            this.name = str;
            this.record = unit.Query(this.name, line);
        }
    }





    public class RuntimeCode
    {
        public string labelpart0;
        public string labelpart1;

        public string op;

        public Operand arg0;
        public Operand arg1;
        public Operand arg2;

        public RuntimeCode(TAC tac, ILUnit unit, int line)
        {
            if(string.IsNullOrEmpty(tac.label))
            {
                labelpart0 = "";
                labelpart1 = "";
            }
            else if (tac.label.StartsWith("entry:"))
            {
                labelpart0 = "entry";
                labelpart1 = tac.label.Substring(6);
            }
            else if (tac.label.StartsWith("exit:"))
            {
                labelpart0 = "exit";
                labelpart1 = tac.label.Substring(5);
            }
            else
            {
                labelpart0 = tac.label;
            }

            this.op = tac.op;

            this.arg0 = Operand.Parse(tac.arg0, unit, line);
            this.arg1 = Operand.Parse(tac.arg1, unit, line);
            this.arg2 = Operand.Parse(tac.arg2, unit, line);
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
                if (string.IsNullOrEmpty(labelpart1) == false)
                {
                    str += ":" + labelpart1;
                }
            }

            str += "\t";
            str += op;

            if (arg0 != null)
            {
                str += "\t";
                str += arg0.str;
            }
            if (arg1 != null)
            {
                str += "\t";
                str += arg1.str;
            }
            if (arg2 != null)
            {
                str += "\t";
                str += arg2.str;
            }
            return str;
        }
    }
    public class RuntimeUnit
    {
        public const long __constdataOffset = 10000000000L;

        public int id = -1;
        public string name;
        public List<RuntimeUnit> directlyDependencies = new List<RuntimeUnit>();
        public List<RuntimeCode> codes = new List<RuntimeCode>();
        public int[] scopeStatusArr;
        public List<Scope> scopes = new List<Scope>();
        public Scope globalScope;
        public Dictionary<string, VTable> vtables = new Dictionary<string, VTable>();
        private Dictionary<string, int> label2Line = new Dictionary<string, int>();
        public Dictionary<int, Gizbox.GStack<SymbolTable>> stackDic;
        public List<object> constData = new List<object>();


        private Dictionary<string, Dictionary<string, int>> specialLabels = new ();




        public List<RuntimeUnit> allUnits = new List<RuntimeUnit>();
        public Dictionary<string, Value> globalData = new Dictionary<string, Value>();




        //构造    
        public RuntimeUnit(ScriptEngine engineContext, ILUnit ilunit)
        {
            this.name = ilunit.name;

            foreach(var depName in ilunit.dependencies)
            {
                var libIr = engineContext.LoadLib(depName);
                this.directlyDependencies.Add(new RuntimeUnit(engineContext, libIr));
            }

            for(int i = 0; i < ilunit.codes.Count; ++i)
            {
                var tac = ilunit.codes[i];
                var newCode = new RuntimeCode(tac, ilunit, i);
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
                string firstKey = null;
                string secondKey = null;
                if(kv.Key.StartsWith("entry:"))
                {
                    firstKey = "entry";
                    secondKey = kv.Key.Substring(6);
                }
                else if(kv.Key.StartsWith("func_begin:"))
                {
                    firstKey = "func_begin";
                    secondKey = kv.Key.Substring(11);
                }
                else if(kv.Key.StartsWith("func_end:"))
                {
                    firstKey = "func_end";
                    secondKey = kv.Key.Substring(9);
                }
                else if(kv.Key.StartsWith("exit:"))
                {
                    firstKey = "exit";
                    secondKey = kv.Key.Substring(5);
                }


                if(firstKey != null && secondKey != null)
                {
                    if(specialLabels.ContainsKey(firstKey) == false)
                        specialLabels[firstKey] = new Dictionary<string, int>();
                    specialLabels[firstKey][secondKey] = kv.Value;
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
            foreach (var code in this.codes)
            {
                SetPtrOffset(code.arg0, this.id);
                SetPtrOffset(code.arg1, this.id);
                SetPtrOffset(code.arg2, this.id);
            }

            //依赖链接    
            foreach(var dep in directlyDependencies)
            {
                dep.LinkToMainUnitCursive(this);
            }
        }

        private void LinkToMainUnitCursive(RuntimeUnit mainUnit)
        {
            if (mainUnit.allUnits.Contains(this)) return;
            if (mainUnit.allUnits.Any(lib => lib.name == this.name)) return;

            //加入集合并编号  
            mainUnit.allUnits.Add(this);
            this.id = mainUnit.allUnits.Count - 1;

            //常量指针  
            foreach(var code in this.codes)
            {
                SetPtrOffset(code.arg0, this.id);
                SetPtrOffset(code.arg1, this.id);
                SetPtrOffset(code.arg2, this.id);
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



        //读取常量值    
        public object ReadConst(long ptr)
        {
            if (ptr == 0) return null;
            if (ptr > 0) throw new GizboxException(ExceptioName.NotPointerToConstant);
            
            long truePtr = Math.Abs(ptr) - 1;
            int unitId = (int)(truePtr / __constdataOffset);
            int valueIdx = (int)(truePtr % __constdataOffset);
            return allUnits[unitId].constData[valueIdx];
        }

        //读取全局变量  
        public Value ReadGlobalVar(string name)
        {
            if (this.globalData.ContainsKey(name))
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
            if (prevUnit != currUnit) return true;

            if (currUnit == -1)
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
            if (currentUnit == -1)
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
            for (int i = 0; i < this.codes.Count; ++i)
            {
                if (scopeStatusArr[i] != prevStatus)
                {
                    int newstate = scopeStatusArr[i];
                    if (stackDic.ContainsKey(newstate) == false)
                    {
                        EnvHits(i, tempList);
                        tempList.Sort((e1, e2) => e1.depth - e2.depth);
                        var newEnvStack = new GStack<SymbolTable>();
                        foreach (var env in tempList)
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
            foreach (var scope in scopes)
            {
                if (scope.lineFrom <= currentLine && currentLine <= scope.lineTo)
                {
                    envs.Add(scope.env);
                }
            }
        }


        //查询标号  
        public Tuple<int, int> QueryLabel(string labelp0, string labelp1, int priorityUnit)
        {
            if(labelp0 == "entry" ||
                labelp0 == "func_begin" ||
                labelp0 == "func_end" ||
                labelp0 == "exit"
                )
            {
                //优先查找的库  
                if(priorityUnit != -1)
                {
                    if(allUnits[priorityUnit].specialLabels[labelp0].ContainsKey(labelp1))
                    {
                        return new Tuple<int, int>(priorityUnit, allUnits[priorityUnit].specialLabels[labelp0][labelp1]);
                    }
                }
                //正常查找顺序  
                if(specialLabels[labelp0].ContainsKey(labelp1) == true)
                {
                    return new Tuple<int, int>(-1, specialLabels[labelp0][labelp1]);
                }
                else
                {
                    for(int i = 0; i < allUnits.Count; ++i)
                    {
                        if(allUnits[i].specialLabels[labelp0].ContainsKey(labelp1))
                        {
                            return new Tuple<int, int>(i, allUnits[i].specialLabels[labelp0][labelp1]);
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

            throw new GizboxException(ExceptioName.LabelNotFound, labelp0 + (labelp1 != null ? (":" + labelp1) : string.Empty));
        }

        // 查询库  
        public RuntimeUnit QueryUnit(int unitIdx)
        {
            if (unitIdx == -1) return this;
            else return allUnits[unitIdx];
        }

        // 查询类  
        public SymbolTable.Record QueryClass(string className)
        {
            if (globalScope.env.ContainRecordName(className))
            {
                return globalScope.env.GetRecord(className);
            }

            foreach (var dep in allUnits)
            {
                if (dep.globalScope.env.ContainRecordName(className))
                {
                    return dep.globalScope.env.GetRecord(className);
                }
            }

            return null;
        }

        //查询代码  
        public RuntimeCode QueryCode(int unitIdx, int line)
        {
            if (unitIdx == -1)
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
            if (this.vtables.ContainsKey(className))
            {
                return this.vtables[className];
            }

            foreach (var dep in allUnits)
            {
                if (dep.vtables.ContainsKey(className))
                {
                    return dep.vtables[className];
                }
            }

            return null;
        }
    }
}
