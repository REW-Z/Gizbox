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
            string source =
                @"
                    void Foo()
                    {
                        int tmp = 0;
                    }
                    
                    float Add(float x, float y)
                    {
                        return x + y;
                    }
                      
                    bool IsPositive(float x)
                    {
                        return true;
                    }

                    int a = 233 ;
                    int b = (a + 111) * 222;
                    float c = 123.0f;                  
                    string d = ""AAA"";

                    float e = Add(1.0f, 2.0f);
                    bool isPositive = IsPositive(e);

                    if(isPositive)
                    {
                        Foo();
                    }
                    else if(true)
                    {
                        a = a + 1;
                    }
                    else
                    {
                        b = b + 1;
                    }


                    while(c < 999.0f)
                    {
                        c = c + 1.0f;
                    }
                    for(int i = 0; i < 99; ++i)
                    {
                        a *= 2;
                    }

                    int fff = (float) a;

                    b.aaa();
                ";

            ////二义性文法测试  
            //string input =
            //    @"var a = 1 + 2 * 3; ";

            FanLang.Compiler compiler = new Compiler();
            compiler.Compile(source);

        }
    }
}
