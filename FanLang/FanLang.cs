using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;



namespace FanLang
{
    /// <summary>
    /// 模式  
    /// </summary>
    public enum PatternType
    {
        Keyword,
        Operator,
        Id,
        Number,
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
        /// 属性值（一般是词素或者指针）    
        /// </summary>
        public string attribute;


        public Token(string name, PatternType type, string attribute = null)
        {
            this.name = name;
            this.patternType = type;
            this.attribute = attribute;
        }
        public override string ToString()
        {
            return "<" + name + (string.IsNullOrEmpty(attribute) ? "" : ("," + attribute)) + ">";
        }
    }


    /// <summary>
    /// 模式  
    /// </summary>
    public class TokenPattern
    {
        public string tokenName;  

        public string regularExpression;

        public int back;

        public TokenPattern(string tokenName, string regularExpr, int back = 0)
        {
            this.tokenName = tokenName;
            this.regularExpression = regularExpr;
            this.back = back;
        }
    }


    /// <summary>
    /// 符号表  
    /// </summary>
    public class SymbolTable
    {
        public enum RecordCatagory
        {
            VariableOrParam,
            Function,
            Class
        }
        public class Record
        {
            public string name;
            public RecordCatagory category;
            public string typeExpression;
            public int addr;
        }

        public string name;

        public SymbolTable parent;

        public Dictionary<string, Record> records;

        //构造  
        public SymbolTable(string name, SymbolTable table = null)
        {
            this.name = name;
            this.parent = table;
            this.records = new Dictionary<string, Record>();
        }


        //查询信息  
        public Record GetRecord(string symbolName)
        {
            return records[symbolName];
        }

        //包含信息  
        public bool ContainRecordName(string name)
        {
            return records.ContainsKey(name);
        }

        //获取某类型记录  
        public List<Record> FindByCategory(RecordCatagory catagory)
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

        //记录符号  
        public Record NewRecord(string synbolName, RecordCatagory catagory, string typeExpr)
        {
            int variableAddr = 99999;//TODO:地址存放  
            var newRec = (new Record() { name = synbolName, category = catagory, addr = variableAddr, typeExpression = typeExpr });
            records[synbolName] = newRec;
            return newRec;
        }

        private string GenGuid()
        {
            return System.Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// 常量池  
    /// </summary>
    public class ConstantValueTable
    {
        public List<string> table = new List<string>();

        public int AddConstantValue(string val)
        {
            table.Add(val);
            return table.Count - 1;
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
        //Symbol Tables (Env)  
        public SymbolTable globalSymbolTable;

        //Constant Value Pool  
        public ConstantValueTable constantValueTable;

        //AST  
        public SyntaxTree syntaxTree;

        //CTOR  
        public Compiler()
        {
            //全局符号表    
            globalSymbolTable = new SymbolTable("global");

            //全局常量池  
            constantValueTable = new ConstantValueTable();
        }


        /// <summary>
        /// 测试简单编译
        /// </summary>
        public SimpleParseTree TestSimpleCompile(string source)
        {
            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);
            SimpleParser parser = new SimpleParser();
            parser.Parse(tokens);
            return parser.parseTree;
        }

        /// <summary>
        /// 测试LL0  
        /// </summary>
        public SimpleParseTree TestLL0Compile(string source)
        {
            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);
            LLParser parser = new LLParser();
            parser.Parse(tokens);
            return parser.parseTree;
        }

        /// <summary>
        /// 暂停  
        /// </summary>
        public static void Pause(string txt = "")
        {
            Console.WriteLine(txt + "\n按任意键继续...");
            //Console.ReadKey();
        }

        /// <summary>
        /// 编译  
        /// </summary>
        public void Compile(string source)
        {
            //词法分析  
            Scanner scanner = new Scanner();
            List<Token> tokens = scanner.Scan(source);

            //生成语法分析器    
            var grammer = new Grammer() { terminalNames = scanner.GetTokenNames() };
            LALRGenerator.LALRGenerator generator = new FanLang.LALRGenerator.LALRGenerator(grammer);
            var data = generator.GetResult();

            //语法分析  
            LRParse.LRParser parser = new LRParse.LRParser(data, this);
            parser.Parse(tokens);

            Console.WriteLine("\n\n语法分析树：");
            Console.WriteLine(parser.parseTree.Serialize());

            Console.WriteLine("\n\n抽象语法树：");
            Console.WriteLine(parser.syntaxTree.Serialize());
            this.syntaxTree = parser.syntaxTree;
            Compiler.Pause("抽象语法树生成完成");


            //语义分析  
            SemanticRule.SemanticAnalyzer semanticAnalyzer = new SemanticRule.SemanticAnalyzer(this.syntaxTree, this);
            semanticAnalyzer.Analysis();


            //中间代码生成    
            FanLang.IL.ILGenerator ilGenerator = new IL.ILGenerator(this.syntaxTree, this);
            ilGenerator.Generate();
            ilGenerator.PrintCodes();


            Compiler.Pause("中间代码生成完毕");
        }
    }

}
