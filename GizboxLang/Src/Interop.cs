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
        public GizObject gizObj;
        public object csObj;
    }

    public class InteropContext
    {
        public ScriptEngine.ScriptEngine engine;

        //外部调用类列表  
        public List<Type> externCallTypes = new List<Type>() 
        {
            typeof(ExterCallPreset) 
        };



        //绑定表  
        public List<ObjectBinding> bindingTable = new List<ObjectBinding>();
        private Dictionary<int, ObjectBinding> idToBind = new Dictionary<int, ObjectBinding>();
        private Dictionary<object, ObjectBinding> objToBind = new Dictionary<object, ObjectBinding>();

        //类型缓存（从Giz类名到C#的类型）    
        private Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        //ctor  
        public InteropContext(ScriptEngine.ScriptEngine engineContext)
        {
            this.engine = engineContext;
        }

        //外部调用  
        public Value ExternCall(string gizFuncName, List<Value> argments)
        {
            string funcName = gizFuncName.Replace("::", "__");

            object[] argumentsBoxed = argments.Select(a => Marshal2CSharp(a)).ToArray();

            var funcInfo = FindFunc(funcName);
            if (funcInfo != null)
            {
                var ret = funcInfo.Invoke(null, argumentsBoxed);
                return Marshal2Giz(ret);
            }
            else
            {
                throw new Exception("找不到对应函数实现：" + funcName);
            }
        }

        //绑定对象  
        public void NewBinding(GizObject gObj, object csObj)
        {
            int id = gObj.instanceID;
            var bd = new ObjectBinding() { gizObj = gObj, csObj = csObj };
            bindingTable.Add(bd);
            idToBind[id] = bd;
            objToBind[csObj] = bd;
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
        public MethodInfo FindFunc(string funcName)
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
            if (gzVal.Type == GizType.GizObject)
            {
                var gizobj = ((GizObject)gzVal.AsObject);

                //CS引用类型  
                var cstype = FindType(gizobj.truetype);
                if (cstype.IsValueType == false)
                {
                    //存在已绑定对象  
                    if (idToBind.ContainsKey(gizobj.instanceID))
                    {
                        return idToBind[gizobj.instanceID].csObj;
                    }
                    //没有绑定的对象-新建  
                    else
                    {
                        var newobj = Activator.CreateInstance(FindType(gizobj.truetype));
                        NewBinding(gizobj, newobj);
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
            else if (gzVal.Type == GizType.GizArray)
            {
                Value[] gzarr = (gzVal.AsObject) as Value[];
                object[] csArr = new object[gzarr.Length]; 
                for (int i = 0; i < gzarr.Length; ++i)
                {
                    csArr[i] = Marshal2CSharp(gzarr[i]);
                }
                return csArr;
            }
            else
            {
                return engine.Box(gzVal);
            }
        }

        //数据封送
        public Value Marshal2Giz(object csVal)
        {
            switch (csVal)
            {
                case string str:
                case int i:
                case float f:
                case bool b:
                    return engine.UnBox(csVal);
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
                    if (objToBind.ContainsKey(csVal))
                    {
                        //return objToBind[csVal].gizObj; //TODO: ???
                        return Value.Void;
                    }
                    else
                    {
                        var newgizobj = new GizObject(cstype.Name, engine);
                        NewBinding(newgizobj, csVal);
                        //return newgizobj; //TODO: ???????
                        return Value.Void;
                    }
                }
                //值类型  
                else
                {
                    var newgizobj = new GizObject(cstype.Name, engine);
                    //结构体无法绑定 - 需要Marshal数值    
                    foreach (var field in cstype.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        newgizobj.fields[field.Name] = Marshal2Giz(field.GetValue(csVal));   
                    }
                    //return newgizobj; //TODO: ????????????
                    return Value.Void;
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

    public static class ExterCallPreset
    {
        public static void Console__Log(string text)
        {
            Console.WriteLine("Gizbox >>>" + text);
        }

        public static void SetFieldValue(object obj, string fieldName, object val)
        {
            obj.GetType().GetField(fieldName).SetValue(obj, val);
        }
        public static object GetFieldValue(object obj, string fieldName)
        {
            return obj.GetType().GetField(fieldName).GetValue(obj);
        }
    }

}