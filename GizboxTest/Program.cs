﻿
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
using GizboxTest;


////测试
//int result = Gizbox.Test.LLVM_Sqr(3);
//Console.WriteLine("测试结束:" + result);
//return;



////生成互操作Wrap代码      
//InteropWrapGenerator generator = new InteropWrapGenerator();
//generator.IncludeTypes(new Type[] {
//    typeof(Gizbox.Examples.Student),
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
//Console.WriteLine("生成分析器硬编码");
//Gizbox.Compiler compilerTest = new Compiler();
//compilerTest.ConfigParserDataSource(hardcode: false);
//compilerTest.ConfigParserDataPath(AppDomain.CurrentDomain.BaseDirectory + "parser_data.txt");
////compilerTest.SaveParserHardcodeToDesktop();//改为直接插入到代码文件  
//compilerTest.InsertParserHardcodeToSourceFile("F:\\ZQJ\\GizboxAndTools\\Gizbox\\Gizbox\\Src\\Parser\\ParserHardcoder.cs");
//Console.WriteLine("生成硬编码完成");
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
                typeof(Gizbox.Examples.ExampleInterop),
            });
engine.Execute(il);

Compiler.Pause("Execute End");






namespace GizboxTest
{
    public class TestExternCalls
    {
        public static void Console__Log(string text)
        {
            Console.WriteLine("GizboxTest >>>" + text);
        }
    }
}