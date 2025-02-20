using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Gizbox;
using Gizbox.IL;



namespace Gizbox.Interop.CSharp
{
    public class ObjectBinding
    {
        public long gizHeapPtr;
        public GizObject gizObj;
        public object csObj;
    }
    public static class InteropCSharp
    {

        //获取CS数值/对象的类型  
        public static string GetGizType(object csVal)
        {
            switch (csVal)
            {
                case bool i: return "bool";
                case int i: return "int";
                case float i: return "float";
                case double i: return "double";
                case string s: return "string";
                default:
                    {
                        return GetGizClassName(csVal.GetType().FullName);
                    }
            }
        }
        private static string GetGizType(Type t)
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

        public static string GetGizClassName(string csFullName)
        {
            return csFullName.Replace(".", "::").Replace("+", "::");
        }
    }
    public class InteropContext
    {
        public ScriptEngine.ScriptEngine engine;

        //外部调用类列表  
        public CoreGiz coreGiz;
        public List<Type> externCallTypes = new List<Type>();


        //绑定表  
        public List<ObjectBinding> bindingTable = new List<ObjectBinding>();
        private Dictionary<int, ObjectBinding> instanceidToBind = new Dictionary<int, ObjectBinding>();
        private Dictionary<object, ObjectBinding> csobjToBind = new Dictionary<object, ObjectBinding>();

        //类型缓存（从Giz类名到C#的类型）    
        private Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        //ctor  
        public InteropContext(ScriptEngine.ScriptEngine engineContext)
        {
            this.engine = engineContext;
            this.coreGiz = new CoreGiz() { engineContext = engineContext };
        }

        //外部调用  
        public Value ExternCall(string gizFuncName, List<Value> argments)
        {
            string funcName = gizFuncName.Replace("::", "__");

            object[] argumentsBoxed = argments.Select(a => Marshal2CSharp(a)).ToArray();

            //核心库查找
            var coreFuncInfo = FindFuncInCoreClass(funcName);
            if (coreFuncInfo != null)
            {
                var ret = coreFuncInfo.Invoke(coreGiz, argumentsBoxed);
                return Marshal2Giz(ret);
            }

            //外部查找    
            var funcInfo = FindFuncExternClass(funcName);
            if (funcInfo != null)
            {
                var ret = funcInfo.Invoke(null, argumentsBoxed);
                return Marshal2Giz(ret);
            }

            throw new Exception("找不到对应函数实现：" + funcName);
        }

        //绑定对象  
        public void NewBinding(long gzheapPtr, GizObject gObj, object csObj)
        {
            int id = gObj.instanceID;
            var bd = new ObjectBinding() { gizHeapPtr = gzheapPtr,  gizObj = gObj, csObj = csObj };
            bindingTable.Add(bd);
            instanceidToBind[id] = bd;
            csobjToBind[csObj] = bd;
        }

        //查询类型  
        public Type FindType(string gzTypeName)
        {
            if (typeCache.ContainsKey(gzTypeName))
            {
                return typeCache[gzTypeName];
            }
            else
            {
                string csTypeName = gzTypeName.Replace("::", ".");
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.FullName == csTypeName)
                        {
                            typeCache[gzTypeName] = t;
                            return t;
                        }
                    }
                }

                throw new Exception("没有在所有Assembly中找到类：" + csTypeName);
            }
        }

        //查询函数  
        public MethodInfo FindFuncInCoreClass(string funcName)
        {
            var func = typeof(CoreGiz).GetMethods(System.Reflection.BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == funcName);
            if (func != null)
            {
                return func;
            }
            return null;
        }
        public MethodInfo FindFuncExternClass(string funcName)
        {
            foreach (var t in externCallTypes)
            {
                var func = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == funcName);
                if (func != null)
                {
                    return func;
                }
            }
            return null;
        }

        //数据封送
        public object Marshal2CSharp(Value gzVal)
        {
            if (gzVal.Type == GizType.Void)
            {
                return null;
            }
            else if (gzVal.Type == GizType.ObjectRef)
            {
                var gizobj =  ((GizObject)engine.DeReference(gzVal.AsPtr));

                //CS引用类型  
                var cstype = FindType(gizobj.truetype);
                if (cstype.IsValueType == false)
                {
                    //存在已绑定对象  
                    if (instanceidToBind.ContainsKey(gizobj.instanceID))
                    {
                        return instanceidToBind[gizobj.instanceID].csObj;
                    }
                    //没有绑定的对象-新建  
                    else
                    {
                        var newobj = Activator.CreateInstance(FindType(gizobj.truetype));
                        NewBinding(gzVal.AsPtr, gizobj, newobj);
                        return newobj;
                    }
                }
                //CS值类型  
                else
                {
                    object csVal = Activator.CreateInstance(cstype);
                    foreach(var field in cstype.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if(gizobj.fields.ContainsKey(field.Name))
                        {
                            field.SetValue(csVal, Marshal2CSharp(gizobj.fields[field.Name]));
                        }
                    }
                    return csVal;
                }
            }
            else if (gzVal.Type == GizType.ArrayRef)
            {
                Value[] gzarr = engine.DeReference(gzVal.AsPtr) as Value[];
                object[] csArr = new object[gzarr.Length]; 
                for (int i = 0; i < gzarr.Length; ++i)
                {
                    csArr[i] = Marshal2CSharp(gzarr[i]);
                }
                return csArr;
            }
            else
            {
                return engine.BoxGizValue2CsObject(gzVal);
            }
        }

        //数据封送
        public Value Marshal2Giz(object csVal)
        {
            switch (csVal)
            {
                case bool b:
                case int i:
                case float f:
                case double d:
                case char c:
                case string str:
                    return engine.UnBoxCsObject2GizValue(csVal);
                default: break;
            }

            if (csVal == null)
            {
                return Value.Void;
            }

            Type cstype = csVal.GetType();

            if (cstype.IsArray)
            {
                System.Array csArr = (csVal as System.Array);
                Value[] gzarr = new Value[csArr.Length];
                for (int i = 0; i < csArr.Length; ++i)
                {
                    gzarr[i] = Marshal2Giz(csArr.GetValue(i));
                }

                //return Value.FromArray(gzarr); //TODO: FromArray  
                return Value.Void;
            }
            else
            {
                //引用类型  
                if(cstype.IsValueType == false)
                {
                    if (csobjToBind.ContainsKey(csVal))
                    {
                        var bind = csobjToBind[csVal];
                        return Value.FromGizObjectPtr(bind.gizHeapPtr);
                    }
                    else
                    {
                        var newgizobj = new GizObject(InteropCSharp. GetGizClassName(cstype.FullName), engine);
                        long newptr = engine.heap.Alloc(newgizobj);
                        NewBinding(newptr, newgizobj, csVal);
                        return Value.FromGizObjectPtr(newptr); 
                    }
                }
                //值类型  
                else
                {
                    var newgizobj = new GizObject(InteropCSharp.GetGizClassName(cstype.FullName), engine);
                    long newptr = engine.heap.Alloc(newgizobj);
                    //结构体无法绑定 - 需要Marshal数值    
                    foreach (var field in cstype.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        newgizobj.fields[field.Name] = Marshal2Giz(field.GetValue(csVal));
                    }
                    return Value.FromGizObjectPtr(newptr);
                }
            }

            throw new Exception("封送错误");
        }


        //配置  
        public void ConfigExternCallClasses(params Type[] classes)
        {
            externCallTypes.AddRange(classes);
        }
        public void ConfigTypeCache(params Type[] types)
        {
            foreach(var t in types)
            {
                typeCache.Add(t.Name , t);
            }
        }
    }

    public class CoreGiz
    {
        public ScriptEngine.ScriptEngine engineContext;

        public void Core__GC__Collect()
        {
            engineContext.GCCollect();
        }
        public void Core__GC__Enable()
        {
            engineContext.GCEnable(true);
        }
        public void Core__GC__Disable()
        {
            engineContext.GCEnable(false);
        }
    }

}