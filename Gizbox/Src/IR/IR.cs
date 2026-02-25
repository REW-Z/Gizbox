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

        [DataMember]
        public string comment;

        public string ToExpression(bool showlabel = true, bool indent = true)
        {
            StringBuilder strb = new();

            if (showlabel)
            {
                if (string.IsNullOrEmpty(label))
                {
                    if(indent)
                        strb.Append(new string(' ', 20));
                    
                }
                else
                {
                    if(indent)
                        strb.Append((label + ":").PadRight(20));
                    else
                        strb.Append(label + ":") ;
                }
            }
            strb.Append(op) ;

            if (string.IsNullOrEmpty(arg0) == false)
            {
                strb.Append(" " + arg0) ;
            }
            if (string.IsNullOrEmpty(arg1) == false)
            {
                strb.Append(" " + arg1) ;
            }
            if (string.IsNullOrEmpty(arg2) == false)
            {
                strb.Append(" " + arg2) ;
            }
            if (string.IsNullOrEmpty(comment) == false)
            {
                strb.Append("    // " + comment);
            }
            return strb.ToString();
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

        //AST根节点(用于模板特化等)
        [DataMember]
        public SyntaxTree.ProgramNode astRoot;

        //模板列表  
        [DataMember]
        public List<string> templateClasses = new();
        //模板列表  
        [DataMember]
        public List<string> templateFunctions = new();

        //(不序列化) 临时载入的依赖      
        public List<IRUnit> dependencyLibs = new List<IRUnit>();
        //(不序列化) 
        public List<IRUnit> libsDenpendThis = new List<IRUnit>();

        //(不序列化) AST缓存
        public SyntaxTree ast;




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

            if(dependencyLibs.Count > 0)
            {
                var uniqueCount = dependencyLibs.Select(d => d.name).Distinct().Count();
                if (dependencies.Count != uniqueCount)
                {
                    throw new GizboxException(ExceptioName.Undefine, $"libs of {this.name} loaded error.");
                }
            }
                

            if(this.dependencyLibs.Count == 0 && this.dependencies.Count != 0)
            {
                foreach(var depName in this.dependencies)
                {
                    var depUnit = loader.LoadLib(depName);
                    this.AddDependencyLib(depUnit);
                }
            }

            if(includeDeps)
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
            if (dependencyLibs.Any(d => d.name == dep.name))
                return;

            dependencyLibs.Add(dep);

            if (dep.libsDenpendThis == null) dep.libsDenpendThis = new List<IRUnit>();
            if (dep.libsDenpendThis.Any(u => u.name == this.name) == false)
                dep.libsDenpendThis.Add(this);
        }

        public void EnsureAst()
        {
            if(ast != null)
                return;

            if(astRoot != null)
            {
                ast = new SyntaxTree(astRoot);
            }
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
            else
            {
                if(globalScope.env.records.TryGetValue(name, out var record))
                {
                    return record;
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
                GixConsole.Write(i.ToString().PadRight(4), ConsoleColor.Gray);
                GixConsole.Write("|status ", ConsoleColor.Gray);
                GixConsole.Write(scopeStatusArr[i].ToString().PadRight(3), ConsoleColor.Gray);
                GixConsole.Write("|", ConsoleColor.Gray);

                var tac = codes[i];
                {
                    if(string.IsNullOrEmpty(tac.label))
                    {
                        GixConsole.Write(new string(' ', 20));
                    }
                    else
                    {
                        GixConsole.Write((tac.label + ":").PadRight(20), ConsoleColor.DarkYellow);
                    }
                }
                {
                    GixConsole.Write(" ");
                    ConsoleColor opColor = ConsoleColor.DarkBlue;
                    if(tac.op == "JUMP" || tac.op == "CALL" || tac.op == "MCALL" || tac.op == "IF_FALSE_JUMP")
                        opColor = ConsoleColor.Magenta;
                    else if(tac.op == "RETURN")
                        opColor = ConsoleColor.DarkMagenta;

                    GixConsole.Write(tac.op, opColor);
                }


                GixConsole.Write(" ");
                GixConsole.Write(tac.arg0, ConsoleColor.White);
                GixConsole.Write(" ");
                GixConsole.Write(tac.arg1, ConsoleColor.DarkGray);
                GixConsole.Write(" ");
                GixConsole.Write(tac.arg2, ConsoleColor.White);

                GixConsole.Write(" ");
                GixConsole.Write(tac.comment, ConsoleColor.DarkGreen);
                GixConsole.Write("\n\r");

                //GixConsole.WriteLine($"{i.ToString().PadRight(4)}|status {scopeStatusArr[i].ToString().PadRight(3)}|{codes[i].ToExpression()}");
            }
            GixConsole.WriteLine(new string('-', 50));
        }
    }


}
