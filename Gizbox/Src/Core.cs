using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;



namespace Gizbox
{
    /// <summary>
    /// 模式  
    /// </summary>
    public enum PatternType
    {
        Keyword,
        Operator,
        Id,
        Literal,
    }

    /// <summary>
    /// 词法单元  
    /// </summary>
    public class Token
    {
        /// <summary>
        /// 词法单元名  
        /// </summary>
        public string name;

        /// <summary>
        /// 类型  
        /// </summary>
        public PatternType patternType;


        /// <summary>
        /// 行号    
        /// </summary>
        public int line;

        /// <summary>
        /// 行内起始位置  
        /// </summary>
        public int start;

        /// <summary>
        /// 行内结束位置  
        /// </summary>
        public int length;

        /// <summary>
        /// 属性值（一般是词素或者指针）    
        /// </summary>
        public string attribute;


        public Token(string name, PatternType type, string attribute, int lineCount, int start, int length)
        {
            this.name = name;
            this.patternType = type;
            this.attribute = attribute;
            this.line = lineCount;
            this.start = start;
            this.length = length;
        }
        public override string ToString()
        {
            return "<" + name + (string.IsNullOrEmpty(attribute) ? "" : ("," + attribute)) + ">";
        }
    }




    /// <summary>
    /// 符号表  
    /// </summary>
    [DataContract(IsReference = true)]
    public class SymbolTable
    {
        public enum RecordCatagory
        {
            Variable,
            Constant,
            Param,
            Function,
            Class,
            Other
        }
        public enum TableCatagory
        {
            GlobalScope,
            StmtBlockScope,
            ClassScope,
            LoopScope,
            FuncScope,
        }
    [DataContract(IsReference = true)]
        public class Record
        {
            [DataMember]
            public string name;//唯一  
            [DataMember]
            public string rawname;//不唯一 如果是functionname则不应该包含mangle部分  
            [DataMember]
            public RecordCatagory category;
            [DataMember]
            public string typeExpression;
            [DataMember]
            public long addr;
            [DataMember]
            public string initValue;
            [DataMember]
            public SymbolTable envPtr;
        }

        //符号表名称  
        [DataMember]
        public string name;

        //符号表类型  
        [DataMember]
        public TableCatagory tableCatagory;

        //符号表关系    
        [DataMember]
        public int depth;
        [DataMember]
        public SymbolTable parent;
        [DataMember]
        public List<SymbolTable> children = new List<SymbolTable>();

        //条目数据  
        [DataMember]
        public Dictionary<string, Record> records;






        //构造  
        public SymbolTable(string name, TableCatagory tableCatagory, SymbolTable parentTable = null)
        {
            this.name = name;
            this.tableCatagory = tableCatagory;
            this.records = new Dictionary<string, Record>();

            if(parentTable != null)
            {
                this.parent = parentTable;
                parentTable.children.Add(this);
                this.depth = this.parent.depth + 1;
            }
            else
            {
                this.depth = 0;
            }
        }



        //包含信息  
        public bool ContainRecordName(string name)
        {
            return records.ContainsKey(name);
        }
        public bool ContainRecordRawName(string rawname)
        {
            if (records.ContainsKey(rawname)) return true;
            foreach (var kv in records)
            {
                if (kv.Value.rawname == rawname) return true;
            }
            return false;
        }


        //查询信息  
        public Record GetRecord(string symbolName)
        {
            if (records.ContainsKey(symbolName) == false)
            {
                this.Print();
                throw new Exception(this.name + "表中获取不到记录：" + symbolName);
            }
            return records[symbolName];
        }
        public Record GetRecordByRawname(string rawSymbolName)
        {
            foreach (var kv in records)
            {
                if (kv.Value.rawname == rawSymbolName) return kv.Value;
            }
            throw new Exception(this.name + "表中获取不到记录原名：" + rawSymbolName);
        }
        public void GetAllRecordByRawname(string rawSymbolName, List<SymbolTable.Record> result)
        {
            foreach(var kv in records)
            {
                if(kv.Value.rawname == rawSymbolName)
                    result.Add(kv.Value);
            }
        }

        //（仅为类符号表时）是子类
        public bool Class_IsSubClassOf(string baseClassName)
        {
            if(this.name == baseClassName) return true;

            if(records.ContainsKey("base") == true)
            {
                return records["base"].envPtr.Class_IsSubClassOf(baseClassName);
            }

            return false;
        }
        //（仅为类符号表时）在本类的符号表和基类符号表中查找  
        public Record Class_GetMemberRecordInChain(string symbolName)
        {
            if (this.tableCatagory != TableCatagory.ClassScope) 
                throw new Exception("必须是类的符号表");


            if (records.ContainsKey(symbolName))
            {
                return records[symbolName];
            }
            else
            {
                if(records.ContainsKey("base") == true)
                {
                    return records["base"].envPtr.Class_GetMemberRecordInChain(symbolName);
                }
                else
                {
                    return null;
                }
            }
        }
        public void Class_GetAllMemberRecordInChain(string symbolName, List<Record> result)
        {
            if(this.tableCatagory != TableCatagory.ClassScope)
                throw new Exception("必须是类的符号表");

            if(records.ContainsKey(symbolName))
            {
                result.Add(records[symbolName]);
            }

            if(records.ContainsKey("base") == true)
            {
                records["base"].envPtr.Class_GetAllMemberRecordInChain(symbolName, result);
            }
        }
        public void Class_GetAllMemberRecordInChainByRawname(string rawname, List<Record> result)
        {
            if(this.tableCatagory != TableCatagory.ClassScope)
                throw new Exception("必须是类的符号表");

            foreach(var kv in records)
            {
                if(kv.Value.rawname == rawname)
                    result.Add(kv.Value);
            }

            if(records.ContainsKey("base") == true)
            {
                records["base"].envPtr.Class_GetAllMemberRecordInChainByRawname(rawname,result);
            }
        }


        //获取某类型记录  
        public List<Record> GetByCategory(RecordCatagory catagory)
        {
            List<Record> result = null;
            foreach(var key in records.Keys)
            {
                if(records[key].category == catagory)
                {
                    if (result == null) result = new List<Record>();

                    result.Add(records[key]);
                }
            }
            return result;
        }

        //新的条目  
        public Record NewRecord(string synbolName, RecordCatagory catagory, string typeExpr, SymbolTable envPtr = null, long addr = 9999, string initValue = default)
        {
            var newRec = new Record() {
                name = synbolName, 
                rawname = synbolName,
                category = catagory,
                addr = addr,
                initValue = initValue,
                typeExpression = typeExpr ,
                envPtr = envPtr,
            };
            records[synbolName] = newRec;

            return newRec;
        }

        //添加已有的条目  
        public void AddRecord(string key, Record rec)
        {
            this.records[key] = rec;
        }

        //获取子表  
        public SymbolTable GetTableInChildren(string name)
        {
            foreach(var child in children)
            {
                if(child.name == name)
                {
                    return child;
                }
            }
            throw new Exception(this.name + "找不到名为" + name + "的子表！");
        }

        private string GenGuid()
        {
            return System.Guid.NewGuid().ToString();
        }

        public void Print()
        {
            int pad = 16;
            GixConsole.LogLine();
            GixConsole.LogLine($"|{new string('-', pad)}-{new string('-', pad)}-{ this.name.PadRight(pad) + (this.parent != null ? ("(parent:" + this.parent.name + ")") : "") }-{new string('-', pad)}-{new string('-', pad)}|");
            GixConsole.LogLine($"|{"NAME".PadRight(pad)}|{"RAW".PadRight(pad)}|{"CATAGORY".PadRight(pad)}|{"TYPE".PadRight(pad)}|{"ADDR".PadRight(pad)}|{"SubTable".PadRight(pad)}|");
            GixConsole.LogLine($"|{new string('-', pad * 6 + 4)}|");
            foreach (var key in records.Keys)
            {
                var rec = records[key];
                GixConsole.LogLine($"|{rec.name.PadRight(pad)}|{rec.rawname.PadRight(pad)}|{rec.category.ToString().PadRight(pad)}|{rec.typeExpression.PadRight(pad)}|{rec.addr.ToString().PadRight(pad)}|{(rec.envPtr != null ? "hasSubTable" : "").PadRight(pad)}|");
            }
            GixConsole.LogLine($"|{new string('-', pad * 6 + 4)}|");
            GixConsole.LogLine();

            if(this.children .Count > 0)
            {
                foreach(var c in children)
                {
                    c.Print();
                }
            }
        }
    }


    /// <summary>
    /// 虚函数表  
    /// </summary>
    [Serializable]
    [DataContract(IsReference = true)]
    public class VTable
    {
        [Serializable]
        [DataContract]
        public class Record
        {
            [DataMember]
            public string funcName;
            [DataMember]
            public string className;
            [DataMember]
            public string funcfullname;
        }

        [DataMember]
        public string name;

        [DataMember]
        public Dictionary<string, Record> data;

        public VTable(string name)
        {
            this.name = name;
            this.data = new Dictionary<string, Record>();
        }

        public Record Query(string funcName)
        {
            return data[funcName];
        }

        public void NewRecord(string fname, string cname)
        {
            data[fname] = new Record() { funcName = fname, className = cname, funcfullname = cname + "." + fname };
        }

        public void CloneDataTo(VTable table)
        {
            foreach(var kv in this.data)
            {
                table.data[kv.Key] = new Record() { funcName = kv.Key, className = kv.Value.className, funcfullname = kv.Value.className + "." + kv.Key };
            }
        }
    }




    /// <summary>
    /// 文法符号  
    /// </summary>
    public abstract class Symbol
    {
        public string name;

        public TerminalSet cachedFIRST = null;
        public TerminalSet cachedFOLLOW = null;
    }

    /// <summary>
    /// 产生式  
    /// </summary>
    public class Production
    {
        public Nonterminal head;
        public Symbol[] body;

        public bool IsεProduction()
        {
            if (body.Length == 0)
                return true;
            else
                return false;
        }
        public bool CanDeriveε()
        {
            if (IsεProduction()) return true;

            foreach (var s in body)
            {
                if (s is Terminal) return false;
            }

            bool allCanDeriveε = true;
            foreach (var s in body)
            {
                if (s is Nonterminal && (s as Nonterminal).CanDeriveε() == false)
                {
                    allCanDeriveε = false;
                    break;
                }
            }
            if (allCanDeriveε)
            {
                return true;
            }

            return false;
        }

        public string ToExpression()
        {
            System.Text.StringBuilder strb = new System.Text.StringBuilder();

            strb.Append(head.name);
            strb.Append(" ->");
            if(this.IsεProduction())
            {
                strb.Append(" ");
                strb.Append("ε");
            }
            else
            {
                foreach(var s in this.body)
                {
                    strb.Append(" ");
                    strb.Append(s.name);
                }
            }

            return strb.ToString();
        }
    }


    /// <summary>
    /// 非终结符  
    /// </summary>
    public class Nonterminal : Symbol
    {
        public List<Production> productions = null;

        //有ε产生式  
        public bool HasεProduction()
        {
            foreach (var production in productions)
            {
                if (production.IsεProduction())
                {
                    return true;
                }
            }
            return false;
        }

        //能够推导出ε  (A =*> ε)
        public bool CanDeriveε()
        {
            foreach (var production in productions)
            {
                if (production.CanDeriveε())
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 终结符  
    /// </summary>
    public class Terminal : Symbol
    { }


    /// <summary>
    /// 基于依赖的集合  
    /// </summary>
    public class TerminalSet
    {
        public class UpperCollectionInfo
        {
            public TerminalSet upperCollection;//关系的上层集合  
            public List<Terminal> exceptedTerminals;//排除的终结符  
            public UpperCollectionInfo(TerminalSet collection, List<Terminal> exceptedTerminals)
            {
                this.upperCollection = collection;
                this.exceptedTerminals = exceptedTerminals;
            }
        }

        private List<Terminal> terminals = new List<Terminal>();

        public List<UpperCollectionInfo> upperCollectionInfos = new List<UpperCollectionInfo>();//被哪些集合依赖  

        public Terminal this[int i]
        {
            get { return terminals[i]; }
        }
        public void AddDistinct(Terminal terminal)
        {
            bool anyChange = false;
            if (this.terminals.Contains(terminal) == false)
            {
                this.terminals.Add(terminal);
                anyChange = true;
            }

            if (anyChange)
            {
                OnChange();
            }
        }
        public void UnionWith(TerminalSet collection, List<Terminal> exceptedTerminals = null)
        {
            if (collection == null) return;
            if (collection == this) return;//不能自我依赖    
            if (collection.upperCollectionInfos.Any(inf => inf.upperCollection == this)) return;//不能重复添加    

            //建立联系  
            collection.upperCollectionInfos.Add(new UpperCollectionInfo(this, exceptedTerminals));

            //添加依赖集合的符号  
            bool anyChange = false;
            foreach (var t in collection.ToArray())
            {
                if (terminals.Contains(t)) continue;
                if (exceptedTerminals != null && exceptedTerminals.Contains(t)) continue;

                terminals.Add(t);
                anyChange = true;
            }

            if (anyChange)
            {
                OnChange();
            }
        }

        private void OnLowerCollectionChange(TerminalSet lowerCollection, List<Terminal> exceptedTerminals)
        {
            bool anyChange = false;
            foreach (var t in lowerCollection.terminals)
            {
                if (terminals.Contains(t)) continue;
                if (exceptedTerminals != null && exceptedTerminals.Contains(t)) continue;


                terminals.Add(t);
                anyChange = true;
            }

            if (anyChange)
            {
                OnChange();
            }
        }

        private void OnChange()
        {
            foreach (var upperInfo in this.upperCollectionInfos)
            {
                upperInfo.upperCollection.OnLowerCollectionChange(this, upperInfo.exceptedTerminals);
            }
        }

        public bool Contains(Terminal terminal)
        {
            foreach (var t in terminals)
            {
                if (t == terminal)
                    return true;
            }
            return false;
        }

        public bool ContainsTerminal(string terminalname)
        {
            if (string.IsNullOrEmpty(terminalname))
            {
                return terminals.Contains(null);
            }


            foreach (var t in terminals)
            {
                if (t != null && t.name == terminalname)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Intersect(TerminalSet another)
        {
            foreach (var t in this.terminals)
            {
                if (another.terminals.Contains(t))
                {
                    return true;
                }
            }
            return false;
        }
        
        public Terminal[] ToArray()
        {
            return terminals.ToArray();
        }
    }



    public static class Debug
    {
        public static void Log(object content)
        {
            Gizbox.GixConsole.LogLine(content);
        }
    }
    public static class GixConsole
    {
        public static bool enableSystemConsole = true;

        public static void Log(object msg = null)
        {
            if(GixConsole.enableSystemConsole)
            {
                Console.Write(msg);
            }
        }
        public static void LogLine(object msg = null)
        {
            if (GixConsole.enableSystemConsole)
            {
                Console.WriteLine(msg);
            }
        }
        public static void Pause()
        {
            if (GixConsole.enableSystemConsole)
            {
                Console.ReadKey();
            }
        }
    }
}
