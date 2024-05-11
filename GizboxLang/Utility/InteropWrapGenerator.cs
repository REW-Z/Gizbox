using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


/*
         public static void Person_Say(Person p, string text)
        {
            p.Say(text);
        }
        public static void Person_set_name(Person p, string text)
        {
            p.name = text;
        }
        public static string Person_get_name(Person p)
        {
            return p.name;
        }
 */

namespace Gizbox
{
    public class InteropWrapGenerator
    {
        public void GenerateAssembly(Type type)
        {
            var asm = type.Assembly;

            StringBuilder codebuilderCs = new StringBuilder();
            StringBuilder codebuilderGiz = new StringBuilder();

            foreach(var t in asm.GetTypes())
            {
                GenerateType(t, codebuilderCs, codebuilderGiz);
            }


            var desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            System.IO.File.WriteAllText(desktop + "\\warp.cs", codebuilderCs.ToString());
            System.IO.File.WriteAllText(desktop + "\\warp.giz", codebuilderGiz.ToString());
        }
        public void GenerateType(Type type,  StringBuilder strbCs, StringBuilder strbGiz)
        {
            string classname = type.Name;

            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.IsSpecialName == false)
                .ToArray();


            //C#包装类声明  
            strbCs.AppendLine("public static class " + classname + "_Interop");
            strbCs.AppendLine("{");

            foreach (var f in fields)
            {
                strbCs.AppendLine("\tpublic static " + ToCsType(f.FieldType) + " " + (classname + "_get_" + f.Name) + "(" + classname + " " + "obj" + ")");
                strbCs.AppendLine("\t{");
                strbCs.AppendLine("\t\t" + "return obj." + f.Name + ";");
                strbCs.AppendLine("\t}");

                strbCs.AppendLine("\tpublic static " + "void" + " " + (classname + "_set_" + f.Name) + "(" + classname + " " + "obj" + ", " + f.FieldType.Name + " " + "newv" + ")");
                strbCs.AppendLine("\t{");
                strbCs.AppendLine("\t\t" + "obj" + "." + f.Name + " = newv;");
                strbCs.AppendLine("\t}");
            }

            foreach (var m in methods)
            {
                List<string> paramList = new List<string>();
                paramList.Add(classname);
                paramList.AddRange(m.GetParameters().Select(p => p.ParameterType.Name));

                string paramsStr = "";
                string argsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i > 0) paramsStr += ", ";
                    paramsStr += (p + " arg" + i.ToString());


                    if (i > 1) argsStr += ", ";
                    if (i != 0)
                    {
                        argsStr += ("arg" + i.ToString());
                    }

                    i++;
                }

                strbCs.AppendLine("\tpublic static " + ToCsType(m.ReturnType) + " " + (classname + "_" + m.Name) + "(" + paramsStr + ")");
                strbCs.AppendLine("\t{");

                if(m.ReturnType == typeof(void))
                {
                    strbCs.AppendLine("\t\t" + "arg0." + m.Name + "(" + argsStr + ");");
                }
                else
                {
                    strbCs.AppendLine("\t\t" + "return arg0." + m.Name + "(" + argsStr + ");");
                }

                strbCs.AppendLine("\t}");
            }

            strbCs.AppendLine("}");
            strbCs.AppendLine("");

            //Giz类外声明部分  

            foreach (var f in fields)
            {
                string gizTypename = ToGizType(f.FieldType) ;
                strbGiz.AppendLine("extern " + gizTypename + " " + (classname + "_get_" + f.Name) + "(" + classname + " " + classname.ToLower() + ");");
                strbGiz.AppendLine("extern " + "void" + " " + (classname + "_set_" + f.Name) + "(" + classname + " " + classname.ToLower() + ", " + gizTypename + " " + "newv" + ");");
            }
            foreach(var m in methods)
            {
                List<string> paramList = new List<string>();
                paramList.Add(classname);
                paramList.AddRange(m.GetParameters().Select(p => ToGizType(p.ParameterType))); 
                
                string paramsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i != 0) paramsStr += ", ";
                    paramsStr += (p + " arg" + i.ToString());
                    i++;
                }

                strbGiz.AppendLine("extern " + ToGizType(m.ReturnType) + " " + (classname + "_" + m.Name) + "(" + paramsStr + ")");
            }


            //Giz类内声明部分  
            string inhertStr = "";
            if(type.BaseType != typeof(Object))
            {
                inhertStr = ":" + type.BaseType.Name;
            }
            strbGiz.AppendLine("class " + classname + inhertStr);
            strbGiz.AppendLine("{");

            foreach(var f in fields)
            {
                string gizTypename = ToGizType(f.FieldType);

                strbGiz.AppendLine("\t" + gizTypename + " " + f.Name + "()");
                strbGiz.AppendLine("\t{");
                strbGiz.AppendLine("\t\t" + "return " + (classname + "_get_" + f.Name) + "(this);");
                strbGiz.AppendLine("\t}");

                strbGiz.AppendLine("\t" + "void " + f.Name + "(" + gizTypename + " " + "newv" + ")");
                strbGiz.AppendLine("\t{");
                strbGiz.AppendLine("\t\t" + (classname + "_set_" + f.Name) + "(this, " + "newv" + ");");
                strbGiz.AppendLine("\t}");
            }



            foreach(var method in methods)
            {
                List<string> paramList = new List<string>();
                paramList.Add(classname);
                paramList.AddRange(method.GetParameters().Select(p => ToGizType(p.ParameterType)));

                string paramsStr = "";
                string argsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i > 1) paramsStr += ", ";
                    if(i != 0)
                    {
                        paramsStr += (p + " arg" + i.ToString());
                    }


                    if (i > 0) argsStr += ", ";
                    if(i == 0)
                    {
                        argsStr += ("this");
                    }
                    else
                    {
                        argsStr += ("arg" + i.ToString());
                    }
                    
                    
                    i++;
                }

                strbGiz.AppendLine("\t" + ToGizType(method.ReturnType) + " " + method.Name + "(" + paramsStr + ")");
                strbGiz.AppendLine("\t{");
                

                if(ToGizType(method.ReturnType) == "void")
                {
                    strbGiz.AppendLine("\t\t" + (classname + "_" + method.Name) + "(" + argsStr + ");");
                }
                else
                {
                    strbGiz.AppendLine("\t\t" + "return " + (classname + "_" + method.Name) + "(" + argsStr + ");");
                }

                strbGiz.AppendLine("\t}");
            }


            strbGiz.AppendLine("}");
            strbGiz.AppendLine("");
        }

        public static string ToGizType(Type t)
        {
            Console.WriteLine("类型封送：" + t);
            switch(t.Name)
            {
                case "Void": return "void";
                case "Boolean" : return "bool";
                case "Int32" : return "int";
                case "Single" : return "float";
                case "String": return "string";
                default:
                    {
                        return t.Name;
                    }
            }
        }

        public static string ToCsType(Type t)
        {
            if(t == typeof(void))
            {
                return "void";
            }
            else
            {
                return t.Name;
            }
        }
    }
}
