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
        /// 每行起始  
        /// </summary>
        public int start;

        /// <summary>
        /// 每行结束  
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
            public string name;
            [DataMember]
            public string rawname;
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
            foreach (var val in records.Values)
            {
                if (val.rawname == rawname) return true;
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
            foreach (var val in records.Values)
            {
                if (val.rawname == rawSymbolName) return val;
            }
            throw new Exception(this.name + "表中获取不到记录原名：" + rawSymbolName);
        }

        //在本符号表和基类符号表中查找  
        public Record GetMemberRecordInChain(string symbolName)
        {
            if (this.tableCatagory != TableCatagory.ClassScope) throw new Exception("进类的符号表支持基类查找");

            if (records.ContainsKey(symbolName))
            {
                GixConsole.LogLine("has symbol:" + symbolName + " in " + this.name);
                return records[symbolName];
            }
            else
            {
                if(records.ContainsKey("base") == true)
                {
                    return records["base"].envPtr.GetMemberRecordInChain(symbolName);
                }
                else
                {
                    return null;
                }
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





    /// <summary>
    /// 编译器  
    /// </summary>
    public class Compiler
    {
        //Settings  
        public static bool enableLogScanner = false;
        public static bool enableLogParser = false;
        public static bool enableLogSemanticAnalyzer = false;
        public static bool enableLogILGenerator = false;
        public static bool enableLogScriptEngine = false;

        //parser data  
        private bool parserDataHardcode = true;
        public string parserDataPath;

        //lib info  
        public Dictionary<string, Gizbox.IL.ILUnit> libsCache = new Dictionary<string, IL.ILUnit>();
        public List<string> libPathFindList = new List<string>();

        //CTOR  
        public Compiler()
        {
        }


        /// <summary>
        /// 例程：测试简单编译
        /// </summary>
        public SimpleParseTree TestSimpleCompile()
        {
            string source =
                @"var a = 233;
                  var b = 111;
                  var c = a + 999;
                 ";

            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);
            SimpleParser parser = new SimpleParser();
            parser.Parse(tokens);

            GixConsole.LogLine("\n\n语法分析树：");
            GixConsole.LogLine(parser.parseTree.Serialize());

            return parser.parseTree;
        }

        /// <summary>
        /// 例程：测试LL0  
        /// </summary>
        public SimpleParseTree TestLL0Compile()
        {
            string source =
                @"var a = 233;
                  var b = (a + 111) * 222;
                 ";

            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);
            LLParser parser = new LLParser();
            parser.Parse(tokens);


            GixConsole.LogLine("\n\n语法分析树：");
            GixConsole.LogLine(parser.parseTree.Serialize());

            return parser.parseTree;
        }

        /// <summary>
        /// 暂停  
        /// </summary>
        public static void Pause(string txt = "")
        {
            return;

            GixConsole.LogLine(txt + "\n按任意键继续...");
            GixConsole.Pause();
        }

        public void AddLib(string libname, Gizbox.IL.ILUnit lib)
        {
            this.libsCache[libname] = lib;
        }
        public void AddLibPath(string path)
        {
            if(System.IO.Directory.Exists(path))
                this.libPathFindList.Add(path);
            else
                throw new Exception("Lib dir not exist:" + path);
        }

        /// <summary>
        /// 载入或者编译库（语义分析用）  
        /// </summary>
        public Gizbox.IL.ILUnit LoadLib(string libname)
        {
            //编译器中查找  
            if (this.libsCache.ContainsKey(libname))
            {
                return this.libsCache[libname];
            }
            //路径查找  
            else
            {
                foreach (var dir in this.libPathFindList)
                {
                    if(System.IO.Directory.Exists(dir) == false) continue;

                    System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(dir);
                    foreach(var f in dirInfo.GetFiles())
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(f.Name) == libname)
                        {
                            if(System.IO.Path.GetExtension(f.Name).EndsWith("gixlib"))
                            {
                                var unit = Gizbox.IL.ILSerializer.Deserialize(f.FullName);

                                if (unit.name != libname)
                                {
                                    throw new GizboxException(ExceptioName.LibraryFileNameMismatch, libname  + " and " + unit.name);
                                }
                                else
                                {
                                    libsCache.Add(unit.name, unit);//cached
                                }
                                return unit;
                            }
                        }
                    }
                }
            }

            throw new GizboxException(ExceptioName.LibraryFileNotFound, libname) ;
        }


        /// <summary>
        /// 配置分析器来源方式  
        /// </summary>
        public void ConfigParserDataSource(bool hardcode)
        {
            this.parserDataHardcode = hardcode;
        }

        /// <summary>
        /// 配置分析器文件读取为止  
        /// </summary>
        public void ConfigParserDataPath(string path)
        {
            this.parserDataPath = path;
        }

        /// <summary>
        /// 硬编码保存到桌面      
        /// </summary>
        public void SaveParserHardcodeToDesktop()
        {
            Scanner scanner = new Scanner();

            LALRGenerator.ParserData data;
            var grammer = new Grammer() { terminalNames = scanner.GetTokenNames() };
            LALRGenerator.LALRGenerator generator = new Gizbox.LALRGenerator.LALRGenerator(grammer, this.parserDataPath);
            data = generator.GetResult();

            string codepath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "\\cs_parse_hardcode.cs";
            string csStr = Utility.ParserHardcoder.GenerateHardcode(data);
            System.IO.File.WriteAllText(codepath, csStr);
        }
        
        /// <summary>
        /// 硬编码插入到源代码文件中  
        /// </summary>
        public void InsertParserHardcodeToSourceFile(string hardcodeSourceFilePath)
        {
            Scanner scanner = new Scanner();

            LALRGenerator.ParserData data;
            var grammer = new Grammer() { terminalNames = scanner.GetTokenNames() };
            LALRGenerator.LALRGenerator generator = new Gizbox.LALRGenerator.LALRGenerator(grammer, this.parserDataPath);
            data = generator.GetResult();

            Utility.ParserHardcoder.GenerateHardcodeHere(hardcodeSourceFilePath, data);
        }



        /// <summary>
        /// 编译库  
        /// </summary>
        public void CompileToLib(string source, string libName, string savePath)
        {
            var ir = this.Compile(source);
            ir.name = libName;
            Gizbox.IL.ILSerializer.Serialize(savePath, ir);

            Gizbox.GixConsole.LogLine($"Lib {libName} Complete Finish!");
        }

        /// <summary>
        /// 编译  
        /// </summary>
        public IL.ILUnit Compile(string source)
        {
            if (string.IsNullOrEmpty(this.parserDataPath) && parserDataHardcode == false) throw new Exception("语法分析器数据源没有设置");

            IL.ILUnit ilUnit = new IL.ILUnit();

            //词法分析  
            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);


            //硬编码生成语法分析器    
            Gizbox.LALRGenerator.ParserData data;
            if (parserDataHardcode)
            {
                data = Gizbox.Utility.ParserHardcoder.GenerateParser();
            }
            //文件系统读取语法分析器    
            else
            {
                var grammer = new Grammer() { terminalNames = scanner.GetTokenNames() };
                LALRGenerator.LALRGenerator generator = new Gizbox.LALRGenerator.LALRGenerator(grammer, this.parserDataPath);
                data = generator.GetResult();
            }

            //语法分析  
            LRParse.LRParser parser = new LRParse.LRParser(data, this);
            parser.Parse(tokens);
            var syntaxTree = parser.syntaxTree;


            //语义分析  
            SemanticRule.SemanticAnalyzer semanticAnalyzer = new SemanticRule.SemanticAnalyzer(syntaxTree, ilUnit, this);
            semanticAnalyzer.Analysis();


            //中间代码生成    
            Gizbox.IL.ILGenerator ilGenerator = new IL.ILGenerator(syntaxTree, ilUnit);
            ilGenerator.Generate();


            return ilUnit;
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

    public static class Utils
    {
        public static string Mangle(string funcname, params string[] paramTypes)
        {
            string result = funcname + "@";
            foreach (var paramType in paramTypes)
            {
                result += '_';
                result += paramType;
            }
            return result;
        }

        public static bool IsPrimitiveType(string typeExpr)
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

        public static bool IsArrayType(string typeExpr)
        {
            return typeExpr.EndsWith("[]");
        }

        public static bool IsFunType(string typeExpr)
        {
            return typeExpr.Contains("->");
        }
    }
}
