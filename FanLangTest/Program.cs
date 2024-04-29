using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FanLang;



namespace FanLangTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.txt");

            Compiler.Pause("代码读取完毕，任意键继续...");

            FanLang.Compiler compiler = new Compiler();
            compiler.Compile(source);


            Compiler.Pause("按任意键执行");
        }
    }
}
