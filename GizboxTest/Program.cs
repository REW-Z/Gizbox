
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
                typeof(TestClass),
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
            compilerTest.InsertParserHardcodeToSourceFile(Path.Combine(Utility.RepoPath, "Src\\Parser\\ParserHardcoder.cs"));
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


            //测试脚本Test  
            Console.WriteLine("测试脚本Test");
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.gix");
            Gizbox.Compiler compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.ConfigParserDataSource(hardcode: true);
            //compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            var il = compiler.CompileToIR(source, isMainUnit: true, "test");

            il.Print();

            compiler.CompileIRToExe(il, System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

            //执行测试  
            Console.WriteLine("按E执行，按其他退出");
            if(Console.ReadKey().Key == ConsoleKey.E)
            {
                Compiler.Run("test", "", System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            }
            else
            {
            }
            
        }
        break;
        case 9:
        {
            //测试脚本Test  
            Console.WriteLine("测试-模板");
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test_template.gix");
            Gizbox.Compiler compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.ConfigParserDataSource(hardcode: true);
            //compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            var il = compiler.CompileToIR(source, isMainUnit: true, "test");
            Compiler.Pause("Compile End");

            il.Print();

            compiler.CompileIRToExe(il, System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
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
                    repoPath = GetRepoPath();
                }
                return repoPath;
            }
        }
        private static string GetRepoPath()
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

            Console.WriteLine(targetDir.FullName);
            return targetDir.FullName;
        }
    }
    public class TestExternCalls
    {
        public static void Console__Print(string text)
        {
            Console.Write(text);
        }
        public static void Console__PrintLn(string text)
        {
            Console.WriteLine(text);
        }

        public static TestClass[] GetObjects()
        {
            var objs = new TestClass[1];
            objs[0] = new TestClass();
            objs[0].id = 233;
            return objs;
        }
        public static TestClass GetObject(int id)
        {
            return new TestClass() { id = id };
        }
        public static System.Int32 GizboxTest__TestClass_get_id(GizboxTest.TestClass obj)
        {
            return obj.id;
        }
        public static void GizboxTest__TestClass_set_id(GizboxTest.TestClass obj, System.Int32 newv)
        {
            obj.id = newv;
        }
    }

    public class TestClass
    {
        public int id = 0;
    }

    public struct MyStruct
    {
        public int id;
        public double _a;
        public double _b;
    }
}