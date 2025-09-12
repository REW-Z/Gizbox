using System;
using System.Collections.Generic;
using System.Text;
using Gizbox.IR;

namespace Gizbox
{
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
        public static bool enableLogCodeGen = true;

        //parser data  
        private bool parserDataHardcode = true;
        public string parserDataPath;

        //lib info  
        public Dictionary<string, ILUnit> libsCache = new Dictionary<string, ILUnit>();
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

            GixConsole.WriteLine("\n\n语法分析树：");
            GixConsole.WriteLine(parser.parseTree.Serialize());

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


            GixConsole.WriteLine("\n\n语法分析树：");
            GixConsole.WriteLine(parser.parseTree.Serialize());

            return parser.parseTree;
        }

        /// <summary>
        /// 暂停  
        /// </summary>
        public static void Pause(string txt = "")
        {
            return;

            GixConsole.WriteLine(txt + "\n按任意键继续...");
            GixConsole.Pause();
        }

        public void AddLib(string libname, ILUnit lib)
        {
            this.libsCache[libname] = lib;
        }
        public void AddLibPath(string path)
        {
            if (System.IO.Directory.Exists(path))
                this.libPathFindList.Add(path);
            else
                throw new Exception("Lib dir not exist:" + path);
        }

        /// <summary>
        /// 载入或者编译库（语义分析用）  
        /// </summary>
        public ILUnit LoadLib(string libname)
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
                    if (System.IO.Directory.Exists(dir) == false) continue;

                    System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(dir);
                    foreach (var f in dirInfo.GetFiles())
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(f.Name) == libname)
                        {
                            if (System.IO.Path.GetExtension(f.Name).EndsWith("gixlib"))
                            {
                                var unit = Gizbox.IR.ILSerializer.Deserialize(f.FullName);

                                if (unit.name != libname)
                                {
                                    throw new GizboxException(ExceptioName.LibraryFileNameMismatch, libname + " and " + unit.name);
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

            throw new GizboxException(ExceptioName.LibraryFileNotFound, libname);
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
            Gizbox.IR.ILSerializer.Serialize(savePath, ir);

            Gizbox.GixConsole.WriteLine($"Lib {libName} Complete Finish!");
        }

        /// <summary>
        /// 编译  
        /// </summary>
        public ILUnit Compile(string source)
        {
            if (string.IsNullOrEmpty(this.parserDataPath) && parserDataHardcode == false) throw new Exception("语法分析器数据源没有设置");

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
            ILUnit ir = new ILUnit();
            SemanticRule.SemanticAnalyzer semanticAnalyzer = new SemanticRule.SemanticAnalyzer(syntaxTree, ir, this);
            semanticAnalyzer.Analysis();

            //中间代码生成    
            ILGenerator ilGenerator = new IR.ILGenerator(syntaxTree, ir);
            var ilUnit = ilGenerator.Generate();


            return ilUnit;
        }
    }

}
