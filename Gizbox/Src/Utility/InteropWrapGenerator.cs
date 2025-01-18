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

        public void IncludeTypes(params Type[] types)
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


        public void StartGenerate(string filenameWithoutExtension, string csCodeSaveDirectory, string gizCodeSaveDirectory)
        {
            StringBuilder codebuilderCs = new StringBuilder();
            StringBuilder codebuilderGiz = new StringBuilder();

            //csInterop类 类名
            codebuilderCs.AppendLine("public class "+ filenameWithoutExtension);
            codebuilderCs.AppendLine("{");


            List<string> namespaceNameList = new List<string>();
            closure = closure.OrderBy(GetInheritanceDepth).ToArray();
            foreach(var t in closure)
            {
                if(namespaceNameList.Contains(t.Namespace) == false)
                {
                    namespaceNameList.Add(t.Namespace);
                }
            }
            List<Type>[] namespaceArr = new List<Type>[namespaceNameList.Count];
            for (int i = 0; i < namespaceArr.Length; ++i)
            {
                string nspace = namespaceNameList[i];
                namespaceArr[i] = closure.Where(t => t.Namespace == nspace).ToList();

                GenNamspace(nspace, namespaceArr[i], codebuilderCs, codebuilderGiz);
            }

            //csInterop类
            codebuilderCs.AppendLine("}");

            System.IO.File.WriteAllText(csCodeSaveDirectory + "\\" + filenameWithoutExtension + ".cs", codebuilderCs.ToString());
            System.IO.File.WriteAllText(gizCodeSaveDirectory + "\\" + filenameWithoutExtension + ".gix", codebuilderGiz.ToString());
        }

        private void GenNamspace(string name, List<Type> types, StringBuilder strbCs, StringBuilder strbGiz)
        {
            //strbCs.AppendLine("namespace " + name);
            //strbCs.AppendLine("{");

            foreach (var t in types)
            {
                GenerateType(t, strbCs, strbGiz);
            }

            strbCs.AppendLine();

            //strbCs.AppendLine("}");
        }

        private void GenerateType(Type type, StringBuilder strbCs, StringBuilder strbGiz)
        {
            if (gizPrimitives.Contains(type)) return;

            string classnameGz = type.FullName.Replace("+", "::").Replace(".", "::");
            string classnameCs = type.FullName.Replace("+", "__").Replace(".", "__");

            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(f => IsInClosure(f))
                .Where(f => IsMemberObsolete(f) == false)
                .ToArray();
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => IsInClosure(p))
                .Where(f => IsMemberObsolete(f) == false)
                .ToArray();
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.IsSpecialName == false)
                .Where(m => IsInClosure(m))
                .Where(f => IsMemberObsolete(f) == false)
                .ToArray();
            var staticFuncs = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.IsSpecialName == false)
                .Where(m => IsInClosure(m))
                .Where(f => IsMemberObsolete(f) == false)
                .ToArray();


            //C#包装类声明  
            //类类型  
            if (type.IsValueType == false)
            {
                foreach (var f in fields)
                {
                    strbCs.AppendLine("\tpublic static " + ToCsType(f.FieldType) + " " + (classnameCs + "_get_" + f.Name) + "(" + type.FullName + " " + "obj" + ")");
                    strbCs.AppendLine("\t{");
                    strbCs.AppendLine("\t\t" + "return obj." + f.Name + ";");
                    strbCs.AppendLine("\t}");

                    strbCs.AppendLine("\tpublic static " + "void" + " " + (classnameCs + "_set_" + f.Name) + "(" + type.FullName + " " + "obj" + ", " + f.FieldType.FullName + " " + "newv" + ")");
                    strbCs.AppendLine("\t{");
                    strbCs.AppendLine("\t\t" + "obj" + "." + f.Name + " = newv;");
                    strbCs.AppendLine("\t}");
                }
                foreach (var property in properties)
                {
                    if(property.CanRead)
                    {
                        strbCs.AppendLine("\tpublic static " + ToCsType(property.PropertyType) + " " + (classnameCs + "_get_" + property.Name) + "(" + type.FullName + " " + "obj" + ")");
                        strbCs.AppendLine("\t{");
                        strbCs.AppendLine("\t\t" + "return obj." + property.Name + ";");
                        strbCs.AppendLine("\t}");
                    }

                    if(property.CanWrite)
                    {
                        strbCs.AppendLine("\tpublic static " + "void" + " " + (classnameCs + "_set_" + property.Name) + "(" + type.FullName + " " + "obj" + ", " + property.PropertyType.FullName + " " + "newv" + ")");
                        strbCs.AppendLine("\t{");
                        strbCs.AppendLine("\t\t" + "obj" + "." + property.Name + " = newv;");
                        strbCs.AppendLine("\t}");
                    }
                }
            }
            //结构体  
            else
            {
                //无访问器  
            }

            //方法  
            foreach (var m in methods)
            {
                List<string> paramList = new List<string>();
                paramList.Add(type.FullName);//this 参数  
                paramList.AddRange(m.GetParameters().Select(p => p.ParameterType.FullName));

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

                strbCs.AppendLine("\tpublic static " + ToCsType(m.ReturnType) + " " + (classnameCs + "_" + m.Name) + "(" + paramsStr + ")");
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
            //静态函数
            foreach (var func in staticFuncs)
            {
                List<string> paramList = new List<string>();
                paramList.AddRange(func.GetParameters().Select(p => p.ParameterType.FullName));

                string paramsStr = "";
                string argsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i > 0) paramsStr += ", ";
                    paramsStr += (p + " arg" + i.ToString());

                    if (i > 0) argsStr += ", ";
                    argsStr += ("arg" + i.ToString());

                    i++;
                }

                strbCs.AppendLine("\tpublic static " + ToCsType(func.ReturnType) + " " + (classnameCs + "_" + func.Name + "_Static") + "(" + paramsStr + ")");
                strbCs.AppendLine("\t{");

                if (func.ReturnType == typeof(void))
                {
                    strbCs.AppendLine("\t\t" + type.FullName + "." + func.Name + "(" + argsStr + ");");
                }
                else
                {
                    strbCs.AppendLine("\t\t" + "return " + type.FullName + "." + func.Name + "(" + argsStr + ");");
                }

                strbCs.AppendLine("\t}");
            }



            //Giz类外声明部分 - 类静态方法      
            foreach (var func in staticFuncs)
            {
                List<string> paramList = new List<string>();
                paramList.AddRange(func.GetParameters().Select(p => ToGizType(p.ParameterType)));

                string paramsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i != 0) paramsStr += ", ";
                    paramsStr += (p + " arg" + i.ToString());
                    i++;
                }

                strbGiz.AppendLine("extern " + ToGizType(func.ReturnType) + " " + (classnameGz + "_" + func.Name + "_Static") + "(" + paramsStr + ");");
            }

            //Giz类外声明部分 - 成员函数实现和成员字段/属性实现  
            if (type.IsValueType == false)
            {
                foreach (var f in fields)
                {
                    string gizTypename = ToGizType(f.FieldType);
                    strbGiz.AppendLine("extern " + gizTypename + " " + (classnameGz + "_get_" + f.Name) + "(" + classnameGz + " " + classnameGz.ToLower() + ");");
                    strbGiz.AppendLine("extern " + "void" + " " + (classnameGz + "_set_" + f.Name) + "(" + classnameGz + " " + classnameGz.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
                foreach (var p in properties)
                {
                    string gizTypename = ToGizType(p.PropertyType);
                    if(p.CanRead)
                        strbGiz.AppendLine("extern " + gizTypename + " " + (classnameGz + "_get_" + p.Name) + "(" + classnameGz + " " + classnameGz.ToLower() + ");");
                    if(p.CanWrite)
                        strbGiz.AppendLine("extern " + "void" + " " + (classnameGz + "_set_" + p.Name) + "(" + classnameGz + " " + classnameGz.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
            }

            foreach (var m in methods)
            {
                List<string> paramList = new List<string>();
                paramList.Add(classnameGz);
                paramList.AddRange(m.GetParameters().Select(p => ToGizType(p.ParameterType)));

                string paramsStr = "";
                int i = 0;
                foreach (var p in paramList)
                {
                    if (i != 0) paramsStr += ", ";
                    paramsStr += (p + " arg" + i.ToString());
                    i++;
                }

                strbGiz.AppendLine("extern " + ToGizType(m.ReturnType) + " " + (classnameGz + "_" + m.Name) + "(" + paramsStr + ");");
            }
            strbGiz.AppendLine();
            strbGiz.AppendLine();
            strbGiz.AppendLine();


            //Giz类内声明部分  
            string inhertStr = "";
            if (type.IsValueType == false && this.closure.Contains(type.BaseType))
            {
                inhertStr = "  :  " + type.BaseType.FullName.Replace("+", "::").Replace(".", "::"); ;
            }
            strbGiz.AppendLine("class " + classnameGz + inhertStr);
            strbGiz.AppendLine("{");


            //类类型
            if (type.IsValueType == false)
            {
                foreach (var f in fields)
                {
                    string gizTypename = ToGizType(f.FieldType);

                    strbGiz.AppendLine("\t" + gizTypename + " " + f.Name + "()");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + "return " + (classnameGz + "_get_" + f.Name) + "(this);");
                    strbGiz.AppendLine("\t}");

                    strbGiz.AppendLine("\t" + "void " + f.Name + "(" + gizTypename + " " + "newv" + ")");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + (classnameGz + "_set_" + f.Name) + "(this, " + "newv" + ");");
                    strbGiz.AppendLine("\t}");
                }

                foreach (var p in properties)
                {
                    string gizTypename = ToGizType(p.PropertyType);

                    if(p.CanRead)
                    {
                        strbGiz.AppendLine("\t" + gizTypename + " " + p.Name + "()");
                        strbGiz.AppendLine("\t{");
                        strbGiz.AppendLine("\t\t" + "return " + (classnameGz + "_get_" + p.Name) + "(this);");
                        strbGiz.AppendLine("\t}");
                    }

                    if(p.CanWrite)
                    {
                        strbGiz.AppendLine("\t" + "void " + p.Name + "(" + gizTypename + " " + "newv" + ")");
                        strbGiz.AppendLine("\t{");
                        strbGiz.AppendLine("\t\t" + (classnameGz + "_set_" + p.Name) + "(this, " + "newv" + ");");
                        strbGiz.AppendLine("\t}");
                    }
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
                paramList.Add(classnameGz);
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
                    strbGiz.AppendLine("\t\t" + (classnameGz + "_" + method.Name) + "(" + argsStr + ");");
                }
                else
                {
                    strbGiz.AppendLine("\t\t" + "return " + (classnameGz + "_" + method.Name) + "(" + argsStr + ");");
                }

                strbGiz.AppendLine("\t}");
            }


            strbGiz.AppendLine("}");
            strbGiz.AppendLine();
            strbGiz.AppendLine();
        }

        private static string GizTypeDefaultValueStr(string gizType)
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


        private bool IsInClosure(FieldInfo field)
        {
            return closure.Contains(field.FieldType);
        }
        private bool IsInClosure(PropertyInfo p)
        {
            return closure.Contains(p.PropertyType);
        }
        private bool IsInClosure(MethodInfo method)
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


        private static int GetInheritanceDepth(Type type)
        {
            int depth = 0;
            while (type.BaseType != null)
            {
                depth++;
                type = type.BaseType;
            }
            return depth;
        }

        public static bool IsMemberObsolete(MemberInfo member)
        {
            // 检查该字段是否有ObsoleteAttribute
            ObsoleteAttribute obsoleteAttr = member.GetCustomAttribute<ObsoleteAttribute>();
            return obsoleteAttr != null;
        }

        private static string ToGizType(Type t)
        {
            switch (t.Name)
            {
                case "Void": return "void";
                case "Boolean": return "bool";
                case "Int32": return "int";
                case "Single": return "float";
                case "String": return "string";
                default:
                    {
                        return t.FullName.Replace(".", "::").Replace("+", "__");
                    }
            }
        }

        private static string ToCsType(Type t)
        {
            if (t == typeof(void))
            {
                return "void";
            }
            else
            {
                return t.FullName;
            }
        }
    }
}
