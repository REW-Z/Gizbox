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
        public enum RecordFlag : ulong
        {
            None = 0,
            OperatorOverloadFunc = 1 << 0,
            ExternFunc = 1 << 1,
            Ctor = 1 << 2,

            ManualClass = 1 << 10,
            OwnershipClass = 1 << 11,
            ManualVar = 1 << 12,
            OwnerVar = 1 << 13,
            BorrowVar = 1 << 14,
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
            public int index;//索引-用于标识参数列表的顺序
            [DataMember]
            public string typeExpression;
            [DataMember]
            public long size;
            [DataMember]
            public long addr;
            [DataMember]
            public string initValue;
            [DataMember]
            public SymbolTable envPtr;
            [DataMember]
            public ulong flagsSerialized;

            public RecordFlag flags
            {
                get => (RecordFlag)flagsSerialized;
                set => flagsSerialized = (ulong)value;
            }

            public object runtimeAdditionalInfo;
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

        public Dictionary<RecordCatagory, int> countDict;





        //构造  
        public SymbolTable(string name, TableCatagory tableCatagory, SymbolTable parentTable = null)
        {
            this.name = name;
            this.tableCatagory = tableCatagory;
            this.records = new ();
            this.countDict = new();

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
        public void GetAllRecordByName(string symbolName, List<SymbolTable.Record> result)
        {
            foreach(var kv in records)
            {
                if(kv.Value.name == symbolName)
                    result.Add(kv.Value);
            }
        }
        public void GetAllRecordByFlag(RecordFlag flag, List<SymbolTable.Record> result)
        {
            foreach(var kv in records)
            {
                if((kv.Value.flags & flag) != 0)
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

        //获取某类型记录
        public int GetRecCount(RecordCatagory catagory)
        {
            int count = 0;
            foreach (var key in records.Keys)
            {
                if (records[key].category == catagory)
                {
                    count++;
                }
            }
            return count;
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
            AddRecord(synbolName, newRec);

            return newRec;
        }

        //添加已有的条目  
        public void AddRecord(string key, Record rec)
        {
            if(records.ContainsKey(key))
                throw new GizboxException(ExceptioName.Undefine, $"exist key:{key}");

            if(key != rec.name)
                throw new GizboxException(ExceptioName.Undefine, $"add record invalid key:{key}.");

            this.records[key] = rec;

            if(this.countDict.TryGetValue(rec.category, out var count))
            {
                rec.index = count;
                this.countDict[rec.category] = count + 1;
            }
            else
            {
                rec.index = 0;
                this.countDict[rec.category] = 1;
            }
        }

        public Record RemoveRecord(string key)
        {
            if(this.records.ContainsKey(key) == false)
                throw new GizboxException(ExceptioName.Undefine, $"key not exist:{key}.");

            var rec = records[key];
            this.records.Remove(key);

            this.countDict[rec.category] -= 1;

            return rec;

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

        public void RefreshDepth()
        {
            if(parent == null)
                depth = 0;

            foreach(var child in children)
            {
                child.depth = this.depth + 1;
                child.RefreshDepth();
            }
        }

        public void RemoveFromParent()
        {
            this.parent.children.Remove(this);
            this.parent = null;
        }
        public void AddChildren(SymbolTable env)
        {
            this.children.Add(env);
            env.parent = this;
            env.depth = this.depth + 1;
        }

        public IEnumerable<KeyValuePair<string, Record>> GetRecordsRecursive()
        {
            if(this.records != null)
            {
                foreach(var kv in this.records)
                {
                    yield return kv;
                }
            }

            if(this.children != null)
            {
                foreach(var child in this.children)
                {
                    if(child == null)
                        continue;

                    var seq = child.GetRecordsRecursive();
                    if(seq == null)
                        continue;

                    foreach(var rec in seq)
                    {
                        yield return rec;
                    }
                }
            }
        }

        public void Print()
        {
            int pad = 16;
            GixConsole.WriteLine();
            GixConsole.WriteLine($"|-{new string('-', pad)}-{new string('-', pad)}-{ this.name.PadRight(pad) + (this.parent != null ? ("(parent:" + this.parent.name + ")") : "").PadRight(pad) }-{new string('-', pad)}-{new string('-', pad)}-{new string('-', pad)}|");
            GixConsole.WriteLine($"|{"NAME".PadRight(pad)}|{"RAW".PadRight(pad)}|{"CATAGORY".PadRight(pad)}|{"TYPE".PadRight(pad)}|{"ADDR".PadRight(pad)}|{"Other".PadRight(pad)}|{"SubTable".PadRight(pad)}|");
            GixConsole.WriteLine($"|{new string('-', pad * 7 + 6)}|");
            foreach (var key in records.Keys)
            {
                var rec = records[key];

                string otherinfo = string.Empty;
                if(string.IsNullOrEmpty(rec.initValue) == false)
                {
                    otherinfo = rec.initValue.Trim();
                }

                GixConsole.WriteLine($"|{rec.name.PadRight(pad)}|{rec.rawname.PadRight(pad)}|{rec.category.ToString().PadRight(pad)}|{rec.typeExpression.PadRight(pad)}|{rec.addr.ToString().PadRight(pad)}|{otherinfo.PadRight(pad)}|{(rec.envPtr != null ? rec.envPtr.name : "").PadRight(pad)}|");
            }
            GixConsole.WriteLine($"|{new string('-', pad * 7 + 6)}|");
            GixConsole.WriteLine();

            if(this.children .Count > 0)
            {
                foreach(var c in children)
                {
                    c.Print();
                }
            }
        }
    }

    public class GType
    {
        public static Dictionary<string, GType> typeExpressionCache = new();
        public enum Kind
        {
            Other,
            Void,
            Int,
            Long,
            Float,
            Double,
            Bool,
            Char,
            String,
            Object, //引用类型
            Array, //数组类型
            Function, //函数类型
        }


        public static GType Parse(string typeExpression)
        {
            typeExpression = typeExpression.Trim();

            if(typeExpressionCache.TryGetValue(typeExpression, out var cached))
                return cached;

            GType type = new GType();

            if(typeExpression.Contains("=>"))
            {
                type._Kind = Kind.Function;

                string paramPart = null;
                string returnPart = null;
                for(int i = 0; i < typeExpression.Length - 1; ++i)
                {
                    if(typeExpression[i] == '=' && typeExpression[i + 1] == '>')
                    {
                        type._Function_ParamTypes = new List<GType>();
                        paramPart = typeExpression.Substring(0, i).Trim();
                        returnPart = typeExpression.Substring(i + 2).Trim();
                        break;
                    }
                }
                if(paramPart == null || returnPart == null)
                    throw new GizboxException(ExceptioName.Undefine, "param or return type expression invalid.");

                var paramStrArr = paramPart.Split(',');
                type._Function_ParamTypes = paramStrArr
                    .Select(p => Parse(p.Trim()))
                    .ToList();
                type._Function_ReturnType = Parse(returnPart.Trim());
            }
            else if(typeExpression.EndsWith("[]"))
            {
                type._Kind = Kind.Array;
                var typeEle = typeExpression.Substring(0, typeExpression.Length - 2).Trim();
                type._Array_ElementType = Parse(typeEle);
            }
            else if(typeExpression.StartsWith("(") && typeExpression.EndsWith(")"))
            {
                type._Kind = Kind.Other;
            }
            else
            {
                switch(typeExpression)
                {
                    case "bool":
                        type._Kind = Kind.Bool;
                        break;
                    case "char":
                        type._Kind = Kind.Char;
                        break;
                    case "int":
                        type._Kind = Kind.Int;
                        break;
                    case "long":
                        type._Kind = Kind.Long;
                        break;
                    case "float":
                        type._Kind = Kind.Float;
                        break;
                    case "double":
                        type._Kind = Kind.Double;
                        break;
                    case "string":
                        type._Kind = Kind.String;
                        break;
                    default:
                        type._Kind = Kind.Object;
                        break;
                }
            }

            type._RawTypeExpression = typeExpression;
            typeExpressionCache[typeExpression] = type;
            return type;
        }

        public static GType GenFuncType(GType returnType, params GType[] paramTypes)
        {
            return GType.Parse($"{string.Join(",", paramTypes.Select(p => p.ToString()))} => {returnType.ToString()}");
        }

        private string _RawTypeExpression;
        private Kind _Kind;
        private GType _Array_ElementType;
        private GType _Function_ReturnType;
        private List<GType> _Function_ParamTypes;

        private GType() { }

        public override string ToString()
        {
            return _RawTypeExpression;
        }

        public Kind Category => _Kind;

        public int Size
        {
            get
            {
                return _Kind switch
                {
                    Kind.Void => 0,
                    Kind.Int => 4,
                    Kind.Long => 8,
                    Kind.Float => 4,
                    Kind.Double => 8,
                    Kind.Bool => 1,
                    Kind.Char => 2,
                    Kind.String => 8,
                    Kind.Object => 8,
                    Kind.Array => 8,
                    Kind.Function => 8,
                    _ => throw new GizboxException(ExceptioName.Undefine, $"unknown type expression category: {_Kind}"),
                };
            }
        }

        public int Align
        {
            get
            {
                return _Kind switch
                {
                    Kind.Void => 0,
                    Kind.Int => 4,
                    Kind.Long => 8,
                    Kind.Float => 4,
                    Kind.Double => 8,
                    Kind.Bool => 1,
                    Kind.Char => 2,
                    Kind.String => 8,
                    Kind.Object => 8,
                    Kind.Array => 8,
                    Kind.Function => 8,
                    _ => throw new GizboxException(ExceptioName.Undefine, $"unknown type expression category: {_Kind}"),
                };
            }
        }

        public bool IsPrimitive
        {
            get
            {
                return _Kind switch
                {
                    Kind.Int => true,
                    Kind.Long => true,
                    Kind.Float => true,
                    Kind.Double => true,
                    Kind.Bool => true,
                    Kind.Char => true,
                    _ => false,
                };
            }
        }
        public bool IsNumberType
        {
            get
            {
                return _Kind switch
                {
                    Kind.Int => true,
                    Kind.Long => true,
                    Kind.Float => true,
                    Kind.Double => true,
                    _ => false,
                };
            }
        }


        public bool IsReferenceType
        {
            get
            {
                return _Kind == Kind.String ||
                    _Kind == Kind.Object || 
                    _Kind == Kind.Array || 
                    _Kind == Kind.Function;
            }
        }

        public bool IsArray
        {
            get => _Kind == Kind.Array;
        }

        public bool IsClassType
        {
            get
            {
                return _Kind == Kind.Object;
            }
        }

        public bool IsInteger
        {
            get
            {
                return _Kind == Kind.Int || _Kind == Kind.Long;
            }
        }

        public bool IsSigned
        {
            get
            {
                return _Kind == Kind.Int || _Kind == Kind.Long || _Kind == Kind.Float || _Kind == Kind.Double;
            }
        }

        public GType BoxType
        {
            get
            {
                switch(_Kind)
                {
                    case Kind.Bool:
                        return Parse("Core::Bool");
                    case Kind.Char:
                        return Parse("Core::Char");
                    case Kind.Int:
                        return Parse("Core::Int");
                    case Kind.Long:
                        return Parse("Core::Long");
                    case Kind.Float:
                        return Parse("Core::Float");
                    case Kind.Double:
                        return Parse("Core::Double");
                    default:
                        return this;
                }
            }
        }

        public string ExternConvertStringFunction
        {
            get
            {
                switch(_Kind)
                {
                    case Kind.Bool:
                        return $"Core::Extern::BoolToString";
                    case Kind.Char:
                        return $"Core::Extern::CharToString";
                    case Kind.Int:
                        return $"Core::Extern::IntToString";
                    case Kind.Long:
                        return $"Core::Extern::LongToString";
                    case Kind.Float:
                        return $"Core::Extern::FloatToString";
                    case Kind.Double:
                        return $"Core::Extern::DoubleToString";
                    default:
                        throw new GizboxException(ExceptioName.Undefine, $"No convert function:{this._RawTypeExpression}");
                }
            }
        }

        public GType FunctionReturnType
        {
            get
            {
                if(this.Category != Kind.Function) 
                    throw new GizboxException(ExceptioName.Undefine, $"cannot get return type of non-function type: {_Kind}");
                return _Function_ReturnType;
            }
        }

        public List<GType> FunctionParamTypes => _Function_ParamTypes;

        public bool IsSSE
        {
            get
            {
                return _Kind == Kind.Float || _Kind == Kind.Double;
            }
        }

        public GType ArrayElementType
        {
            get
            {
                if(_Kind != Kind.Array)
                    throw new GizboxException(ExceptioName.Undefine, $"cannot get element type of non-array type: {_Kind}");

                return _Array_ElementType;
            }
        }
    }



    /// <summary>
    /// 虚函数表  
    /// </summary>
    [Serializable]
    [DataContract(IsReference = true)]
    public class VTable : IEnumerable<VTable.Record>
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
        public List<Record> data;

        [DataMember]
        public Dictionary<string, int> dataIndexDict;

        public VTable(string name)
        {
            this.name = name;
            this.data = new List<Record>();
            this.dataIndexDict = new Dictionary<string, int>();
        }

        public (int, Record) Query(string funcName)
        {
            if(dataIndexDict.TryGetValue(funcName, out var index))
            {
                return (index, data[index]);
            }
            return default;
        }

        public void NewRecord(string fname, string cname)
        {
            if(dataIndexDict.TryGetValue(fname, out var index))
            {
                data[index].className = cname;
                data[index].funcfullname = $"{cname}.{fname}";
            }
            else
            {
                var newfuncRec = new Record() { funcName = fname, className = cname, funcfullname = $"{cname}.{fname}" };

                data.Add(newfuncRec);
                int newindex = data.Count - 1;
                dataIndexDict[fname] = newindex;
            }
        }

        public void CloneDataTo(VTable targetTable)
        {
            foreach(var rec in this.data)
            {
                var newrec = new Record() { funcName = rec.funcName, className = rec.className, funcfullname = rec.className + "." + rec.funcName };
                targetTable.data.Add(newrec);
                int index = targetTable.data.Count - 1;
                targetTable.dataIndexDict[rec.funcName] = index;
            }
        }

        public IEnumerator<Record> GetEnumerator()
        {
            return data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return data.GetEnumerator();
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


        public Production(GrammerSet container, Nonterminal head, Symbol[] body)
        {
            this.head = head;
            this.body = body;

            container.AddProduction(this);
        }

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


        public Nonterminal(GrammerSet container, string name) 
        {
            this.name = name;
            this.productions = new ();

            container.AddNonterminal(this);
        }

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
    {
        public Terminal(GrammerSet container, string name) 
        {
            this.name = name;

            container.AddTerminal(this);
        }
    }

    public class GrammerSet
    {
        public Dictionary<string, Symbol> symbolDict = new();
        public Dictionary<string, Terminal> terminalDict = new();
        public Dictionary<string, Nonterminal> nonterminalDict = new();

        public List<Production> productions = new();//索引代表编号
        public Dictionary<string, Production> productionDict = new();
        public Dictionary<Production, int> productionIdDict = new();

        public void AddTerminal(Terminal terminal)
        {
            symbolDict[terminal.name] = terminal;
            terminalDict[terminal.name] = terminal;
        }
        public void AddNonterminal(Nonterminal nonterminal)
        {
            symbolDict[nonterminal.name] = nonterminal;
            nonterminalDict[nonterminal.name] = nonterminal;
        }
        public void AddProduction(Production production)
        {
            productions.Add(production);

            //head.productions添加  
            production.head.productions.Add(production);

            //dict修改
            productionDict[production.ToExpression()] = production;
            productionIdDict[production] = productions.Count - 1;
        }
    }

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
            Gizbox.GixConsole.WriteLine(content);
        }
        public static void Assert(bool expr)
        {
            if(expr == false)
                throw new GizboxException(ExceptioName.Undefine, "assert false.");
        }
    }
    public static class GixConsole//LSP使用时需要disable  
    {
        public static bool enableSystemConsole = true;
        
        public static void Write(object msg = null)
        {
            if(GixConsole.enableSystemConsole)
            {
                Console.Write(msg);
            }
        }
        public static void WriteLine(object msg = null)
        {
            if (GixConsole.enableSystemConsole)
            {
                Console.Write(msg + "\n");
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
