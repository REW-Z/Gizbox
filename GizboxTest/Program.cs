
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gizbox;
using GizboxTest;
using Gizbox.Src.Backend;



////测试

string[] cmds = {
    "0.生成互操作代码",
    "1.生成库文件",
    "2.生成分析器硬编码",
    "3.测试杂项",
    "4.测试x64目标代码生成",
    };

Console.WindowWidth = 150;
Console.WindowHeight = 30;

Console.ForegroundColor = ConsoleColor.Gray;
Console.WriteLine($"******************************************************");
Console.WriteLine($"******************* Gizbox测试程序 *******************");
Console.WriteLine($"******************************************************");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("输入要进行的测试：");
for(int i = 0; i < cmds.Length ; i++)
{
    Console.WriteLine(cmds[i]);
}
int cmdIdx = -1;
while(cmdIdx < 0)
{
    string cmdstr = Console.ReadLine() ?? "";
    int num;
    bool valid = int.TryParse(cmdstr, out num);
    if(valid)
        cmdIdx = num;
}
Console.ForegroundColor = ConsoleColor.White;



switch(cmdIdx)
{
    case 0:
        {
            //测试生成互操作Wrap代码      
            Console.WriteLine("测试生成互操作Wrap代码");
            InteropWrapGenerator generator = new InteropWrapGenerator();
            generator.IncludeTypes(new Type[] {
                //typeof(TestClass),
                });
            foreach(var t in generator.closure)
            {
                Console.WriteLine(t.Name);
            }
            string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            generator.StartGenerate("Example", desktop, desktop);
            Console.ReadLine();
            return;
        } 
        break;
        case 1:
        {
            //生成库文件  
            Console.WriteLine("生成库文件");
            string libsrcCore = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\core.gix");
            string libsrcStd = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\stdlib.gix");
            Compiler libCompiler = new Compiler();
            libCompiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            libCompiler.ConfigParserDataSource(hardcode: true);
            //libCompiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            libCompiler.CompileToLib(libsrcCore, "core", AppDomain.CurrentDomain.BaseDirectory + "\\core.gixlib");
            libCompiler.CompileToLib(libsrcStd, "stdlib", AppDomain.CurrentDomain.BaseDirectory + "\\stdlib.gixlib");
            return;
        }
        break;
        case 2:
        {
            //生成分析器硬编码  
            Console.WriteLine("生成分析器硬编码");
            Gizbox.Compiler compilerTest = new Compiler();
            compilerTest.ConfigParserDataSource(hardcode: false);
            compilerTest.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            compilerTest.InsertParserHardcodeToSourceFile(Path.Combine(Utility.CoreProjPath, "Src\\Parser\\ParserHardcoder.cs"));
            Console.WriteLine("生成硬编码完成");
            return;
        }
        break;
        case 3:
        {
            //测试脚本Test  
            Console.WriteLine("测试杂项");


            break;  
        }
        break;
        case 4:
        {
            Console.WriteLine("测试x64目标代码生成");

            var projpath = Path.Combine(Utility.RepoPath, "GizboxTest");
            DirectoryInfo projDir = new DirectoryInfo(projpath);
            var gixFiles = projDir.GetFiles("*.gix");


            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.BackgroundColor = ConsoleColor.Yellow;
            for(int i = 0; i < gixFiles.Length; i++)
            {
                Console.WriteLine($"{i} : {gixFiles[i].Name}");
            }
            Console.ResetColor();

            var key = Console.ReadKey();
            if(int.TryParse(key.KeyChar.ToString(), out int idx))
            {
                var path = gixFiles[idx].FullName;

                //测试脚本Test  
                string source = System.IO.File.ReadAllText(path);
                Gizbox.Compiler compiler = new Compiler();
                compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
                compiler.ConfigParserDataSource(hardcode: true);
                //compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
                var il = compiler.CompileToIR(source, isMainUnit: true, "test");

                il.Print();

                compiler.CompileIRToExe(il, System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            }
            else
            {
                Console.WriteLine("无效数字");
            }
        }
        break;
}

Console.WriteLine("测试结束");
Console.ReadKey();


namespace GizboxTest
{
    public static class Utility
    {
        private static string repoPath = null;

        public static string RepoPath
        {
            get
            {
                if(repoPath == null)
                {
                    DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                    while(dir != null)
                    {
                        if(dir.Name == "Gizbox")
                        {
                            break;
                        }
                        dir = dir.Parent;
                    }
                    repoPath = dir.FullName;
                }
                return repoPath;
            }
        }

        private static string coreProjPath = null;
        public static string CoreProjPath
        {
            get
            {
                if(coreProjPath == null)
                {
                    DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                    while(dir != null)
                    {
                        if(dir.Name == "Gizbox")
                        {
                            break;
                        }
                        dir = dir.Parent;
                    }

                    var subDirs = dir.GetDirectories();
                    var targetDir = subDirs.FirstOrDefault(d => d.Name == "Gizbox");

                    coreProjPath = targetDir.FullName;
                }

                return coreProjPath;
            }
        }
    }
}