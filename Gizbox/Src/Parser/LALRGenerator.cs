using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

using Gizbox.LRParse;
using Gizbox.LALRGenerator;


namespace Gizbox.LRParse
{
    /// <summary>
    /// 项目  
    /// </summary>
    public class LR1Item
    {
        public Production production;
        public int iDot;
        public Terminal lookahead;


        public (Production prod, int idot) GetLeft()
        {
            return (this.production, this.iDot);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            var other = obj as LR1Item;
            if (other == null) return false;
            return (this.production == other.production) && (this.iDot == other.iDot) && (this.lookahead == other.lookahead);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (production != null ? production.GetHashCode() : 0);
                hash = hash * 31 + iDot.GetHashCode();
                hash = hash * 31 + (lookahead != null ? lookahead.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// 项集  
    /// </summary>
    public class LR1ItemSet : IEnumerable<LR1Item>
    {
        public int id;

        private List<LR1Item> items = new List<LR1Item>();
        private HashSet<LR1Item> itemHashSet;//用于查重  

        public LR1ItemSet()
        {
            this.itemHashSet = new HashSet<LR1Item>();
        }

        public int Count => this.itemHashSet.Count;

        public IEnumerator<LR1Item> GetEnumerator()
        {
            return ((IEnumerable<LR1Item>)items).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)items).GetEnumerator();
        }


        public void AddImmediate(LR1Item item)//立即添加 - 反序列使用
        {
            if (item == null) return;
            // 反序列化时可能重复调用，确保不重复添加
            if (this.itemHashSet.Add(item))
            {
            items.Add(item);
        }
        }

        public void AddDistinct(LR1Item item)
        {
            if (item == null) return;
            if (this.itemHashSet.Add(item))
            {
                items.Add(item);
            }
        }

        public bool AnyRepeat(LR1Item item)
        {
            if (item == null) return false;
            return this.itemHashSet.Contains(item);
        }

        public bool IsSameTo(LR1ItemSet another)
        {
            if (another == null) return false;
            if (another.Count != this.Count) return false;

            return this.itemHashSet.SetEquals(another.itemHashSet);
        }

        public LR1ItemSet Clone()
        {
            LR1ItemSet newCollection = new LR1ItemSet();
            foreach (var itm in this.items)
            {
                newCollection.AddDistinct(itm);
            }
            return newCollection;
        }
    }


    /// <summary>
    /// 扩展方法(仅用于打印)    
    /// </summary>
    public static class LR1ItemExtensions
    {
        public static string ToExpression(this LR1Item item)
        {
            StringBuilder strb = new StringBuilder();

            strb.Append(item.production.head.name);
            strb.Append(" ->");
            for(int i = 0; i < item.production.body.Length; ++i)
            {
                if(i == item.iDot)
                {
                    strb.Append(' ');
                    strb.Append('·');
                }

                strb.Append(' ');
                strb.Append(item.production.body[i] != null ? item.production.body[i].name : "ε");
            }
            if(item.iDot == item.production.body.Length)
            {
                strb.Append(' ');
                strb.Append("·");
            }

            strb.Append(", ");
            strb.Append(item.lookahead != null ? item.lookahead.name : "ε");

            return strb.ToString();
        }

        public static string ToExpression(this LR1ItemSet set)
        {
            void AppendProductionWithDot(StringBuilder strb, Production production, int iDot)
            {
                strb.Append(production.head.name);
                strb.Append(" ->");
                for(int i = 0; i < production.body.Length; ++i)
                {
                    if(i == iDot)
                    {
                        strb.Append(' ');
                        strb.Append('·');
                    }

                    strb.Append(' ');
                    strb.Append(production.body[i] != null ? production.body[i].name : "ε");
                }
                if(iDot == production.body.Length)
                {
                    strb.Append(' ');
                    strb.Append("·");
                }
            }



            var order = new List<(Production production, int iDot)>();
            var groups = new Dictionary<Production, Dictionary<int, List<Terminal>>>();

            foreach(var itm in set.ToArray())
            {
                var prod = itm.production;
                var dot = itm.iDot;

                if(!groups.TryGetValue(prod, out var byDot))
                {
                    byDot = new Dictionary<int, List<Terminal>>();
                    groups[prod] = byDot;
                }

                if(!byDot.TryGetValue(dot, out var lookaheads))
                {
                    lookaheads = new List<Terminal>();
                    byDot[dot] = lookaheads;
                    order.Add((prod, dot)); // 记录首次出现顺序
                }

                if(!lookaheads.Contains(itm.lookahead))
                {
                    lookaheads.Add(itm.lookahead);
                }
            }

            StringBuilder strb = new StringBuilder();
            strb.AppendLine(" ----------------");
            foreach(var (production, iDot) in order)
            {
                strb.Append("| ");
                AppendProductionWithDot(strb, production, iDot);

                strb.Append(", ");
                var lookaheadList = groups[production][iDot];
                foreach(var la in lookaheadList)
                {
                    strb.Append("," + (la != null ? la.name : "ε"));
                }
                strb.Append('\n');
            }
            strb.AppendLine(" ----------------");
            return strb.ToString();
        }
    }



    /// <summary>
    /// 状态    
    /// </summary>
    public class State
    {
        public int idx;

        public string name;

        public LR1ItemSet set;

        public State(int idx, string name, LR1ItemSet _set)
        {
            this.idx = idx;
            this.name = name;
            this.set = _set;
        }
    }




    public enum ACTION_TYPE
    {
        Shift,  //移入(s)
        Reduce, //规约(r)
        Accept, //接受(acc)
        Error,  //报错(err)
    }
    public struct ACTION
    {
        public ACTION_TYPE type;//操作类型  
        public int num;//要移进的状态ID或者要规约的产生式ID    

        public static ACTION Shift(int stateId) => new ACTION() { type = ACTION_TYPE.Shift, num = stateId };
        public static ACTION Reduce(int productionId) => new ACTION() { type = ACTION_TYPE.Reduce, num = productionId };
        public static ACTION Acc => new ACTION() { type = ACTION_TYPE.Accept };
        public static ACTION Err => new ACTION() { type = ACTION_TYPE.Error };

        public override string ToString()
        {
            if (type == ACTION_TYPE.Shift)
                return type.ToString().ToLower().Substring(0, 1) + num.ToString();
            else if (type == ACTION_TYPE.Reduce)
                return type.ToString().ToLower().Substring(0, 1) + (num + 1).ToString();//产生式序号是从0开始的
            else
                return type.ToString().ToLower().Substring(0, 3);
        }

        public static ACTION Parse(string str)
        {
            switch (str[0])
            {
                case 'r':
                    {
                        return new ACTION() { type = ACTION_TYPE.Reduce, num = (int.Parse(str.Substring(1)) - 1) };
                    }
                case 's':
                    {
                        return new ACTION() { type = ACTION_TYPE.Shift, num = int.Parse(str.Substring(1)) };
                    }
                case 'a':
                    {
                        if(str == "acc")
                        {
                            return new ACTION() { type = ACTION_TYPE.Accept };
                        }
                        else
                        {
                            throw new Exception("acc字符串格式错误！");
                        }
                    }
                default:
                    return new ACTION() { type = ACTION_TYPE.Error };
            }
        }
    }
    public struct GOTO
    {
        public int stateId;

        public override string ToString()
        {
            return stateId.ToString();
        }

        public static GOTO Parse(string str)
        {
            return new GOTO() { stateId = int.Parse(str) };
        }
    }


    /// <summary>
    /// 分析表  
    /// </summary>
    public class ParseTable
    {
        ///ChatGPT:可以使用稀疏矩阵来压缩存储表格    
        ///


        public int stateCount;
        public List<string> terminals;
        public List<string> nonterminals;


        public int accState = -99;
        public string accSymbol = "not set !";

        //ACTION子表（使用嵌套Dic的话，找不到ACTION项认为是Err）  
        private Dictionary<int, Dictionary<string, ACTION>> actions = new Dictionary<int, Dictionary<string, ACTION>>();
        //GOTO子表  
        private Dictionary<int, Dictionary<string, GOTO>> gotos = new Dictionary<int, Dictionary<string, GOTO>>();

        //检查  
        public bool CheckACTION(int i, string terminal)
        {
            if (actions.ContainsKey(i))
            {
                if (actions[i].ContainsKey(terminal))
                {
                    return true;
                }
            }
            return false;
        }

        //设置    
        public void SetACTION(int i, string a, ACTION act)
        {
            if (actions.ContainsKey(i) == false)
                actions[i] = new Dictionary<string, ACTION>();

            //无冲突  
            if (actions[i].ContainsKey(a) == false)
            {
                actions[i][a] = act;

                if(act.type ==  ACTION_TYPE.Accept)
                {
                    accState = i;
                    accSymbol = a;
                }
            }
            //冲突  
            else if (actions[i][a].type != act.type && actions[i][a].num != act.num)
            {
                var prevAct = actions[i][a];
                GixConsole.WriteLine("发生" + prevAct.type.ToString() + "-" + act.type.ToString() + "冲突！");
                throw new Exception("语法动作冲突：ACTION[" + i + ", " + a + "]   old:" + actions[i][a].ToString() + "  new:" + act.ToString());
            }

        }
        public void SetGOTO(int statei, string X, int j)
        {
            //if (X is Terminal) return; //保存终结符的GOTO信息没有必要
            //（但是构建ACTION表时需要）    
            //（可以压缩的时候去掉终结符的GOTO信息）    

            if (gotos.ContainsKey(statei) == false)
                gotos[statei] = new Dictionary<string, GOTO>();

            if (gotos[statei].ContainsKey(X) == false)
                gotos[statei][X] = new GOTO() { stateId = j };
            else if (gotos[statei][X].stateId != j)
                throw new Exception("GOTO表设置冲突：[" + statei + ", " + X + "]  值比较：" + gotos[statei][X].stateId + " vs " + j);
        }

        //压缩    
        public void Compress()
        {
        }

        //查询ACTION表  
        public ACTION ACTION(int i, string terminal)
        {
            if (actions.ContainsKey(i))
            {
                if (actions[i].ContainsKey(terminal))
                {
                    return actions[i][terminal];
                }
            }

            return new ACTION() { type = ACTION_TYPE.Error };
        }

        //查询GOTO表  
        public GOTO GOTO(int i, string X)
        {
            if (gotos.ContainsKey(i))
            {
                if (gotos[i].ContainsKey(X))
                {
                    return gotos[i][X];
                }
            }

            return default;
        }

        //提取所有数据  
        public List<Tuple<int, string, ACTION>> GetAllActions()
        {
            List<Tuple<int, string, ACTION>> result = new List<Tuple<int, string, ACTION>>();
            foreach (var kv in actions)
            {
                foreach(var kv2 in actions[kv.Key])
                {
                    result.Add(new Tuple<int, string, ACTION>(kv.Key, kv2.Key, kv2.Value));
                }
            }
            return result;
        }
        public List<Tuple<int, string, GOTO>> GetAllGotos()
        {
            List<Tuple<int, string, GOTO>> result = new List<Tuple<int, string, GOTO>>();
            foreach (var kv in gotos)
            {
                foreach (var kv2 in gotos[kv.Key])
                {
                    result.Add(new Tuple<int, string, GOTO>(kv.Key, kv2.Key, kv2.Value));
                }
            }
            return result;
        }

        //序列化  
        public string Serialize()
        {
            bool accSet = false;
            StringBuilder strb = new StringBuilder();

            strb.AppendLine("ACTION_TABLE");
            for (int i = 0; i < stateCount; ++i)
            {
                if (actions.ContainsKey(i))
                {
                    strb.AppendLine(i.ToString());
                    Dictionary<string, ACTION> row = actions[i];
                    for (int j = 0; j < this.terminals.Count; ++j)
                    {
                        if (j != 0) strb.Append(",");
                        if (row.ContainsKey(this.terminals[j]))
                        {
                            var act = row[this.terminals[j]];
                            strb.Append(act.ToString());

                            if(act.type == ACTION_TYPE.Accept) accSet = true;
                        }
                    }
                    strb.Append("\n");
                }
            }

            strb.AppendLine("GOTO_TABLE");
            for (int i = 0; i < stateCount; ++i)
            {
                if (gotos.ContainsKey(i))
                {
                    strb.AppendLine(i.ToString());
                    Dictionary<string, GOTO> row = gotos[i];
                    for (int j = 0; j < this.terminals.Count; ++j)
                    {
                        if (j != 0) strb.Append(",");
                        if (row.ContainsKey(this.terminals[j]))
                        {
                            strb.Append(row[this.terminals[j]].ToString());
                        }
                    }
                    for (int j = 0; j < this.nonterminals.Count; ++j)
                    {
                        strb.Append(",");
                        if (row.ContainsKey(this.nonterminals[j]))
                        {
                            strb.Append(row[this.nonterminals[j]].ToString());
                        }
                    }
                    strb.Append("\n");
                }
            }

            strb.AppendLine("ENDTABLE");

            if(accSet == false)
            {
                throw new Exception("错误：表中没有ACC!");
            }

            return strb.ToString();
        }

        //反序列化  
        public void Deserialize(string data)
        {
            //Debug.Log("terminals:");
            //Debug.Log(string.Concat(this.terminals.Select(t => t + ",")));
            //Debug.Log("nonterminals:");
            //Debug.Log(string.Concat(this.nonterminals.Select(nt => nt + ",")));


            StringReader reader = new StringReader(data);

            if (reader.ReadLine() != "ACTION_TABLE") throw new Exception("Deserialze Err 10000");

            while(true)
            {
                string line1 = reader.ReadLine();
                if (line1 == "GOTO_TABLE") break;
                string line2 = reader.ReadLine();

                int idx = int.Parse(line1);

                string[] segments = line2.Split(',');

                if (segments.Length != terminals.Count) throw new Exception("记录长度不一致1");



                for (int i = 0; i < this.terminals.Count; ++i) 
                {
                    if (string.IsNullOrEmpty(segments[i])) continue;
                    SetACTION(idx, terminals[i], Gizbox.LRParse.ACTION.Parse(segments[i]));
                }
            }
            while (true)
            {
                string line1 = reader.ReadLine();
                if (line1 == "ENDTABLE") break;
                string line2 = reader.ReadLine();

                int idx = int.Parse(line1);

                string[] segments = line2.Split(',');

                if (segments.Length != (terminals.Count + nonterminals.Count)) throw new Exception("记录长度不一致2");


                for (int i = 0; i < this.terminals.Count; ++i)
                {
                    if (string.IsNullOrEmpty(segments[i])) continue;
                    SetGOTO(idx, terminals[i], int.Parse(segments[i]));
                }
                for (int i = 0; i < this.nonterminals.Count; ++i)
                {
                    if (string.IsNullOrEmpty(segments[i + this.terminals.Count])) continue;
                    SetGOTO(idx, nonterminals[i], int.Parse(segments[i + this.terminals.Count]));
                }
            }
        }

        //打印该表  
        public void Print()
        {
            StringBuilder strb = new StringBuilder();

            strb.AppendLine("------------------------------------------------------------------------");
            strb.Append("状态\t|");
            foreach (var t in this.terminals)
            {
                strb.Append("\t" + t);
            }
            strb.Append("\t|");
            foreach (var nt in this.nonterminals)//
            {
                //if (nt == @"S'") continue;

                strb.Append("\t" + nt);
            }
            strb.Append("\n");
            strb.AppendLine("------------------------------------------------------------------------");
            for (int i = 0; i < this.stateCount; ++i)
            {
                strb.Append("I" + i + "\t|");
                //ACTION
                foreach (var t in this.terminals)
                {
                    if (actions.ContainsKey(i) && actions[i].ContainsKey(t))
                    {
                        strb.Append("\t" + actions[i][t].ToString());
                    }
                    else
                    {
                        strb.Append("\t ");//err显示为空白  
                    }
                }
                //分隔  
                strb.Append("\t|");
                foreach (var nt in this.nonterminals)//
                {
                    //if (nt == @"S'") continue;

                    if (gotos.ContainsKey(i) && gotos[i].ContainsKey(nt))
                    {
                        strb.Append("\t" + gotos[i][nt].stateId);
                    }
                    else
                    {
                        strb.Append("\t ");//err显示为空白  
                    }
                }
                strb.Append("\n");
            }
            strb.AppendLine("（注意:r后面的产生式编号是从1开始的！）");
            strb.AppendLine("------------------------------------------------------------------------");




            GixConsole.WriteLine(strb.ToString());
        }
    }



}

namespace Gizbox.LALRGenerator
{
    /// <summary>
    /// 生成器输出  
    /// </summary>
    public class ParserData
    {
        //基本信息（语法分析器用不到）      
        public Nonterminal startSymbol = null;//开始符号  
        public Nonterminal augmentedStartSymbol = null; //增广后的开始符号  

        public GrammerSet grammerSet = new();

        //分析表  
        public ParseTable table;

        //状态  
        public List<State> lalrStates;
    }


    /// <summary>
    /// 生成器  
    /// </summary>
    public class LALRGenerator
    {
        //项和产生式查询表  
        public Dictionary<string, Production> productionDic = new Dictionary<string, Production>();
        public Dictionary<(Production, int, Terminal), LR1Item> itemDic = new ();
        public Dictionary<Production, int> productionIdDic = new Dictionary<Production, int>();

        //项列表  
        public List<LR1Item> items = new List<LR1Item>();//Item列表  

        //G'项集族  
        public List<LR1ItemSet> canonicalItemCollection; // 即C  

        //项集族的合并组
        private List<List<int>> groups;


        //符号串的FIRST集缓存  
        private Dictionary<string, TerminalSet> cachedFIRSTOfSymbolStr = new Dictionary<string, TerminalSet>();
        //规范项集族的GOTO缓存  
        private Dictionary<int, Dictionary<Symbol, int>> cachedCanonicalGOTO = new Dictionary<int, Dictionary<Symbol, int>>();



        //输入    
        public Grammer genratorInput;
        //输出  
        public ParserData outputData = new ParserData();







        // ------------------------------------- 构造函数 ------------------------------------------
        public LALRGenerator(Grammer input, string path)
        {
            //生成器输入  
            this.genratorInput = input;


            //判断是否分析器数据文件
            if (System.IO.File.Exists(path))
            {
                if (CompareInfo(path) == true)//信息一致
                {
                    Compiler.Pause("存在分析表： " + path);
                    
                    ReadExistDataFromFile(path);

                    return;
                }
            }


            Compiler.Pause("不存在分析表： " + path);


            //文法初始化  
            InitGrammar();

            Compiler.Pause("即将保存分析表到：" + path);


            //保存分析器数据  
            SaveData(path);
        }
        public LALRGenerator(string content)
        {
            ReadData(content);
            return;
        }

        // ------------------------------------- 数据读写 ------------------------------------------
        public bool CompareInfo(string path)
        {
            using (StreamReader reader = new StreamReader(path))
            {
                reader.ReadLine();
                foreach (var terminal in genratorInput.terminalNames)
                {
                    string t = reader.ReadLine();
                    if (t != terminal)
                    {

                        reader.Close();
                        return false;
                    }
                }
                reader.ReadLine();
                foreach (var nonterminal in genratorInput.nonterminalNames)
                {
                    string nt = reader.ReadLine();
                    if (nt != nonterminal)
                    {

                        reader.Close();
                        return false;
                    }
                }
                reader.ReadLine();
                foreach (var production in genratorInput.productionExpressions)
                {
                    string p = reader.ReadLine();
                    if (p != production)
                    {

                        reader.Close();
                        return false;
                    }
                }

                reader.Close();
            }

            return true;
        }

        public void ReadExistDataFromFile(string path)
        {
            string content = System.IO.File.ReadAllText(path);
            ReadData(content);
        }
        public void ReadData(string content)
        {
            using (StringReader reader = new StringReader(content))
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == "***Terminals***") break;
                }

                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == "***Nonterminals***") break;
                    NewTerminal(line);
                }

                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == "***Productions***") break;
                    NewNonterminal(line);
                }

                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == "***States***") break;
                    NewProduction(line);
                }

                outputData.startSymbol = outputData.grammerSet.nonterminalDict["S"];
                outputData.augmentedStartSymbol = outputData.grammerSet.nonterminalDict[@"S'"];


                outputData.lalrStates = new List<State>();
                while (true)
                {
                    if (reader.ReadLine() == "***Table***") break;

                    string strIdx = reader.ReadLine();
                    string name = reader.ReadLine();

                    LR1ItemSet itemset = new LR1ItemSet();
                    if (reader.ReadLine() != "***Set***") throw new Exception("not match ***Set***");

                    while (true)
                    {
                        string line = reader.ReadLine();
                        if (line == "***EndSet***") break;

                        var item = GetOrCreateLR1Item(line);
                        itemset.AddImmediate(item);
                    }

                    if (reader.ReadLine() != "***EndState***") throw new Exception("not match ***EndState***");

                    State state = new State(int.Parse(strIdx), name, itemset);
                    outputData.lalrStates.Add(state);
                }

                string strTable = reader.ReadToEnd();

                outputData.table = new ParseTable();
                outputData.table.stateCount = outputData.lalrStates.Count;
                outputData.table.terminals = outputData.grammerSet.terminalDict.Select(t => t.Value.name).ToList();
                outputData.table.nonterminals = outputData.grammerSet.nonterminalDict.Select(nont => nont.Value.name).ToList();

                outputData.table.Deserialize(strTable);
            }
        }

        public void SaveData(string path)
        {
            StringBuilder strb = new StringBuilder();
            strb.AppendLine("***Raw Terminals***");
            foreach (var terminal in genratorInput.terminalNames)
            {
                strb.AppendLine(terminal);
            }
            strb.AppendLine("***Raw Nonterminals***");
            foreach (var nonterminal in genratorInput.nonterminalNames)
            {
                strb.AppendLine(nonterminal);
            }
            strb.AppendLine("***Raw Productions***");
            foreach (var production in genratorInput.productionExpressions)
            {
                strb.AppendLine(production);
            }

            strb.AppendLine("");
            strb.AppendLine("");
            strb.AppendLine("");
            strb.AppendLine("***Data***");


            strb.AppendLine("***Terminals***");
            foreach (var (name, t) in outputData.grammerSet.terminalDict)
            {
                strb.AppendLine(t.name);
            }
            strb.AppendLine("***Nonterminals***");
            foreach (var (name, nt) in outputData.grammerSet.nonterminalDict)
            {
                strb.AppendLine(nt.name);
            }
            strb.AppendLine("***Productions***");
            foreach (var (name, production) in outputData.grammerSet.productionDict)
            {
                strb.AppendLine(production.ToExpression());
            }
            strb.AppendLine("***States***");
            foreach (var state in outputData.lalrStates)
            {
                strb.AppendLine("***State***");
                strb.AppendLine(state.idx.ToString());
                strb.AppendLine(state.name);

                strb.AppendLine("***Set***");

                foreach (var item in state.set.ToArray())
                {
                    strb.AppendLine(item.ToExpression());
                }

                strb.AppendLine("***EndSet***");

                strb.AppendLine("***EndState***");
            }
            strb.AppendLine("***Table***");

            strb.Append(outputData.table.Serialize());

            File.WriteAllText(path, strb.ToString());
        }

        // ------------------------------------- 初始化 ------------------------------------------

        /// <summary>
        /// 文法初始化  
        /// </summary>
        public void InitGrammar()
        {
            // *** 文法初始化 ***   

            //终结符
            foreach (var terminalName in genratorInput.terminalNames)
            {
                NewTerminal(terminalName);
            }

            //非终结符  
            foreach (var nonterminalName in genratorInput.nonterminalNames)
            {
                NewNonterminal(nonterminalName);
            }
            outputData.startSymbol = outputData.grammerSet.nonterminalDict["S"];

            //产生式  
            foreach (var productionExpr in genratorInput.productionExpressions)
            {
                NewProduction(productionExpr);
            }


            // ****** 符号和产生式初始化结束 ******  

            //添加结束符  
            NewTerminal("$");//结束符  

            //文法增广  
            GixConsole.WriteLine("文法初始化完成...\n文法增广...");
            outputData.augmentedStartSymbol = NewNonterminal(@"S'");
            NewProduction(@"S' -> " + outputData.startSymbol.name);
            //CRE：S' -> S产生式是隐式规约的（实际不归约）


            //FIRST集计算（从开始符号递归）  
            GixConsole.WriteLine("开始计算FIRST集");
            InitFIRSTCollections();


            // DEBUG  
            {
                GixConsole.WriteLine("\n\n\n***输出FIRST集计算结果*** ");
                foreach (var (_, nont) in outputData.grammerSet.nonterminalDict)
                {
                    GixConsole.WriteLine("符号" + nont.name + "的FIRST集:");
                    GixConsole.WriteLine("{ " + string.Concat(nont.cachedFIRST.ToArray().Select(s => (s != null ? s.name : "ε") + ",")) + "}");
                }
            }




            GixConsole.WriteLine("开始构造项集族");

            //构造规范LR(1)项集族（同时缓存规范GOTO表）    
            InitCanonicalItemsCollection();


            //合并项集  
            InitLALRCollection();

            //构造LALR分析表  
            InitLALRTable();
        }

        /// <summary>
        /// 初始化所有符号的FIRST集    
        /// </summary>
        private void InitFIRSTCollections()
        {
            FIRST(outputData.startSymbol);
            foreach (var (_, nt) in outputData.grammerSet.nonterminalDict)
            {
                FIRST(nt);
            }
        }

        /* 伪代码：项集族计算  
        void items(G')
        {
            将C初始化为{CLOSURE}({[S' -> ·S, $]});
            repeat
                for(C中每个项集I)
                    for(每个文法符号X)
                        if(GOTO(I,X)非空且不在C中)
                            将GOTO(I,X)加入C中;
            until(不再有新的项集加入到C中);
        }*/

        /// <summary>
        /// 构造LR(1)项集族（同时构造规范GOTO表缓存）      
        /// </summary>
        private void InitCanonicalItemsCollection()
        {
            List<LR1ItemSet> C = new List<LR1ItemSet>();
            LR1ItemSet initSet = new LR1ItemSet(); initSet.AddDistinct(GetOrCreateLR1Item(@"S' -> · " + outputData.startSymbol.name + ", $"));
            initSet = CLOSURE(initSet);
            C.Add(initSet);


            Compiler.Pause("开始构造规范LR(1)项集族");

            while (true)
            {
                bool anyAdded = false;
                int count = 0;


                //for(C中每个项集I)
                for (int i = 0; i < C.Count; ++i)
                {
                    var I = C[i];
                    GixConsole.WriteLine("遍历第" + i + "个项集族");

                    //for(每个文法符号X)
                    foreach (var (_, X) in outputData.grammerSet.symbolDict)//文法符号X可以是终结符和非终结符  
                    {
                        //if (GOTO(I, X)非空且不在C中)  
                        var gotoix = GOTO(I, X);

                        if (gotoix.Count > 0 && Utils_ExistSameSetInC(gotoix, C) == false)
                        {
                            //将GOTO(I, X)加入C中;
                            C.Add(gotoix);
                            anyAdded = true;
                            count++;
                            GixConsole.WriteLine("将第" + C.Count + "个状态加入项集族");

                            //缓存  
                            int jIdx = C.Count - 1;
                            if (cachedCanonicalGOTO.ContainsKey(i) == false)
                                cachedCanonicalGOTO[i] = new Dictionary<Symbol, int>();
                            if (cachedCanonicalGOTO[i].ContainsKey(X) == false)
                            {
                                cachedCanonicalGOTO[i][X] = jIdx;
                                GixConsole.WriteLine("cache goto(" + i + ", " + X.name + ") = " + jIdx);
                            }
                        }
                    }
                }

                Compiler.Pause("本轮加入了" + count + "个项集到C中");

                if (anyAdded == false) break;
            }

            this.canonicalItemCollection = C;


            Compiler.Pause("构造规范LR(1)项集族完毕");


            //DEBUG
            {
                GixConsole.WriteLine("\n\n\n***输出项集族***");
                for (int i = 0; i < this.canonicalItemCollection.Count; ++i)
                {
                    GixConsole.WriteLine("项集I" + i + ":");

                    GixConsole.WriteLine(this.canonicalItemCollection[i].ToExpression());
                }
            }

            Compiler.Pause("");

            //剩余的规范GOTO缓存补全(必要的)    
            for (int i = 0; i < this.canonicalItemCollection.Count; ++i)
            {
                foreach (var (_, symbol) in outputData.grammerSet.symbolDict)
                {
                    if (cachedCanonicalGOTO.ContainsKey(i) && cachedCanonicalGOTO[i].ContainsKey(symbol)) continue;

                    var gotoix = GOTO(this.canonicalItemCollection[i], symbol);

                    for (int j = 0; j < this.canonicalItemCollection.Count; ++j)
                    {
                        if (gotoix.IsSameTo(this.canonicalItemCollection[j]))
                        {
                            if (cachedCanonicalGOTO.ContainsKey(i) == false)
                                cachedCanonicalGOTO[i] = new Dictionary<Symbol, int>();
                            if (cachedCanonicalGOTO[i].ContainsKey(symbol) == false)
                                cachedCanonicalGOTO[i][symbol] = j;

                            break;
                        }
                    }
                }
            }

            //Pause
            Compiler.Pause("合并前总共" + this.canonicalItemCollection.Count + "个项集。");


            //DEBUG  
            {
                GixConsole.WriteLine("------------------------规范LR(1)项集族的GOTO表--------------------------");
                GixConsole.WriteLine("-------------------------------------------------------------------------");
                GixConsole.Write("状态\t|");
                foreach (var (_, X) in outputData.grammerSet.symbolDict)
                {
                    GixConsole.Write("\t" + X.name);
                }
                GixConsole.Write("\n");
                GixConsole.WriteLine("-------------------------------------------------------------------------");
                for (int i = 0; i < this.canonicalItemCollection.Count; ++i)
                {
                    if (cachedCanonicalGOTO.ContainsKey(i))
                    {
                        GixConsole.Write(i + "\t|");
                        foreach (var (_, X) in outputData.grammerSet.symbolDict)
                        {
                            if (cachedCanonicalGOTO[i].ContainsKey(X))
                            {
                                GixConsole.Write("\t" + cachedCanonicalGOTO[i][X]);
                            }
                            else
                            {
                                GixConsole.Write("\t ");
                            }
                        }
                        GixConsole.Write('\n');
                    }
                    else
                    {
                        GixConsole.Write(i + "\t|\n");
                    }
                }
                GixConsole.WriteLine("-------------------------------------------------------------------------");

                //Pause
                Compiler.Pause("规范LR(1)项集族的GOTO表构建完毕");
            }
        }

        /// <summary>
        /// 合并项集并构造LALR分析表      
        /// </summary>
        private void InitLALRCollection()
        {
            //计算重新分的组  
            List<int> markedSet = new List<int>();
            this.groups = new List<List<int>>();
            int gidx = -1;
            for (int i = 0; i < this.canonicalItemCollection.Count - 1; ++i)
            {
                if (markedSet.Contains(i)) continue;

                bool anySame = false;
                for (int j = i + 1; j < this.canonicalItemCollection.Count; ++j)
                {
                    if (markedSet.Contains(j)) continue;

                    if (Utils_IsSameCore(this.canonicalItemCollection[i], this.canonicalItemCollection[j]))
                    {
                        GixConsole.WriteLine("第" + i + "个项集和第" + j + "个项集有相同核心，可以合并。");

                        if (anySame == false)
                        {
                            markedSet.Add(i);
                            groups.Add(new List<int>());
                            gidx++;
                            groups[gidx].Add(i);
                        }
                        anySame = true;

                        markedSet.Add(j);
                        groups[gidx].Add(j);
                    }
                }
                if (anySame == false)
                {
                    groups.Add(new List<int>());
                    gidx++;
                    groups[gidx].Add(i);
                }
            }
            //DEBUG  
            for (int i = 0; i < groups.Count; ++i)
            {
                GixConsole.WriteLine("group" + i);
                GixConsole.WriteLine("{" + string.Concat(groups[i].Select(num => num.ToString() + ",")) + "}");
            }
            //新的LALR项集族
            outputData.lalrStates = new List<State>();

            for (int i = 0; i < groups.Count; ++i)
            {
                var g = groups[i];

                if (g.Count == 1)
                {
                    State state = new State(i, "I_" + g[0], this.canonicalItemCollection[g[0]]);
                    outputData.lalrStates.Add(state);

                    GixConsole.WriteLine("创建了状态" + state.idx);
                }
                else
                {
                    IEnumerable<LR1ItemSet> setArr = g.Select(idx => this.canonicalItemCollection[idx]);

                    var unionSet = Utils_UnionSet(setArr);
                    State state = new State(i, "I" + string.Concat(g.Select(num => "_" + num.ToString())), unionSet);
                    outputData.lalrStates.Add(state);

                    GixConsole.WriteLine("创建了状态" + state.idx);
                }
            }


            //Pause
            Compiler.Pause("合并后的状态数：" + outputData.lalrStates.Count);

            //DEBUG
            {
                GixConsole.WriteLine("\n\n\n***新的LALR项集族***");
                for (int i = 0; i < outputData.lalrStates.Count; ++i)
                {
                    GixConsole.WriteLine("状态" + outputData.lalrStates[i].name + ":");

                    GixConsole.WriteLine(outputData.lalrStates[i].set.ToExpression());
                }
            }
        }

        /// <summary>
        /// 构建LALR语法分析表
        /// </summary>
        private void InitLALRTable()
        {
            this.outputData.table = new ParseTable();

            // *** LALR分析表的基本信息设置 ***   
            outputData.table.stateCount = outputData.lalrStates.Count;
            outputData.table.terminals = outputData.grammerSet.terminalDict.Select(t => t.Value.name).ToList();
            outputData.table.nonterminals = outputData.grammerSet.nonterminalDict.Select(nont => nont.Value.name).ToList();

            // *** LALR分析表的GOTO表构造 ***   
            foreach (var key in cachedCanonicalGOTO.Keys)
            {
                foreach (var X in cachedCanonicalGOTO[key].Keys)
                {
                    int _i = key;
                    int _j = cachedCanonicalGOTO[key][X];

                    int new_i = this.groups.FindIndex(g => g.Contains(_i));
                    int new_j = this.groups.FindIndex(g => g.Contains(_j));

                    this.outputData.table.SetGOTO(new_i, X.name, new_j);
                }
            }


            // *** LALR分析表的ACTION表构造 ***  

            //1) 构造项G'的LR(1)项集族。    
            //2) 语法分析器的状态i根据$I_i$构造得到。状态i的语法分析动作按下面的规则确定：  
            //    +如果[A->α·aβ, b]在I_i中，并且GOTO(I_i, a) = I_j，那么将ACTION[i, a]设置为“移入j”。（a必须是一个终结符号）  
            //    +如果[A->α·, a]在I_i中且A≠S'，那么将ACTION[i,a]设置为“规约A->α”。    
            //    +如果[S'->S·, $]在I_i中，那么将ACTION[i,$]设置为“接受”。    
            //    +如果上述规则会产生任何冲突动作，我们就说这个文法不是LR(1)的，这个算法无法为这个文法生成语法分析器。    

            //3) 状态i相对于各个非终结符号A的goto转换按照下面的规则构造得到：如果GOTO(I_i, A) = I_j，那么GOTO[i, A] = j。    
            //4) 所有没有按照规则(2)和(3)定义的分析表条目都设置为“报错”。    
            //5) 语法分析器的初始状态是由包含[S'->·S,$]的项集构造得到的状态。    

            for (int i = 0; i < outputData.lalrStates.Count; ++i)
            {
                var statei = outputData.lalrStates[i];
                foreach (var item in statei.set)
                {
                    //如果[A->α·aβ, b]在I_i中    并且   GOTO(I_i, a) = I_j 
                    if (item.iDot < item.production.body.Length && (item.production.body[item.iDot] is Terminal))
                    {
                        //那么将ACTION[i, a]设置为“移入j”。（a必须是一个终结符号）  
                        string a = (item.production.body[item.iDot] as Terminal).name;
                        int j = outputData.table.GOTO(i, a).stateId;

                        if (outputData.table.CheckACTION(i, a) == false)
                        {
                            outputData.table.SetACTION(i, a, ACTION.Shift(j));
                        }
                        else
                        {
                            //处理二义性等文法冲突...  
                        }
                    }

                    //如果[A->α·, a]在I_i中且A≠S'，那么将ACTION[i,a]设置为“规约A->α”。    
                    if (item.iDot == item.production.body.Length && item.production.head != outputData.augmentedStartSymbol)
                    {
                        var a = item.lookahead.name;
                        int productionId = productionIdDic[item.production];

                        if (outputData.table.CheckACTION(i, a) == false)
                        {
                            outputData.table.SetACTION(i, a, ACTION.Reduce(productionId));
                        }
                        else
                        {
                            //处理二义性等文法冲突...  
                        }

                    }

                    //如果[S'->S·, $]在I_i中，那么将ACTION[i,$]设置为“接受”。    
                    if (item.production.head == outputData.augmentedStartSymbol &&
                        item.production.body.Length == 1 &&
                        item.production.body[0] == outputData.startSymbol &&
                        item.iDot == 1 &&
                        item.lookahead.name == "$"
                        )
                    {
                        outputData.table.SetACTION(i, item.lookahead.name, ACTION.Acc);
                    }
                }
            }



            //DEBUG
            {
                outputData.table.Print();
                Compiler.Pause("\nLALR分析表构建完毕");
            }
        }

        // ------------------------------------- NewElement ------------------------------------------
        private Terminal NewTerminal(string name)
        {
            Terminal terminal = new Terminal(outputData.grammerSet, name);

            return terminal;
        }

        private Nonterminal NewNonterminal(string name)
        {
            Nonterminal nonterminal = new Nonterminal(outputData.grammerSet, name);

            return nonterminal;
        }

        private void NewProduction(string expression)
        {
            string[] segments = expression.Split(' ');
            if (segments.Length < 2) return;
            if (segments[1] != "->") return;

            int bodyLength = segments.Length - 2;

            Nonterminal head = outputData.grammerSet.nonterminalDict[segments[0]];

            if (head.productions == null) head.productions = new List<Production>();


            //是ε产生式  
            if (segments.Length == 3 && segments[2] == "ε")
            {
                Production newProduction = new Production();
                newProduction.head = head;
                newProduction.body = new Symbol[0];//body长度0  

                //ADD
                head.productions.Add(newProduction);
                outputData.productions.Add(newProduction);
                productionDic[expression] = newProduction;
                productionIdDic[newProduction] = outputData.productions.Count - 1;

            }
            //不是ε产生式  
            else
            {
                Production newProduction = new Production();
                newProduction.head = head;
                newProduction.body = new Symbol[bodyLength];

                //ADD
                head.productions.Add(newProduction);
                outputData.productions.Add(newProduction);
                productionDic[expression] = newProduction;
                productionIdDic[newProduction] = outputData.productions.Count - 1;

                for (int i = 2; i < segments.Length; ++i)
                {
                    Symbol symbol = outputData.grammerSet.symbolDict[segments[i]];

                    if (symbol == null) throw new Exception("不存在名称为" + segments[i] + "的符号！");

                    newProduction.body[i - 2] = symbol;
                }
            }
        }

        private void NewLR1Item(string expression) //形式: "A -> α · β, a"  
        {
            var (production, idot, lookahead) = SplitItemExpression(expression);

            //ADD  
            CreateLR1Item(production, idot, lookahead);
        }

        private (Production prod, int idot, Terminal lookahead) SplitItemExpression(string expression)
        {
            if(string.IsNullOrWhiteSpace(expression))
            {
                throw new Exception("LR1项不合规，分量前加空格!:" + expression);
            }

            int lastSpaceIdx = -1;
            for(int i = expression.Length - 1; i > -1; --i)
            {
                if(expression[i] == ' ')
                {
                    lastSpaceIdx = i;
                    break;
                }
            }
            if(lastSpaceIdx == -1)
                throw new Exception("LR1项不合规，分量前加空格!:" + expression);

            string lookaheadStr = expression.Substring(lastSpaceIdx).Trim();//a
            string itemNonLookahead = expression.Substring(0, lastSpaceIdx - 1).Trim();  //A -> α·β

            string[] segments = itemNonLookahead.Split(' ');
            if(segments.Length < 3 || segments[1] != "->")
            { throw new Exception("LR1项格式错误"); }

            // find production
            string productionExpression;
            if(segments.Length == 3 && segments[2] == "·")
            {
                productionExpression = segments[0] + " -> ε";
            }
            else
            {
                productionExpression = itemNonLookahead.Replace(" ·", "");
            }

            if(productionDic.ContainsKey(productionExpression) == false)
            { throw new Exception("没找到产生式:" + productionExpression); }
            Production production = productionDic[productionExpression];

            // Find iDot
            int idot = -1;
            for(int i = 0; i < segments.Length; ++i)
            {
                if(segments[i] == "·")
                {
                    idot = i - 2;
                    break;
                }
            }
            if(idot == -1)
                throw new Exception(@"项 " + expression + " 不合规:没有'·'");

            // lookahead
            Terminal lookahead = outputData.grammerSet.terminalDict[lookaheadStr];
            if(lookahead == null)
            { throw new Exception("没找到终结符: [" + lookaheadStr + "](" + lookaheadStr.Length + ")"); }

            return (production, idot, lookahead);
        }


        private LR1Item CreateLR1Item(Production production, int idot, Terminal lookahead)
        {
            if (lookahead == null)
            {
                throw new Exception("ε不能作为lookahead！");
            }

            LR1Item newItem = new LR1Item()
            {
                production = production,
                iDot = idot,
                lookahead = lookahead,
            };
            if (newItem.iDot > newItem.production.body.Length)
            {
                throw new Exception("LR1项不合规");
            }

            this.items.Add(newItem);
            this.itemDic.Add((production, idot, lookahead), newItem);

            return newItem;
        }

        // ------------------------------------- Getters ------------------------------------------

        public Production GetProduction(string expression)
        {
            if (productionDic.ContainsKey(expression))
            {
                return productionDic[expression];
            }
            else
            {
                throw new Exception("没找到产生式:" + expression);
            }
        }

        public LR1Item GetOrCreateLR1Item(string expression)
        {
            var itemTuple = SplitItemExpression(expression);

            if (itemDic.ContainsKey(itemTuple))
            {
                return itemDic[itemTuple];
            }
            else
            {
                NewLR1Item(expression);
                if (itemDic.ContainsKey(itemTuple) == false) throw new Exception("没有item: " + expression + "\n已有的项:" + string.Concat(itemDic.Keys.Select(k => k + "\n")));
                return itemDic[itemTuple];
            }
        }

        public LR1Item GetOrCreateLR1Item(Production production, int idot, Terminal lookahead)
        {
            foreach (var item in this.items)
            {
                if (item.production != production) continue;
                if (item.iDot != idot) continue;
                if (item.lookahead != lookahead) continue;

                return item;
            }

            return CreateLR1Item(production, idot, lookahead);
        }


        // ---------------------------------- 工具 -----------------------------------------
        private bool Utils_ExistSameSetInC(LR1ItemSet setNew, List<LR1ItemSet> C)
        {
            if (setNew.Count == 0)
            {
                throw new Exception("要判断重复的闭包集为空");
            }

            bool anySame = false;
            foreach (var set in C)
            {
                bool allItemSame = true;

                foreach (var itmNew in setNew)
                {
                    if (set.AnyRepeat(itmNew) == false)
                    {
                        allItemSame = false;
                        break;
                    }
                }

                if (allItemSame && setNew.Count == set.Count)
                {
                    anySame = true;
                    break;
                }
            }


            return anySame;
        }

        private bool Utils_IsSameCore(LR1ItemSet set1, LR1ItemSet set2)
        {
            //第1个核心  
            List<(Production prod, int idot)> core1 = new List<(Production prod, int idot)>();
            foreach (var itm1 in set1)
            {
                var left = itm1.GetLeft();
                if (core1.Contains(left) == false)
                {
                    core1.Add(left);
                }
            }

            //第2个核心  
            List<(Production prod, int idot)> core2 = new List<(Production prod, int idot)>();
            foreach (var itm2 in set2)
            {
                var left = itm2.GetLeft();
                if (core2.Contains(left) == false)
                {
                    core2.Add(left);
                }
            }

            //不重复第一分量的数量不一样  
            if (core1.Count != core2.Count) return false;

            //逐个比较  
            bool allSame = true;
            foreach (var left1 in core1)
            {
                if (core2.Contains(left1) == false)
                {
                    allSame = false;
                    break;
                }
            }

            return allSame;
        }

        private LR1ItemSet Utils_UnionSet(IEnumerable<LR1ItemSet> setArr)
        {
            LR1ItemSet setNew = new LR1ItemSet();
            foreach (var set in setArr)
            {
                foreach (var itm in set)
                {
                    setNew.AddDistinct(itm);
                }
            }
            return setNew;
        }



        // ---------------------------------- 函数 -----------------------------------------


        /// <summary>
        /// FIRST集计算（有缓存）    
        /// </summary>
        private TerminalSet FIRST(Symbol symbol)
        {
            if (symbol == null) throw new Exception("不能计算null的FIRST集！");
            //Debug.Log("计算" + symbol.name + "的FIRST集...");

            //访问过的节点（直接读取缓存）  
            if (symbol.cachedFIRST != null)
            {
                return symbol.cachedFIRST;
            }
            //未访问过的节点  
            else
            {
                TerminalSet newSet = new TerminalSet();
                symbol.cachedFIRST = (newSet);//缓存(即标记为visited)  

                //终结符的FIRST集只包含它自己  
                if (symbol is Terminal)
                {
                    newSet.AddDistinct(symbol as Terminal);
                    return symbol.cachedFIRST;
                }
                //非终结符
                else
                {
                    Nonterminal nt = symbol as Nonterminal;
                    foreach (var production in nt.productions)
                    {
                        //ε产生式  
                        if (production.IsεProduction())
                        {
                            newSet.AddDistinct(null);
                            continue;
                        }

                        int i = 0;
                        while (i < production.body.Length)
                        {
                            Symbol currentSymbol = production.body[i];

                            //DEBUG  
                            //if (currentSymbol == null)
                            //{
                            //    throw new Exception("错误的产生式:" + production.ToExpression() + "，含有ε。");
                            //}

                            if (currentSymbol is Terminal)
                            {
                                //终结符 -> 添加并结束  
                                newSet.AddDistinct(currentSymbol as Terminal);
                                break;
                            }
                            else if (currentSymbol != symbol)
                            {
                                var currentFirst = FIRST(currentSymbol);
                                newSet.UnionWith(currentFirst);

                                //如果当前符号的FIRST没有ε -> 到此结束  
                                if (currentFirst.Contains(null) == false)
                                {
                                    break;
                                }
                                //4.19修改添加    
                                else
                                {
                                    i++; break;
                                }
                            }
                            else // currentSymbol == symbol
                            {
                                //后续有本身  ->  左递归  ->  到此结束  
                                if (!production.body.Skip(i).Contains(symbol))
                                {
                                    break;
                                }

                                //到最后一个字符了还未结束 -> 添加ε  
                                if (++i >= production.body.Length)
                                {
                                    newSet.AddDistinct(null);
                                }
                            }
                        }
                    }


                    return newSet;
                }
            }
        }
        private TerminalSet FIRST(IEnumerable<Symbol> sstr)
        {
            string key = string.Concat(sstr.Select(s => (s != null ? s.name : "ε") + " "));

            //有缓存缓存  
            if (cachedFIRSTOfSymbolStr.ContainsKey(key))
            {
                return cachedFIRSTOfSymbolStr[key];
            }


            //无缓存  
            TerminalSet newSet = new TerminalSet();
            cachedFIRSTOfSymbolStr[key] = newSet;
            foreach (var s in sstr)
            {
                if (s is Nonterminal)
                {
                    //该产生式中的该非终结符 可以 推导ε
                    if ((s as Nonterminal).HasεProduction() == true)
                    {
                        newSet.UnionWith(FIRST(s));

                        continue;//跳到产生式下一个符号  
                    }
                    //该产生式中的该非终结符 不可以 推导ε
                    else
                    {
                        newSet.UnionWith(FIRST(s));

                        break;//跳出该产生式  
                    }
                }
                else if (s is Terminal)
                {
                    newSet.AddDistinct(s as Terminal);
                    break;//跳出该产生式  
                }
            }

            return newSet;
        }

        /* CLOSURE伪代码  
        SetOfItems CLOSURE(I)
        {
            repeat
                for(I中的每个项[A->α·Bβ,a])
                    for(G'中每个产生式B->γ)
                        for(FIRST(βa)中的每个终结符b)
                            将[B->·γ, b]加入到集合I中;
            until(不能向I中加入更多项);  
            return I;
        }*/

        /// <summary>
        /// CLOSURE（生成新的集合，不改变原集合）
        /// </summary>
        public LR1ItemSet CLOSURE(LR1ItemSet oldI)
        {
            LR1ItemSet I = oldI.Clone();

            for (int i = 0; i < 9999; ++i)
            {
                bool anyAdded = false;

                var tmpArr = I.ToArray();
                for (int j = 0; j < tmpArr.Length; ++j)
                {
                    var itm = tmpArr[j];

                    //判断是不是α·Bβ
                    if (itm.iDot > itm.production.body.Length - 1) continue;
                    if ((itm.production.body[itm.iDot] is Nonterminal) == false) continue;

                    //非终结符B  
                    Nonterminal B = itm.production.body[itm.iDot] as Nonterminal;
                    Terminal a = itm.lookahead;
                    List<Symbol> β = itm.production.body.Skip(itm.iDot + 1).ToList();
                    List<Symbol> βa = new List<Symbol>(); βa.AddRange(β); βa.Add(a);

                    //非终结符B的所有产生式  
                    Production[] productionsOfB = outputData.grammerSet.productions.Where(p => p.head == B).ToArray();
                    foreach (var production in productionsOfB)
                    {
                        TerminalSet firstβa = FIRST(βa);

                        foreach (var b in firstβa.ToArray())
                        {
                            //b可能是ε
                            if (b == null) continue;

                            var itemtarget = GetOrCreateLR1Item(production, 0, b);
                            if (I.AnyRepeat(itemtarget) == false)
                            {
                                I.AddDistinct(itemtarget);
                                anyAdded = true;
                            }
                        }
                    }
                }

                if (anyAdded == false)
                {
                    break;
                }
            }

            return I;
        }

        /* GOTO伪代码  
        SetOfItems GOTO(I, X)
        {
            将J初始化为空集;
            for(I中的每个项[A->α·Xβ, a])
                将项[A->αX·β, a]加入到集合J中;
            return CLOSURE(J);
        }
        */

        /// <summary>
        /// GOTO（实时计算，没有缓存）  
        /// </summary>
        public LR1ItemSet GOTO(LR1ItemSet I, Symbol X)//文法符号X可以是终结符和非终结符  
        {
            LR1ItemSet J = new LR1ItemSet();

            foreach (var item in I)
            {
                //过滤不是[A->α·Xβ, a]的项  
                if (item.iDot > item.production.body.Length - 1) continue;
                if (item.production.body[item.iDot] != X) continue;

                //ADD  
                LR1Item itmAdd = GetOrCreateLR1Item(item.production, item.iDot + 1, item.lookahead);
                if (J.AnyRepeat(itmAdd) == false)
                {
                    J.AddDistinct(itmAdd);
                }
            }

            var closure = CLOSURE(J);
            return closure;
        }


        // ---------------------------------- 获取输出 --------------------------------------  

        public ParserData GetResult()
        {
            return this.outputData;
        }
    }
}
