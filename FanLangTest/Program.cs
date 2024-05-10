using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FanLang;
using FanLang.ScriptEngine;



namespace FanLangTest
{
    using FanLang.Interop.CSharp;
    class Program
    {
        static void Main(string[] args)
        {
            //InteropWrapGenerator generator = new InteropWrapGenerator();
            //generator.GenerateAssembly(typeof());
            //Console.ReadLine();
            //return;

            string source = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\test.txt");

            //Compile  
            FanLang.Compiler compiler = new Compiler();
            var il = compiler.Compile(source);


            Compiler.Pause("Compile End");

            //Interpret  
            ScriptEngine engine = new ScriptEngine();
            engine.Execute(il);

            Compiler.Pause("Execute End");
        }
    }
}

