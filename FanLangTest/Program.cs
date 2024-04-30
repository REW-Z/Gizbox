using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FanLang;
using FanLang.ScriptEngine;



namespace FanLangTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.txt");

            //Compile  
            FanLang.Compiler compiler = new Compiler();
            var il = compiler.Compile(source);

            //Interpret  
            ScriptEngine engine = new ScriptEngine();
            engine.Execute(il);

            Compiler.Pause("End");
        }
    }
}
