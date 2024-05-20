
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gizbox;
using Gizbox.ScriptEngine;
using Gizbox.Interop.CSharp;


//Console.WriteLine("TOP");


namespace GizboxLangTest
{
    public class TestExternCalls
    {
        public static void Console__Log(string text)
        {
            Console.WriteLine("GizboxTest >>>" + text);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            //int result = Gizbox.Test.LLVM_Sqr(3);
            //Console.WriteLine("测试结束:" + result);
            //return;

            ////生成互操作Wrap代码      
            //InteropWrapGenerator generator = new InteropWrapGenerator();
            //generator.IncludeTypes(new Type[] {
            //    typeof(GizboxLang.Examples.Student),
            //});
            //foreach (var t in generator.closure)
            //{
            //    Console.WriteLine(t.Name);
            //}
            //generator.GenerateFile("Example");
            //Console.ReadLine();
            //return;


            ////生成库文件  
            //string libsrcCore = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\core.gix");
            //string libsrcStd = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\stdlib.gix");
            //Compiler libCompiler = new Compiler();
            //libCompiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            //libCompiler.ConfigParserDataSource(hardcode: false);
            //libCompiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            //libCompiler.CompileToLib(libsrcCore, "core", AppDomain.CurrentDomain.BaseDirectory + "\\core.gixlib");
            //libCompiler.CompileToLib(libsrcStd, "stdlib", AppDomain.CurrentDomain.BaseDirectory + "\\stdlib.gixlib");
            //return;

            ////生成分析器硬编码  
            //Gizbox.Compiler compilerTest = new Compiler();
            //compilerTest.ConfigParserDataSource(hardcode: false);
            //compilerTest.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            //compilerTest.SaveParserHardcodeToDesktop();
            //return;


            //Compile Test  
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.gix");
            Gizbox.Compiler compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.ConfigParserDataSource(hardcode: false);
            compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            var il = compiler.Compile(source);

            Compiler.Pause("Compile End");

            //Interpret  
            ScriptEngine engine = new ScriptEngine();
            engine.AddLibSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);
            engine.csharpInteropContext.ConfigExternCallClasses(new Type[] {
                typeof(TestExternCalls),
                typeof(GizboxLang.Examples.ExampleInterop),
            });
            engine.Execute(il);

            Compiler.Pause("Execute End");

            //ScriptEngine engineCallTest = new ScriptEngine();
            //engineCallTest.Load(il);
            //var ret = engineCallTest.Call("Math::Pow", 2f, 4);
            //Console.WriteLine("result:" + ret); //result:16
        }
    }
}

