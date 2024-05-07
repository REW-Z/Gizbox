using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FanLang;
using FanLang.ScriptEngine;



using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;



namespace FanLangTest
{
    class Program
    {
        static void Traversal(SyntaxNode node, int depth)
        {
            Console.WriteLine(new string(' ', depth * 4) + "node:" + node.GetType().Name);
            foreach(var child in node.ChildNodes())
            {
                Traversal(child, depth + 1);
            }
        }
        static void Main(string[] args)
        {
//            string src = @"
//using System;

//class Person
//{
//}
//class Test
//{
//    void Foo()
//    {
//        Person[] arr = new Person[3];
//        Person[0] = new Person();
//    }
//}";

//            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);

//            Traversal(syntaxTree.GetRoot(), 0);


//            return;
            
            
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

