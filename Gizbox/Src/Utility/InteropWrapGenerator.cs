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
            HashSet<string> namespaces = new HashSet<string>();
            HashSet<Type> closure = new HashSet<Type>();
            HashSet<Type> visited = new HashSet<Type>();

            gizPrimitives.Add(typeof(void));
            gizPrimitives.Add(typeof(int));
            gizPrimitives.Add(typeof(bool));
            gizPrimitives.Add(typeof(float));
            gizPrimitives.Add(typeof(string));

            foreach(var pt in gizPrimitives)
            {
                closure.Add(pt);
            }
            

            foreach (var t in types)
            {
                namespaces.Add(t.Namespace);
            }
            foreach (var t in types)
            {
                AddToClosureRecursive(t, namespaces, closure, visited);
            }

            this.closure = closure.ToArray();
        }

        private void AddToClosureRecursive(Type type, HashSet<string> namespaces, HashSet<Type> typeClosure, HashSet<Type> vistied)
        {
            if (vistied.Contains(type)) return;
            vistied.Add(type);

            if (namespaces.Contains(type.Namespace) == false) return;

            if(typeClosure.Contains(type)) { return; };
            if (type == typeof(void)) { return; };
            if (type.IsGenericType == true) { return; };
            if (type.IsPrimitive) { return; } 
            if (type.IsGenericParameter) { return; };
            if (type.IsArray)
            {
                AddToClosureRecursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }
            if (type.IsByRef)
            {
                AddToClosureRecursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }
            if (type.IsPointer)
            {
                AddToClosureRecursive(type.GetElementType(), namespaces, typeClosure, vistied);
                return;
            }
            if(type.IsEnum)
            {
                typeClosure.Add(type);
                return;
            }

            //ADD  
            typeClosure.Add(type);


            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                AddToClosureRecursive(field.FieldType, namespaces, typeClosure, vistied);
            }
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                AddToClosureRecursive(property.PropertyType, namespaces, typeClosure, vistied);
            }
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                foreach (var paraminfo in method.GetParameters())
                {
                    AddToClosureRecursive(paraminfo.ParameterType, namespaces, typeClosure, vistied);
                }
                AddToClosureRecursive(method.ReturnType, namespaces, typeClosure, vistied);
            }


            //嵌套类型    
            var nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach(var nestedType in nestedTypes)
            {
                if(nestedType.IsPublic)
                {
                    AddToClosureRecursive(nestedType, namespaces, typeClosure, vistied);
                }
            }
        }


        public void StartGenerate(string filenameWithoutExtension, string csCodeSaveDirectory, string gizCodeSaveDirectory)
        {
            StringBuilder codebuilderCs = new StringBuilder();
            StringBuilder codebuilderGiz = new StringBuilder();

            //csInterop类 起始
            codebuilderCs.AppendLine("public class "+ filenameWithoutExtension);
            codebuilderCs.AppendLine("{");

            //gizInterop类 起始  
            codebuilderGiz.AppendLine("import <\"core\">");
            codebuilderGiz.AppendLine("import <\"stdlib\">");
            codebuilderGiz.AppendLine();

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

            //csInterop类 结束  
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

            //枚举  
            if(type.IsEnum)
            {
                GenerateTypeAsEnum(type, strbCs, strbGiz);
            }
            //类或者结构体  
            else
            {
                GenerateTypeAsClassOrStruct(type, strbCs, strbGiz);
            }
        }

        private void GenerateTypeAsClassOrStruct(Type type, StringBuilder strbCs, StringBuilder strbGiz)
        {
            string gzClassnameIdentif = ToIdentifClassName(type.FullName);
            string csClassnameIdentif = ToIdentifClassName(type.FullName);
            


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


            // *** C# ***   

            //属性和字段  
            {
                //类类型  
                if(type.IsValueType == false)
                {
                    foreach(var f in fields)
                    {
                        string anyCast = f.FieldType.IsEnum ? "(int)" : string.Empty;
                        string anyReverseCast = f.FieldType.IsEnum ? ("(" + ToCsClassName(f.FieldType.FullName) + ")") : string.Empty;


                        strbCs.AppendLine("\tpublic static " + ToCsMarshalClassName(f.FieldType) + " " + (csClassnameIdentif + "_get_" + f.Name) + "(" + ToCsMarshalClassName(type) + " " + "obj" + ")");
                        strbCs.AppendLine("\t{");
                        strbCs.AppendLine("\t\t" + "return " + anyCast + "obj." + f.Name + ";");
                        strbCs.AppendLine("\t}");

                        strbCs.AppendLine("\tpublic static " + "void" + " " + (csClassnameIdentif + "_set_" + f.Name) + "(" + ToCsMarshalClassName(type) + " " + "obj" + ", " + ToCsMarshalClassName(f.FieldType) + " " + "newv" + ")");
                        strbCs.AppendLine("\t{");
                        strbCs.AppendLine("\t\t" + "obj" + "." + f.Name + " = " + anyReverseCast + "newv;");
                        strbCs.AppendLine("\t}");
                    }
                    foreach(var property in properties)
                    {
                        string anyCast = property.PropertyType.IsEnum ? "(int)" : string.Empty;
                        string anyReverseCast = property.PropertyType.IsEnum ? ("(" + ToCsClassName(property.PropertyType.FullName) + ")") : string.Empty;


                        if(property.CanRead)
                        {
                            strbCs.AppendLine("\tpublic static " + ToCsMarshalClassName(property.PropertyType) + " " + (csClassnameIdentif + "_get_" + property.Name) + "(" + type.FullName + " " + "obj" + ")");
                            strbCs.AppendLine("\t{");
                            strbCs.AppendLine("\t\t" + "return " + anyCast + "obj." + property.Name + ";");
                            strbCs.AppendLine("\t}");
                        }

                        if(property.CanWrite)
                        {
                            strbCs.AppendLine("\tpublic static " + "void" + " " + (csClassnameIdentif + "_set_" + property.Name) + "(" + ToCsMarshalClassName(type) + " " + "obj" + ", " + ToCsMarshalClassName(property.PropertyType) + " " + "newv" + ")");
                            strbCs.AppendLine("\t{");
                            strbCs.AppendLine("\t\t" + "obj" + "." + property.Name + " = " + anyReverseCast + "newv;");
                            strbCs.AppendLine("\t}");
                        }
                    }
                }
                //结构体  
                else
                {
                    //无访问器  
                }
            }

            //实例方法和静态函数    
            {
                //实例方法
                foreach(var m in methods)
                {
                    List<Type> parameTypes = new List<Type>();
                    parameTypes.Add(type);
                    parameTypes.AddRange(m.GetParameters().Select(p => p.ParameterType));
                    
                    List<string> paramDefines = new List<string>();
                    List<string> callArgs = new List<string>();
                    for(int i = 0; i < parameTypes.Count; ++i)
                    {
                        paramDefines.Add($"{ToCsMarshalClassName(parameTypes[i])} arg{i}" );

                        if(i != 0)
                        {
                            string anyReverseCast = parameTypes[i].IsEnum ? ($"({ ToCsClassName(parameTypes[i].FullName)})") : "";
                            callArgs.Add($"{anyReverseCast}arg{i}");
                        }
                    }

                    string paramsStr = ConcatViaComma(paramDefines);
                    string callArgsStr = ConcatViaComma(callArgs);


                    strbCs.AppendLine("\tpublic static " + ToCsMarshalClassName(m.ReturnType) + " " + (csClassnameIdentif + "_" + m.Name) + "(" + paramsStr + ")");
                    strbCs.AppendLine("\t{");

                    if(m.ReturnType == typeof(void))
                    {
                        strbCs.AppendLine("\t\t" + "arg0." + m.Name + "(" + callArgsStr + ");");
                    }
                    else
                    {
                        string anyIntCast = m.ReturnType.IsEnum ? "(int)" : "";
                        strbCs.AppendLine("\t\t" + "return " + anyIntCast + "arg0." + m.Name + "(" + callArgsStr + ");");
                    }

                    strbCs.AppendLine("\t}");
                }
                //静态函数
                foreach(var func in staticFuncs)
                {
                    List<Type> parameTypes = new List<Type>();
                    parameTypes.AddRange(func.GetParameters().Select(p => p.ParameterType));

                    List<string> paramDefines = new List<string>();
                    List<string> callArgs = new List<string>();
                    for(int i = 0; i < parameTypes.Count; ++i)
                    {
                        paramDefines.Add($"{ToCsMarshalClassName(parameTypes[i])} arg{i}");

                        string anyReverseCast = parameTypes[i].IsEnum ? ($"({ToCsClassName(parameTypes[i].FullName)})") : "";
                        callArgs.Add($"{anyReverseCast}arg{i}");
                    }

                    string paramsStr = ConcatViaComma(paramDefines);
                    string callArgsStr = ConcatViaComma(callArgs);


                    strbCs.AppendLine("\tpublic static " + ToCsMarshalClassName(func.ReturnType) + " " + (csClassnameIdentif + "_" + func.Name + "_Static") + "(" + paramsStr + ")");
                    strbCs.AppendLine("\t{");

                    if(func.ReturnType == typeof(void))
                    {
                        strbCs.AppendLine("\t\t" + type.FullName + "." + func.Name + "(" + callArgsStr + ");");
                    }
                    else
                    {
                        string anyIntCast = func.ReturnType.IsEnum ? "(int)" : "";
                        strbCs.AppendLine("\t\t" + "return " + anyIntCast + type.FullName + "." + func.Name + "(" + callArgsStr + ");");
                    }

                    strbCs.AppendLine("\t}");
                }
            }
            


            // *** Gizbox *** 

            //Giz类外声明部分 - 类静态方法      
            foreach(var func in staticFuncs)
            {
                List<string> paramDefines = new List<string>();

                var paramters = func.GetParameters();
                for (int i = 0; i < paramters.Length; ++i)
                {
                    paramDefines.Add($"{ToGizMarshalClassName(paramters[i].ParameterType)} arg{i}");
                }

                strbGiz.AppendLine("extern " + ToGizMarshalClassName(func.ReturnType) + " " + (gzClassnameIdentif + "_" + func.Name + "_Static") + "(" + ConcatViaComma(paramDefines) + ");");
            }

            //Giz类外声明部分 - 成员函数实现和成员字段/属性实现  
            if(type.IsValueType == false)
            {
                foreach(var f in fields)
                {
                    string gizTypename = ToGizMarshalClassName(f.FieldType);
                    strbGiz.AppendLine("extern " + gizTypename + " " + (gzClassnameIdentif + "_get_" + f.Name) + "(" + ToGizMarshalClassName(type)  + " " + gzClassnameIdentif.ToLower() + ");");
                    strbGiz.AppendLine("extern " + "void" + " " + (gzClassnameIdentif + "_set_" + f.Name) + "(" + ToGizMarshalClassName(type) + " " + gzClassnameIdentif.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
                foreach(var p in properties)
                {
                    string gizTypename = ToGizMarshalClassName(p.PropertyType);
                    if(p.CanRead)
                        strbGiz.AppendLine("extern " + gizTypename + " " + (gzClassnameIdentif + "_get_" + p.Name) + "(" + ToGizMarshalClassName(type) + " " + gzClassnameIdentif.ToLower() + ");");
                    if(p.CanWrite)
                        strbGiz.AppendLine("extern " + "void" + " " + (gzClassnameIdentif + "_set_" + p.Name) + "(" + ToGizMarshalClassName(type) + " " + gzClassnameIdentif.ToLower() + ", " + gizTypename + " " + "newv" + ");");
                }
            }

            foreach(var m in methods)
            {
                List<string> paramDefines = new List<string>();
                paramDefines.Add($"{ToGizMarshalClassName(type)} pthis");
                var parameters = m.GetParameters();
                for(int i = 0; i < parameters.Length; i++)
                {
                    paramDefines.Add($"{ToGizMarshalClassName(parameters[i].ParameterType)} arg{i}");
                }


                strbGiz.AppendLine("extern " + ToGizMarshalClassName(m.ReturnType) + " " + (gzClassnameIdentif + "_" + m.Name) + "(" + ConcatViaComma(paramDefines) + ");");
            }
            strbGiz.AppendLine();
            strbGiz.AppendLine();
            strbGiz.AppendLine();


            //Giz类内声明部分  
            string inhertStr = "";
            if(type.IsValueType == false && this.closure.Contains(type.BaseType))
            {
                inhertStr = "  :  " + ToGizMarshalClassName(type.BaseType);
                ;
            }
            strbGiz.AppendLine("class " + ToGizClassName(type.FullName) + inhertStr);
            strbGiz.AppendLine("{");


            //类类型
            if(type.IsValueType == false)
            {
                foreach(var f in fields)
                {
                    string gizTypename = ToGizMarshalClassName(f.FieldType);

                    strbGiz.AppendLine("\t" + gizTypename + " " + f.Name + "()");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + "return " + (gzClassnameIdentif + "_get_" + f.Name) + "(this);");
                    strbGiz.AppendLine("\t}");

                    strbGiz.AppendLine("\t" + "void " + f.Name + "(" + gizTypename + " " + "newv" + ")");
                    strbGiz.AppendLine("\t{");
                    strbGiz.AppendLine("\t\t" + (gzClassnameIdentif + "_set_" + f.Name) + "(this, " + "newv" + ");");
                    strbGiz.AppendLine("\t}");
                }

                foreach(var p in properties)
                {
                    string gizTypename = ToGizMarshalClassName(p.PropertyType);

                    if(p.CanRead)
                    {
                        strbGiz.AppendLine("\t" + gizTypename + " " + p.Name + "()");
                        strbGiz.AppendLine("\t{");
                        strbGiz.AppendLine("\t\t" + "return " + (gzClassnameIdentif + "_get_" + p.Name) + "(this);");
                        strbGiz.AppendLine("\t}");
                    }

                    if(p.CanWrite)
                    {
                        strbGiz.AppendLine("\t" + "void " + p.Name + "(" + gizTypename + " " + "newv" + ")");
                        strbGiz.AppendLine("\t{");
                        strbGiz.AppendLine("\t\t" + (gzClassnameIdentif + "_set_" + p.Name) + "(this, " + "newv" + ");");
                        strbGiz.AppendLine("\t}");
                    }
                }

            }
            //结构体类型  
            else
            {
                foreach(var f in fields)
                {
                    string gizTypename = ToGizMarshalClassName(f.FieldType);

                    strbGiz.AppendLine("\t" + gizTypename + " " + f.Name + " = " + GizTypeDefaultValueStr(gizTypename) + ";");
                }
            }



            foreach(var method in methods)
            {
                //List<string> paramDefines = new List<string>();
                //paramDefines.Add(ToGizMarshalClassName(type));
                //paramDefines.AddRange(method.GetParameters().Select(p => ToGizMarshalClassName(p.ParameterType)));

                //string paramsStr = "";
                //string argsStr = "";
                //int i = 0;
                //foreach(var p in paramDefines)
                //{
                //    if(i > 1)
                //        paramsStr += ", ";
                //    if(i != 0)
                //    {
                //        paramsStr += (p + " arg" + i.ToString());
                //    }


                //    if(i > 0)
                //        argsStr += ", ";
                //    if(i == 0)
                //    {
                //        argsStr += ("this");
                //    }
                //    else
                //    {
                //        argsStr += ("arg" + i.ToString());
                //    }


                //    i++;
                //}


                List<string> paramDefines = new List<string>();
                List<string> callArgs = new List<string>();
                callArgs.Add("this");                
                var parameters = method.GetParameters();
                for(int i = 0; i < parameters.Length; ++i)
                {
                    paramDefines.Add($"{ToGizMarshalClassName(parameters[i].ParameterType)} arg{i}");
                    callArgs.Add($"arg{i}");
                }

                strbGiz.AppendLine("\t" + ToGizMarshalClassName(method.ReturnType) + " " + method.Name + "(" + ConcatViaComma(paramDefines) + ")");
                strbGiz.AppendLine("\t{");


                if(ToGizMarshalClassName(method.ReturnType) == "void")
                {
                    strbGiz.AppendLine("\t\t" + (gzClassnameIdentif + "_" + method.Name) + "(" + ConcatViaComma(callArgs) + ");");
                }
                else
                {
                    strbGiz.AppendLine("\t\t" + "return " + (gzClassnameIdentif + "_" + method.Name) + "(" + ConcatViaComma(callArgs) + ");");
                }

                strbGiz.AppendLine("\t}");
            }


            strbGiz.AppendLine("}");
            strbGiz.AppendLine();

            //Giz类命名空间存放静态方法    
            strbGiz.AppendLine("namespace " + ToGizClassName(type.FullName));
            strbGiz.AppendLine("{");

            foreach(var staticFunc in staticFuncs)
            {
                List<Type> paramTypes = new List<Type>();
                paramTypes.AddRange(staticFunc.GetParameters().Select(p => p.ParameterType));

                List<string> paramsDefines = new List<string>();
                List<string> callArgs = new List<string>();
                for(int i = 0; i < paramTypes.Count; ++i)
                {
                    paramsDefines.Add($"{ToGizMarshalClassName(paramTypes[i])} arg{i}");

                    callArgs.Add($"arg{i}");
                }

                strbGiz.AppendLine("\t" + ToGizMarshalClassName(staticFunc.ReturnType) + " " + staticFunc.Name + "(" + ConcatViaComma(paramsDefines) + ")");
                strbGiz.AppendLine("\t{");


                if(ToGizMarshalClassName(staticFunc.ReturnType) == "void")
                {
                    strbGiz.AppendLine("\t\t" + (gzClassnameIdentif + "_" + staticFunc.Name + "_Static") + "(" + ConcatViaComma(callArgs) + ");");
                }
                else
                {
                    strbGiz.AppendLine("\t\t" + "return " + (gzClassnameIdentif + "_" + staticFunc.Name + "_Static") + "(" + ConcatViaComma(callArgs) + ");");
                }

                strbGiz.AppendLine("\t}");
            }

            strbGiz.AppendLine("}");
            strbGiz.AppendLine();
            strbGiz.AppendLine();
        }
        private void GenerateTypeAsEnum(Type type, StringBuilder strbCs, StringBuilder strbGiz)
        {
            strbGiz.AppendLine($"namespace {ToGizClassName(type.FullName)}");
            strbGiz.AppendLine("{");

            var enumValues = Enum.GetValues(type);
            foreach(var enumval in enumValues)
            {
                strbGiz.AppendLine($"\tconst int {enumval.ToString()} = {(int)enumval};");
            }

            strbGiz.AppendLine("}");
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


        private string ConcatViaComma(IEnumerable<string> elements)
        {
            StringBuilder sb = new StringBuilder();
            
            int i = 0;
            foreach(var e in elements)
            {
                if(i != 0)
                    sb.Append(", ");

                sb.Append(e);

                i++;
            }

            return sb.ToString();
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


        /// <summary>
        /// 标识符化的类名  
        /// </summary>
        private static string ToIdentifClassName(string classFullName)
        {
            return classFullName
                .Replace("+", "__")
                .Replace(".", "__");
        }

        /// <summary>
        /// Gizbox类名
        /// </summary>
        private static string ToGizClassName(string classFullName)
        {
            return classFullName
                .Replace("+", "::")
                .Replace(".", "::");
        }
        /// <summary>
        /// 替换嵌套类的"+"号
        /// </summary>
        private static string ToCsClassName(string classFullName)
        {
            return classFullName
                .Replace("+", ".");
        }

        /// <summary>
        /// Gizbox封送类型  
        /// </summary>
        private static string ToGizMarshalClassName(Type t)
        {
            if(t.IsEnum)
            {
                return "int";
            }

            switch (t.Name)
            {
                case "Void": return "void";
                case "Boolean": return "bool";
                case "Int32": return "int";
                case "Int64": return "long";
                case "Single": return "float";
                case "String": return "string";
                default:
                    {
                        return ToGizClassName(t.FullName);
                    }
            }
        }

        /// <summary>
        /// Cs封送类型  
        /// </summary>
        private static string ToCsMarshalClassName(Type t)
        {
            if(t.IsEnum)
            {
                return "int";
            }


            if (t == typeof(void))
            {
                return "void";
            }

            return ToCsClassName(t.FullName);
        }
    }
}
