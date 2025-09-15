using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using static Gizbox.SyntaxTree;

namespace Gizbox.IR
{
    [DataContract]
    public class TAC
    {
        [DataMember]
        public int line;
        [DataMember]
        public string label;

        [DataMember]
        public string op;
        [DataMember]
        public string arg0;
        [DataMember]
        public string arg1;
        [DataMember]
        public string arg2;

        public string ToExpression(bool showlabel = true, bool indent = true)
        {
            string str = "";

            if (showlabel)
            {
                if (string.IsNullOrEmpty(label))
                {
                    if(indent)
                        str += new string(' ', 20);
                    
                }
                else
                {
                    if(indent)
                        str += (label + ":").PadRight(20);
                    else
                        str += (label + ":");
                }
            }
            str += op;

            if (string.IsNullOrEmpty(arg0) == false)
            {
                str += " " + arg0;
            }
            if (string.IsNullOrEmpty(arg1) == false)
            {
                str += " " + arg1;
            }
            if (string.IsNullOrEmpty(arg2) == false)
            {
                str += " " + arg2;
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


        public override string ToString()
        {
            return $"{(env?.name ?? "scope")}  ({lineFrom} ~ {lineTo})";
        }
    }



    public class IRUnitsCache
    {
        public List<IRUnit> units;
    }

    [Serializable]
    [DataContract(IsReference = true)]
    public class IRUnit
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

        //行数 -> 作用域状态数组  
        [DataMember]
        public int[] scopeStatusArr;

        //作用域列表(按结束顺序)    
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

        //状态 -> 符号表链 查询表
        [DataMember]
        public Dictionary<int, GStack<SymbolTable>> stackDic;

        //静态数据区 - 常量  
        [DataMember]
        public List<(string typeExpress, string valueExpr)> constData = new ();


        //(不序列化) 临时载入的依赖      
        public List<IRUnit> dependencyLibs = new List<IRUnit>();
        //(不序列化) 
        public List<IRUnit> libsDenpendThis = new List<IRUnit>();




        //构造函数  
        public IRUnit()
        {
            var globalSymbolTable = new SymbolTable("global", SymbolTable.TableCatagory.GlobalScope);
            this.globalScope = new Scope() { env = globalSymbolTable };
        }


        //自动加载所有依赖  
        public void AutoLoadDependencies(Compiler loader, bool includeDeps = true)
        {
            if(dependencyLibs == null)
                dependencyLibs = new();

            if(this.dependencyLibs.Count == 0 && this.dependencies.Count != 0)
            {
                foreach(var depName in this.dependencies)
                {
                    var depUnit = loader.LoadLib(depName);
                    this.AddDependencyLib(depUnit);
                }
            }

            if(includeDeps && this.dependencyLibs != null)
            {
                foreach(var dep in this.dependencyLibs)
                {
                    dep.AutoLoadDependencies(loader);
                }
            }

        }
        //添加依赖  
        public void AddDependencyLib(IRUnit dep)
        {
            if (dep == null) throw new GizboxException(ExceptioName.LibraryDependencyCannotBeEmpty);

            if (dependencyLibs == null) dependencyLibs = new List<IRUnit>();
            dependencyLibs.Add(dep);

            if (dep.libsDenpendThis == null) dep.libsDenpendThis = new List<IRUnit>();
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

            scopeStatusArr = new int[codes.Count];
            for (int i = 0; i < scopeStatusArr.Length; i++)
            {
                scopeStatusArr[i] = 0;
            }
            int status = 0; ;
            foreach (var scope in scopes)
            {
                status++;
                for (int i = scope.lineFrom; i <= scope.lineTo; ++i)
                {
                    scopeStatusArr[i] = status;
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
            stackDic = new Dictionary<int, GStack<SymbolTable>>();

            List<SymbolTable> tempList = new List<SymbolTable>();

            int prevStatus = -1;
            for (int i = 0; i < codes.Count; ++i)
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

        /// <summary> 计算所在符号表链 </summary>
        private void EnvHits(int currentLine, List<SymbolTable> envs)
        {
            envs.Clear();
            foreach (var scope in scopes)
            {
                if (scope.lineFrom <= currentLine && currentLine <= scope.lineTo)
                {
                    envs.Add(scope.env);
                }
            }
        }

        /// <summary> 查询(当前表栈、全局作用域、库) </summary>
        public SymbolTable.Record Query(string name, int line = -1)
        {
            if(line > -1)
            {
                var stack = GetEnvStackAtLine(line);

                //符号表链查找  
                foreach(SymbolTable env in stack)
                {
                    if(env.ContainRecordName(name))
                    {
                        return env.GetRecord(name);
                    }
                }
            }
            //库依赖中查找  
            if(dependencyLibs != null)
            {
                foreach(var lib in this.dependencyLibs)
                {
                    var result = lib.Query(name);
                    if(result != null)
                        return result;
                }
            }

            return null;
        }
        /// <summary> 查询(当前表栈、全局作用域、库) </summary>
        public SymbolTable.Record QueryRaw(string name, int line = -1)
        {
            if(line > -1)
            {
                var stack = GetEnvStackAtLine(line);

                //符号表链查找  
                foreach(SymbolTable env in stack)
                {
                    if(env.ContainRecordRawName(name))
                    {
                        return env.GetRecordByRawname(name);
                    }
                }
            }
            //库依赖中查找  
            if(dependencyLibs != null)
            {
                foreach(var lib in this.dependencyLibs)
                {
                    var result = lib.QueryRaw(name);
                    if(result != null)
                        return result;
                }
            }

            return null;
        }

        /// <summary> 查询一个顶层符号 </summary>
        public SymbolTable.Record QueryTopSymbol(string name, bool ignoreMangle = false)
        {
            //本单元查找  
            if (ignoreMangle == false)
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
            if (dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryTopSymbol(name, ignoreMangle);
                    if (result != null) return result;
                }
            }

            return null;
        }


        /// <summary> 查询顶层符号并全部填充到 </summary>
        public void QueryAndFillTopSymbolsToContainer(string name, List<SymbolTable.Record> result, bool ignoreMangle = false)
        {
            //本单元查找  
            if (ignoreMangle == false)
            {
                if (globalScope.env.ContainRecordName(name))
                {
                    result.Add(globalScope.env.GetRecord(name));
                }
            }
            else
            {
                globalScope.env.GetAllRecordByRawname(name, result);
            }

            //依赖中查找  
            if (dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    dep.QueryAndFillTopSymbolsToContainer(name, result, ignoreMangle);
                }
            }
        }


        /// <summary> 查询虚函数表 </summary>
        public VTable QueryVTable(string name)
        {
            //本单元查找  
            if (vtables.ContainsKey(name))
            {
                return vtables[name];
            }

            //依赖中查找  
            if (dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryVTable(name);
                    if (result != null) return result;
                }
            }

            return null;
        }


        /// <summary> 查询所有全局作用域 </summary>
        public List<SymbolTable> GetAllGlobalEnvs()
        {
            List<SymbolTable> envs = new List<SymbolTable>();
            AddGlobalEnvsToList(this, envs);
            return envs;
        }


        /// <summary> 行所在符号表栈 </summary>
        public GStack<SymbolTable> GetEnvStackAtLine(int line)
        {
            if(line < scopeStatusArr.Length)
            {
                var status = scopeStatusArr[line];
                return stackDic[status];
            }
            return null;
        }

        /// <summary> 行所在的最外层符号表 </summary>
        public SymbolTable GetOutermostEnvAtLine(int line)
        {
            return GetEnvStackAtLine(line).Peek();
        }

        private void AddGlobalEnvsToList(IRUnit unit, List<SymbolTable> list)
        {
            if (list.Contains(unit.globalScope.env)) return;

            list.Add(unit.globalScope.env);

            if(dependencyLibs.Count == 0 && dependencies.Count > 0)
                throw new GizboxException(ExceptioName.Undefine, "dependencies is not loaded.");
            foreach (var dep in dependencyLibs)
            {
                AddGlobalEnvsToList(dep, list);
            }
        }

        //打印  
        public void Print()
        {
            GixConsole.WriteLine("中间代码输出：(" + codes.Count + "行)");
            GixConsole.WriteLine(new string('-', 50));
            for (int i = 0; i < codes.Count; ++i)
            {
                GixConsole.WriteLine($"{i.ToString().PadRight(4)}|status {scopeStatusArr[i].ToString().PadRight(3)}|{codes[i].ToExpression()}");
            }
            GixConsole.WriteLine(new string('-', 50));
        }
    }


}
