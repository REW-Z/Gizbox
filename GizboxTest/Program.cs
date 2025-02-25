
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gizbox;
using Gizbox.ScriptEngine;
using Gizbox.ScriptEngineV2;
using Gizbox.Interop.CSharp;
using GizboxTest;



////测试

string[] cmds = {
    "0.生成互操作代码",
    "1.生成库文件",
    "2.生成分析器硬编码",
    "3.执行Test脚本",
    "4.测试新解释器",
    };

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
            //compilerTest.SaveParserHardcodeToDesktop();//改为直接插入到代码文件  
            compilerTest.InsertParserHardcodeToSourceFile("F:\\ZQJ\\GizboxAndTools\\Gizbox\\Gizbox\\Src\\Parser\\ParserHardcoder.cs");
            Console.WriteLine("生成硬编码完成");
            return;
        }
        break;
        case 3:
        {
            //测试脚本Test  
            Console.WriteLine("测试脚本Test");
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.gix");
            Gizbox.Compiler compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.ConfigParserDataSource(hardcode: true);
            //compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            var il = compiler.Compile(source);

            Compiler.Pause("Compile End");

            ScriptEngine engine = new ScriptEngine();
            engine.AddLibSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);
            engine.csharpInteropContext.ConfigExternCallClasses(new Type[] {
                typeof(TestExternCalls),
            });
            engine.Execute(il);

            Compiler.Pause("Execute End");
        }
        break;
    case 4:
        {
            //测试脚本Test  
            Console.WriteLine("测试新解释引擎");
            unsafe
            {
                SimMemory mem = new SimMemory(10, 10);
                MyStruct s1 = new MyStruct() { id = 111, _a = 100d, _b = 10000d };
                MyStruct s2 = new MyStruct() { id = 222, _a = 200d, _b = 20000d };
                MyStruct* pS3 = mem.new_<MyStruct>();
                pS3->id = 333;
                pS3->_a = 300d;
                pS3->_b = 30000d;

                byte* ptr1 = mem.stack_malloc(sizeof(MyStruct));
                mem.write<MyStruct>(ptr1, s1);

                byte* ptr2 = mem.heap_malloc(sizeof(MyStruct));
                mem.write<MyStruct>(ptr2, s2);

                var ret1 = mem.read<MyStruct>(ptr1);
                var ret2 = mem.read<MyStruct>(ptr2);

                Console.WriteLine("ret1:" + ret1.id + "  " + ret1._a + "  " + ret1._b);
                Console.WriteLine("ret2:" + ret2.id + "  " + ret2._a + "  " + ret2._b);
                Console.WriteLine("ret3:" + pS3->id + "  " + pS3->_a + "  " + pS3->_b);

            }
            break;  
            //string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.gix");
            //Gizbox.Compiler compiler = new Compiler();
            //compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            //compiler.ConfigParserDataSource(hardcode: true);
            ////compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            //var il = compiler.Compile(source);

            //Compiler.Pause("Compile End");

            //ScriptEngineV2 engineV2 = new ScriptEngineV2();
            //engineV2.AddLibSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);
            //engineV2.csharpInteropContext.ConfigExternCallClasses(new Type[] {
            //    typeof(TestExternCalls),
            //});
            //engineV2.Execute(il);



            //Compiler.Pause("Execute End");
        }
        break;
}

Console.WriteLine("测试结束");
Console.ReadKey();


namespace GizboxTest
{

    public class TestExternCalls
    {
        public static void Console__Log(string text)
        {
            Console.WriteLine("GizboxTest >>>" + text);
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