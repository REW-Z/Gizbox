using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
        public Dictionary<string, IRUnit> libsCache = new Dictionary<string, IRUnit>();
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

        public void AddLib(string libname, IRUnit lib)
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
        public IRUnit LoadLib(string libname)
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


        public static int Run(string fileName, string args, string workingDir = "")
        {
            // 工作目录
            string workDir = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir;
            try
            {
                if(string.IsNullOrEmpty(workDir) || System.IO.Directory.Exists(workDir) == false)
                    workDir = Environment.CurrentDirectory;
            }
            catch
            {
                workDir = Environment.CurrentDirectory;
            }

            string resolved = null;

            // 查找  
            string pathEnv = Environment.GetEnvironmentVariable("Path") ?? "";
            string pathEnv2 = Environment.GetEnvironmentVariable("PATH") ?? "";
            string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
            var paths = pathEnv.Split(System.IO.Path.PathSeparator);
            var paths2 = pathEnv2.Split(System.IO.Path.PathSeparator);
            List<string> allpaths = new();
            allpaths.Add(workDir);
            allpaths.AddRange(paths);
            allpaths.AddRange(paths2);
            var exts = pathext.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach(var dir in allpaths)
            {
                if(string.IsNullOrWhiteSpace(dir))
                    continue;
                try
                {
                    var cand0 = System.IO.Path.Combine(dir, fileName);
                    if(System.IO.File.Exists(cand0))
                    {
                        resolved = cand0;
                        break;
                    }

                    foreach(var ext in exts)
                    {
                        var extClean = ext.Trim();
                        var cand = cand0;
                        if(System.IO.Path.HasExtension(cand) == false)
                            cand = cand0 + extClean;

                        if(System.IO.File.Exists(cand))
                        {
                            resolved = cand;
                            break;
                        }
                    }
                    if(resolved != null)
                        break;
                }
                catch { /* ignore directories we cannot access */ }
            }

            if(resolved == null)
                resolved = fileName;

            var psi = new ProcessStartInfo
            {
                FileName = resolved,
                Arguments = args,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using(var proc = new Process { StartInfo = psi })
            {
                proc.OutputDataReceived += (s, e) => { if(e != null && e.Data != null) GixConsole.WriteLine("[OUT] " + e.Data); };
                proc.ErrorDataReceived += (s, e) => { if(e != null && e.Data != null) GixConsole.WriteLine("[ERR] " + e.Data); };

                try
                {
                    proc.Start();
                }
                catch(Exception ex)
                {
                    string extra = string.Equals(resolved, fileName, StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : $" (resolved '{fileName}' -> '{resolved}')";
                    string msg = $"无法启动进程 '{fileName}'{extra}. WorkingDirectory='{workDir}'. 原始错误: {ex.Message}";
                    throw new System.InvalidOperationException(msg, ex);
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }


        /// <summary>
        /// 编译库  
        /// </summary>
        public void CompileToLib(string source, string libName, string savePath)
        {
            var ir = this.CompileToIR(source, isMainUnit:false, libName);
            Gizbox.IR.ILSerializer.Serialize(savePath, ir);

            Gizbox.GixConsole.WriteLine($"Lib {libName} Complete Finish!");
        }

        /// <summary>
        /// 编译为IR  
        /// </summary>
        public IRUnit CompileToIR(string source, bool isMainUnit, string name)
        {
            if (string.IsNullOrEmpty(this.parserDataPath) && parserDataHardcode == false) throw new Exception("语法分析器数据源没有设置");
            if(name == null)
                throw new Exception("ir name cant be empty");

            //词法分析  
            Scanner scanner = new Scanner();
            scanner.SetTypeNames(CollectTypeNames(source));
            List<Token> tokens = scanner.Scan(source);


            //硬编码生成语法分析器    
            Gizbox.LALRGenerator.ParserData data;
            if (parserDataHardcode)
            {
                string dataPath = this.parserDataPath;
                if (string.IsNullOrEmpty(dataPath))
                {
                    var candidates = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parser_data.txt"),
                        Path.Combine(Environment.CurrentDirectory, "parser_data.txt"),
                    };
                    dataPath = candidates.FirstOrDefault(File.Exists);
                }

                if (!string.IsNullOrEmpty(dataPath) && File.Exists(dataPath))
                {
                    var grammer = new Grammer() { terminalNames = scanner.GetTokenNames() };
                    LALRGenerator.LALRGenerator generator = new Gizbox.LALRGenerator.LALRGenerator(grammer, dataPath);
                    data = generator.GetResult();
                }
                else
                {
                    data = Gizbox.Utility.ParserHardcoder.GenerateParser();
                }
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
            IRUnit ir = new IRUnit();
            ir.name = name;
            SemanticRule.SemanticAnalyzer semanticAnalyzer = new SemanticRule.SemanticAnalyzer(syntaxTree, ir, this);
            semanticAnalyzer.Analysis();

            //中间代码生成    
            IRGenerator irGenerator = new IR.IRGenerator(syntaxTree, ir, isMainUnit);
            var irUnit = irGenerator.Generate();


            return irUnit;
        }

        private HashSet<string> CollectTypeNames(string source)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in CollectTypeNamesFromSource(source))
            {
                AddTypeName(result, name);
            }

            foreach (var libName in CollectImportNames(source))
            {
                var lib = LoadLib(libName);
                lib.AutoLoadDependencies(this);
                CollectTypeNamesFromLib(lib, result);
            }

            return result;
        }

        private IEnumerable<string> CollectTypeNamesFromSource(string source)
        {
            if (string.IsNullOrEmpty(source))
                yield break;

            var classRegex = new Regex(@"\bclass\s+(?:own\s+)?([A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*)");
            foreach (Match match in classRegex.Matches(source))
            {
                if (match.Success)
                    yield return match.Groups[1].Value;
            }

            var classGenericRegex = new Regex(@"\bclass\s+(?:own\s+)?[A-Za-z_][A-Za-z0-9_]*\s*<([^>]+)>");
            foreach (Match match in classGenericRegex.Matches(source))
            {
                if (!match.Success)
                    continue;

                foreach (var name in SplitGenericParams(match.Groups[1].Value))
                {
                    yield return name;
                }
            }

            var funcGenericRegex = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\s*<([^>]+)>\s*\(");
            foreach (Match match in funcGenericRegex.Matches(source))
            {
                if (!match.Success)
                    continue;

                foreach (var name in SplitGenericParams(match.Groups[1].Value))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<string> SplitGenericParams(string value)
        {
            return value.Split(',')
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v));
        }

        private static IEnumerable<string> CollectImportNames(string source)
        {
            if (string.IsNullOrEmpty(source))
                yield break;

            var importRegex = new Regex(@"\bimport\s*<\s*""([^""]+)""\s*>");
            foreach (Match match in importRegex.Matches(source))
            {
                if (match.Success)
                    yield return match.Groups[1].Value;
            }
        }

        private void CollectTypeNamesFromLib(IRUnit lib, HashSet<string> result)
        {
            if (lib == null)
                return;

            foreach (var record in lib.globalScope.env.records.Values)
            {
                if (record.category != SymbolTable.RecordCatagory.Class)
                    continue;

                var name = string.IsNullOrWhiteSpace(record.rawname) ? record.name : record.rawname;
                AddTypeName(result, name);
                AddTypeName(result, record.name);
            }

            if (lib.dependencyLibs == null)
                return;

            foreach (var dep in lib.dependencyLibs)
            {
                CollectTypeNamesFromLib(dep, result);
            }
        }

        private static void AddTypeName(HashSet<string> result, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            result.Add(name);
            var idx = name.LastIndexOf("::", StringComparison.Ordinal);
            if (idx >= 0 && idx + 2 < name.Length)
            {
                result.Add(name.Substring(idx + 2));
            }
        }


        /// <summary>
        /// 编译为NASM汇编  
        /// </summary>
        public void CompileIRToAsm(IRUnit ir, string outputDir, CompileOptions options = null)
        {
            if(options == null)
                options = new CompileOptions();
            var allfiles = Gizbox.Src.Backend.Win64Target.GenAsms(this, ir, outputDir, options);
        }


        /// <summary>
        /// 编译为可执行文件  
        /// </summary>
        public void CompileIRToExe(IRUnit ir, string outputDir, CompileOptions options = null)
        {
            if(options == null)
                options = new CompileOptions();
            var allfiles = Gizbox.Src.Backend.Win64Target.GenAsms(this, ir, outputDir, options);

            List<string> fileNames = new();

            foreach(var fileFullName in allfiles)
            {
                fileNames.Add(System.IO.Path.GetFileNameWithoutExtension(fileFullName));
            }

            //编译核心C运行库    
            string mingw_gcc = "x86_64-w64-mingw32-gcc";

#if DEBUG
            //测试：使用把corert.c编译为win64的asm
            Run(mingw_gcc, $"-S -m64 -masm=intel -o corert.s corert.c", AppDomain.CurrentDomain.BaseDirectory);
#endif

            Run(mingw_gcc, $"-c corert.c -o corert.obj", AppDomain.CurrentDomain.BaseDirectory);

            //复制corert.obj到outputDir
            var srcPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "corert.obj");
            if(System.IO.File.Exists(srcPath) == false)
                throw new GizboxException(ExceptioName.Link, "obj not exist:" + srcPath);

            var dstPath = System.IO.Path.Combine(outputDir, "corert.obj");
            if(System.IO.File.Exists(dstPath))
                System.IO.File.Delete(dstPath);


            System.IO.File.Copy(srcPath, dstPath);

            foreach(var fileName in fileNames)
            {
                GixConsole.WriteLine($">>>nasm -f win64 {fileName}.asm -o {fileName}.obj");
                Run("nasm", $"-f win64 {fileName}.asm -o {fileName}.obj", outputDir);
            }

            var ret = Run(mingw_gcc, $"corert.obj {string.Join(" ", fileNames.Select(f => $"{f}.obj"))} -o {ir.name}.exe", outputDir);

            GixConsole.WriteLine($"编译完成...结束码:{ret}");
        }
    }



    //Compile Options  
    public enum BuildMode
    {
        Debug,
        Release,
    }
    public enum Platform
    {
        Windows_X64,
    }

    public class CompileOptions
    {
        public BuildMode buildMode = BuildMode.Debug;
        public Platform platform = Platform.Windows_X64;
    }
}
