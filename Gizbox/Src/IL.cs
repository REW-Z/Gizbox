using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Gizbox;
using static Gizbox.SyntaxTree;

namespace Gizbox.IL
{
    [DataContract]
    public class TAC
    {
        [DataMember]
        public string label;

        [DataMember]
        public string op;
        [DataMember]
        public string arg1;
        [DataMember]
        public string arg2;
        [DataMember]
        public string arg3;

        public string ToExpression(bool showlabel = true)
        {
            string str = "";

            if(showlabel)
            {
                if (string.IsNullOrEmpty(label))
                {
                    str += new string(' ', 20);
                }
                else
                {
                    str += (label + ":").PadRight(20);
                }
            }
            str += op;

            if(string.IsNullOrEmpty(arg1) == false)
            {
                str += (" " + arg1);
            }
            if (string.IsNullOrEmpty(arg2) == false)
            {
                str += (" " + arg2);
            }
            if (string.IsNullOrEmpty(arg3) == false)
            {
                str += (" " + arg3);
            }
            return str;
        }
    }
    [DataContract(IsReference = true)]
    public class Scope
    {
        [DataMember]
        public int lineFrom;
        [DataMember]
        public int lineTo;
        [DataMember]
        public SymbolTable env;
    }

    [Serializable]
    [DataContract(IsReference = true)]
    public class ILUnit
    {
        //名称    
        [DataMember]
        public string name;

        //依赖库名称  
        [DataMember]
        public List<string> dependencies = new List<string>();

        //代码  
        [DataMember]
        public List<TAC> codes = new List<TAC>();

        //作用域状态数组  
        [DataMember]
        public int[] scopeStatusArr;

        //作用域列表  
        [DataMember]
        public List<Scope> scopes = new List<Scope>();

        //全局作用域  
        [DataMember]
        public Scope globalScope;

        //虚函数表  
        [DataMember]
        public Dictionary<string, VTable> vtables = new Dictionary<string, VTable>();

        //标号 -> 行数 查询表  
        [DataMember]
        public Dictionary<string, int> label2Line = new Dictionary<string, int>();

        //行数 -> 符号表链 查询表
        [DataMember]
        public Dictionary<int, Gizbox.GStack<SymbolTable>> stackDic;

        //静态数据区 - 常量  
        [DataMember]
        public List<object> constData = new List<object>();


        //(不序列化) 临时载入的依赖      
        public List<ILUnit> dependencyLibs = new List<ILUnit>();
        //(不序列化) 
        public List<ILUnit> libsDenpendThis = new List<ILUnit>();




        //构造函数  
        public ILUnit()
        {
            var globalSymbolTable = new SymbolTable("global", SymbolTable.TableCatagory.GlobalScope);
            this.globalScope = new Scope() { env = globalSymbolTable };
        }



        //添加依赖  
        public void AddDependencyLib(ILUnit dep)
        {
            if (dep == null) throw new GizboxException(ExceptioName.LibraryDependencyCannotBeEmpty);

            if (this.dependencyLibs == null) this.dependencyLibs = new List<ILUnit>();
            this.dependencyLibs.Add(dep);

            if (dep.libsDenpendThis == null) dep.libsDenpendThis = new List<ILUnit>();
            dep.libsDenpendThis.Add(this);
        }

        //完成构建  
        public void Complete()
        {
            globalScope.lineFrom = 0;
            globalScope.lineTo = codes.Count - 1;

            FillLabelDic();
            BuildMarkArray();
            CacheEnvStack();
        }

        //填充作用域标记  
        private void BuildMarkArray()
        {
            scopes.Sort((s1, s2) => s1.env.depth - s2.env.depth);

            this.scopeStatusArr = new int[this.codes.Count];
            for (int i = 0; i < this.scopeStatusArr.Length; i++)
            {
                this.scopeStatusArr[i] = 0;
            }
            int status = 0; ;
            foreach (var scope in scopes)
            {
                status++;
                for (int i = scope.lineFrom; i <= scope.lineTo; ++i)
                {
                    this.scopeStatusArr[i] = status;
                }
            }
        }

        //填充字典  
        private void FillLabelDic()
        {
            for (int i = 0; i < codes.Count; ++i)
            {
                if (string.IsNullOrEmpty(codes[i].label) == false)
                {
                    label2Line[codes[i].label] = i;
                }
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
                if(scopeStatusArr[i] != prevStatus)
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
            foreach(var scope in scopes)
            {
                if(scope.lineFrom <= currentLine && currentLine <= scope.lineTo)
                {
                    envs.Add(scope.env);
                }
            }
        }

        //查询顶层符号  
        public SymbolTable.Record QueryTopSymbol(string name, bool ignoreMangle = false)
        {
            //本单元查找  
            if(ignoreMangle == false)
            {
                if (globalScope.env.ContainRecordName(name))
                {
                    return globalScope.env.GetRecord(name);
                }
            }
            else
            {
                if (globalScope.env.ContainRecordRawName(name))
                {
                    return globalScope.env.GetRecordByRawname(name);
                }
            }

            //依赖中查找  
            if(this.dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryTopSymbol(name, ignoreMangle);
                    if (result != null) return result;
                }
            }

            return null;
        }

        //查询虚函数表  
        public VTable QueryVTable(string name)
        {
            //本单元查找  
            if (vtables.ContainsKey(name))
            {
                return vtables[name];
            }

            //依赖中查找  
            if (this.dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryVTable(name);
                    if (result != null) return result;
                }
            }

            return null;
        }


        //查询所有全局作用域   
        public List<SymbolTable> GetAllGlobalEnvs()
        {
            List<SymbolTable> envs = new List<SymbolTable>();
            AddGlobalEnvsToList(this, envs);
            return envs;
        }
        private void AddGlobalEnvsToList(ILUnit unit, List<SymbolTable> list)
        {
            if (list.Contains(unit.globalScope.env)) return;

            list.Add(unit.globalScope.env);
            foreach (var dep in this.dependencyLibs)
            {
                AddGlobalEnvsToList(dep, list);
            }
        }


        //打印  
        public void Print()
        {
            GixConsole.LogLine("中间代码输出：(" + this.codes.Count + "行)");
            GixConsole.LogLine(new string('-', 50));
            for (int i = 0; i < this.codes.Count; ++i)
            {
                GixConsole.LogLine($"{i.ToString().PadRight(4)}|status {this.scopeStatusArr[i].ToString().PadRight(3)}|{this.codes[i].ToExpression()}");
            }
            GixConsole.LogLine(new string('-', 50));


            GixConsole.LogLine("作用域：");
            foreach (var scope in this.scopes)
            {
                GixConsole.LogLine("scope:" + scope.env.name + ":  " + scope.lineFrom + " ~ " + scope.lineTo);
            }
        }
    }


}
