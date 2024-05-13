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


namespace GizboxLangTest
{
    class Program
    {
        static void Main(string[] args)
        {
            ////生成互操作Wrap代码      
            //InteropWrapGenerator generator = new InteropWrapGenerator();
            //generator.GetClosure(new Type[] { typeof(GizboxLang.Examples.Student) });
            //foreach (var t in generator.closure)
            //{
            //    Console.WriteLine(t.Name);
            //}
            //generator.GenerateFile("AAA");
            //Console.ReadLine();
            //return;


            //生成库文件  
            //string libsrc = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\stdlib.gix");



            ////生成分析器硬编码  
            //Gizbox.Compiler compilerTest = new Compiler();
            //compilerTest.ConfigParserDataSource(hardcode: false);
            //compilerTest.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            //compilerTest.SaveParserHardcodeToDesktop();
            //return;


            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.gix");

            //Compile  
            Gizbox.Compiler compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.ConfigParserDataSource(hardcode: false);
            compiler.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
            var il = compiler.Compile(source);


            Compiler.Pause("Compile End");

            //Interpret  
            ScriptEngine engine = new ScriptEngine();
            engine.csharpInteropContext.ConfigExternCallClasses(typeof(GizboxLang.Examples.ExampleInterop));
            engine.Execute(il);

            Compiler.Pause("Execute End");
        }
    }
}

