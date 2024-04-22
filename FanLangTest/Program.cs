using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FanLang;


using System.Linq.Expressions;



namespace FanLangTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine("选择例程：");
            //Console.WriteLine("1 - 简单的语法分析器");
            //Console.WriteLine("2 - LL(0)预测分析器");
            //Console.WriteLine("3 - LALR语法分析器");
            //string input = Console.ReadLine();
            //switch(input)
            //{
            //    case "1": TestSimpleParse() break;
            //    case "2": TestLLParse() break;
            //    case "3": TestLALRParse() break;
            //    default:break;
            //}

            Test();
        }

        static void TestSimpleParse()
        {
            string source =
                @"var a = 233;
                  var b = 111;
                  var c = a + 999;
                 ";

            FanLang.Compiler compiler = new Compiler();
            SimpleParseTree tree = compiler.TestSimpleCompile(source);

            Console.WriteLine("\n\n语法分析树：");
            Console.WriteLine(tree.Serialize());
        }

        static void TestLLParse()
        {
            string source =
                @"var a = 233;
                  var b = (a + 111) * 222;
                 ";

            FanLang.Compiler compiler = new Compiler();
            SimpleParseTree tree = compiler.TestLL0Compile(source);

            Console.WriteLine("\n\n语法分析树：");
            Console.WriteLine(tree.Serialize());
        }

        static void Test()
        {
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.txt");

            Console.Write(source);
            Console.WriteLine("代码读取完毕，任意键继续...");
            Console.ReadKey();

            ////二义性文法测试  
            //string input =
            //    @"var a = 1 + 2 * 3; ";

            FanLang.Compiler compiler = new Compiler();
            compiler.Compile(source);


            Console.WriteLine("按任意键执行");
            Console.ReadKey();

            //FanLang.ScriptEngine.ScriptEngine engine = new FanLang.ScriptEngine.ScriptEngine();
            //engine.tree = compiler.syntaxTree;

            //engine.Execute();
        }
    }
}
