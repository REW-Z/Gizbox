using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;




namespace Gizbox
{
    public enum VEnum
    {
        AAA,
        BBB,
        CCC
    }
    public struct Vector3
    {
        public VEnum e;
        public int x;
        public int y;
        public int z;
    }
    public class InteropWrapGenerator
    {
        public Type[] closure;
        public List<Type> gizPrimitives = new List<Type>();

        public void GetClosure(Type[] types)
        {
            List<string> namespaces = new List<string>();
            List<Type> closure = new List<Type>();
            List<Type> visited = new List<Type>();

            gizPrimitives.Add(typeof(void));
            gizPrimitives.Add(typeof(int));
            gizPrimitives.Add(typeof(bool));
            gizPrimitives.Add(typeof(float));
            gizPrimitives.Add(typeof(string));

            closure.AddRange(gizPrimitives);

            foreach (var t in types)
            {
                namespaces.Add(t.Namespace);
            }
            foreach (var t in types)
            {
                AddToClosureCursive(t, namespaces, closure, visited);
            }

            this.closure = closure.ToArray();
        }

        private void AddToClosureCursive(Type type, List<string> namespaces, List<Type> typeClosure, List<Type> vistied)
        {
            if (vistied.Contains(type)) return;
            vistied.Add(type);

            if (namespaces.Contains(type.Namespace) == false) return;

            if (typeClosure.Contains(type)) return;
            if (type == typeof(void)) return;
            if (type.IsGenericType == true) return;
            if (type.IsPrimitive) return;
            if (type.IsGenericParameter) return;
            if (type.IsEnum) return;
            if (type.IsArray)
            {
                AddToClosureCursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }
            if (type.IsByRef)
            {
                AddToClosureCursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }
            if (type.IsPointer)
            {
                AddToClosureCursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }


            typeClosure.Add(type);

            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                AddToClosureCursive(field.FieldType, namespaces, typeClosure, vistied);
            }
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                AddToClosureCursive(property.PropertyType, namespaces, typeClosure, vistied);
            }
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                foreach (var paraminfo in method.GetParameters())
                {
                    AddToClosureCursive(paraminfo.ParameterType, namespaces, typeClosure, vistied);
                }
                AddToClosureCursive(method.ReturnType, namespaces, typeClosure, vistied);
            }
        }

        public  void GenerateFile(string staticclassName)
        {
            StringBuilder codebuilderCs = new StringBuilder();
            StringBuilder codebuilderGiz = new StringBuilder();

            codebuilderCs.AppendLine("public class " + staticclassName);
            codebuilderCs.AppendLine("{");


            foreach (var t in closure)
            {
                GenerateType(t, codebuilderCs, codebuilderGiz);
            }
            codebuilderCs.AppendLine("}");

            var desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            System.IO.File.WriteAllText(desktop + "\\warp.cs", codebuilderCs.ToString());
            System.IO.File.WriteAllText(desktop + "\\warp.giz", codebuilderGiz.ToString());
        }

        public  void GenerateType(Type type, StringBuilder strbCs, StringBuilder strbGiz)
        {
            if (gizPrimitives.Contains(type)) return;

            string classname = type.Name;

            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(f => IsInClosure(f))
                .ToArray();
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => IsInClosure(p))
                .ToArray();
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.IsSpecialName == false)
                .Where(m => IsInClosure(m))
                .ToArray();


            //C#包装类声明  
            //类类型  
            if (type.IsValueType == false)
            {
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
                foreach (var property in properties)
                {
                    strbCs.AppendLine("\tpublic static " + ToCsType(property.PropertyType) + " " + (classname + "_get_" + property.Name) + "(" + classname + " " + "obj" + ")");
                    strbCs.AppendLine("\t{");
                    strbCs.AppendLine("\t\t" + "return obj." + property.Name + ";");
                    strbCs.AppendLine("\t}");

                    strbCs.AppendLine("\tpublic static " + "void" + " " + (classname + "_set_" + property.Name) + "(" + classname + " " + "obj" + ", " + property.PropertyType.Name + " " + "newv" + ")");
                    strbCs.AppendLine("\t{");
                    strbCs.AppendLine("\t\t" + "obj" + "." + property.Name + " = newv;");
                    strbCs.AppendLine("\t}");
                }
            }
            //结构体  
            else
            {
                if(methods.Length > 0)
                {
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

                        if (m.ReturnType == typeof(void))
                        {
                            strbCs.AppendLine("\t\t" + "arg0." + m.Name + "(" + argsStr + ");");
                        }
                        else
                        {
                            strbCs.AppendLine("\t\t" + "return arg0." + m.Name + "(" + argsStr + ");");
                        }

                        strbCs.AppendLine("\t}");
                    }

                }
            }


            //Giz类外声明部分  

            if (type.IsValueType == false)
            {
                foreach (var f in fields)
                {
                    string gizTypename = ToGizType(f.FieldType);
                    strbGiz.AppendLine("extern " + gizTypename + " " + (classname + "_get_" + f.Name) + "(" + classname + " " + classname.ToLower() + ");");
                    strbGiz.AppendLine("extern " + "void" + " " + (classname + "_set_" + f.Name) + "(" + classname + " " + classname.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
                foreach (var p in properties)
                {
                    string gizTypename = ToGizType(p.PropertyType);
                    strbGiz.AppendLine("extern " + gizTypename + " " + (classname + "_get_" + p.Name) + "(" + classname + " " + classname.ToLower() + ");");
                    strbGiz.AppendLine("extern " + "void" + " " + (classname + "_set_" + p.Name) + "(" + classname + " " + classname.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
            }

            foreach (var m in methods)
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
            if (type.IsValueType == false && this.closure.Contains(type.BaseType))
            {
                inhertStr = ":" + type.BaseType.Name;
            }
            strbGiz.AppendLine("class " + classname + inhertStr);
            strbGiz.AppendLine("{");

            //类类型
            if(type.IsValueType == false)
            {
                foreach (var f in fields)
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

                foreach (var p in properties)
                {
                    string gizTypename = ToGizType(p.PropertyType);

                    strbGiz.AppendLine("\t" + gizTypename + " " + p.Name + "()");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + "return " + (classname + "_get_" + p.Name) + "(this);");
                    strbGiz.AppendLine("\t}");

                    strbGiz.AppendLine("\t" + "void " + p.Name + "(" + gizTypename + " " + "newv" + ")");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + (classname + "_set_" + p.Name) + "(this, " + "newv" + ");");
                    strbGiz.AppendLine("\t}");
                }

            }
            //结构体类型  
            else
            {
                foreach (var f in fields)
                {
                    string gizTypename = ToGizType(f.FieldType);

                    strbGiz.AppendLine("\t" + gizTypename + " " + f.Name + " = " + GizTypeDefaultValueStr(gizTypename) + ";");
                }
            }



            foreach (var method in methods)
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
                    if (i != 0)
                    {
                        paramsStr += (p + " arg" + i.ToString());
                    }


                    if (i > 0) argsStr += ", ";
                    if (i == 0)
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


                if (ToGizType(method.ReturnType) == "void")
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

        public static string GizTypeDefaultValueStr(string gizType)
        {
            switch (gizType)
            {
                case "int": return "0";
                case "bool": return "false";
                case "float": return "0.0f";
                case "string": return "";
                default:
                    {
                        return "null";
                    }
            }
        }


        public bool IsInClosure(FieldInfo field)
        {
            return closure.Contains(field.FieldType);
        }
        public bool IsInClosure(PropertyInfo p)
        {
            return closure.Contains(p.PropertyType);
        }
        public bool IsInClosure(MethodInfo method)
        {
            foreach (var p in method.GetParameters())
            {
                if (closure.Contains(p.ParameterType) == false)
                {
                    return false;
                }
            }
            if (closure.Contains(method.ReturnType) == false)
            {
                return false;
            }

            return true;
        }



        public static string ToGizType(Type t)
        {
            Console.WriteLine("类型封送：" + t);
            switch (t.Name)
            {
                case "Void": return "void";
                case "Boolean": return "bool";
                case "Int32": return "int";
                case "Single": return "float";
                case "String": return "string";
                default:
                    {
                        return t.Name;
                    }
            }
        }

        public static string ToCsType(Type t)
        {
            if (t == typeof(void))
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
