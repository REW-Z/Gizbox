using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using FanLang;
using FanLang.IL;



namespace FanLang.Interop.CSharp
{
    public class Car
    {
        public string name;

        public virtual void MoveTo(string text)
        {
            Console.WriteLine("car " + this.name + " move to " + text + ". (slow)");
        }
    }
    public class RaceCar : Car
    {
        public override void MoveTo(string text)
        {
            Console.WriteLine("race car " + this.name + " move to " + text + ". (fast)");
        }
    }




    public class ObjectBinding
    {
        public FanObject fanObj;
        public object csObj;
    }

    public class InteropContext
    {
        public ScriptEngine.ScriptEngine engine;

        //类型缓存    
        private Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        //外部调用类列表  
        public List<Type> externCallTypes = new List<Type>() { typeof(ExterCalls) };

        //绑定表  
        public List<ObjectBinding> bindingTable = new List<ObjectBinding>();
        private Dictionary<int, ObjectBinding> idToBind = new Dictionary<int, ObjectBinding>();
        private Dictionary<object, ObjectBinding> objToBind = new Dictionary<object, ObjectBinding>();


        public InteropContext(ScriptEngine.ScriptEngine engineContext)
        {
            this.engine = engineContext;
        }

        public void NewBinding(FanObject fObj, object csObj)
        {
            int id = fObj.instanceID;
            var bd = new ObjectBinding() { fanObj = fObj, csObj = csObj };
            bindingTable.Add(bd);
            idToBind[id] = bd;
            objToBind[csObj] = bd;
        }

        public Type FindType(string typename)
        {
            if (typeCache.ContainsKey(typename))
            {
                return typeCache[typename];
            }
            else
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == typename)
                        {
                            typeCache[typename] = t;
                            return t;
                        }
                    }
                }

                throw new Exception("没有在所有Assembly中找到类：" + typename);
            }
        }

        public object Marshal2CSharp(Value val)
        {
            if (val.Type == FanType.FanObject)
            {
                var fanobj = ((FanObject)val.AsObject);

                //存在已绑定对象  
                if (idToBind.ContainsKey(fanobj.instanceID))
                {
                    return idToBind[fanobj.instanceID].csObj;
                }
                //没有绑定的对象-新建  
                else
                {
                    var newobj = Activator.CreateInstance(FindType(fanobj.truetype));
                    NewBinding(fanobj, newobj);
                    return newobj;
                }
            }
            else if (val.Type == FanType.FanArray)
            {
                return val.Box();
                //TODO:对象数组...
            }
            else
            {
                return val.Box();
            }
        }

        public Value Marshal2Fan(object val)
        {
            if (val == null)
            {
                return Value.Void;
            }

            switch (val)
            {
                case string str:
                case int i:
                case float f:
                case bool b:
                    return Value.UnBox(val);
                default: break;
            }

            Type type = val.GetType();

            if (type.IsArray)
            {
                //TODO:对象数组处理  
                return Value.UnBox(val);
            }
            else
            {
                if (objToBind.ContainsKey(val))
                {
                    return objToBind[val].fanObj;
                }
                else
                {
                    var newfanobj = new FanObject(type.Name, engine);
                    NewBinding(newfanobj, val);
                    return newfanobj;
                }
            }

            throw new Exception("封送错误");
        }


        public MethodInfo QueryFunc(string funcName)
        {
            foreach(var t in externCallTypes)
            {
                var func = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == funcName);
                if(func != null)
                {
                    return func;
                }
            }
            return null;
        }

        public void ConfigExternCallClasses(params Type[] classes)
        {
            externCallTypes.AddRange(classes);
        }
        public void ConfigTypeCache(params Type[] types)
        {
        }
    }

    public static class ExterCalls
    {
        public static void Log(string text)
        {
            Console.WriteLine("FanLang >>>" + text);
        }


        public static String Car_get_name(Car obj)
        {
            return obj.name;
        }
        public static void Car_set_name(Car obj, String newv)
        {
            obj.name = newv;
        }
        public static void Car_MoveTo(Car arg0, String arg1)
        {
            arg0.MoveTo(arg1);
        }
        public static void RaceCar_MoveTo(RaceCar arg0, String arg1)
        {
            arg0.MoveTo(arg1);
        }
    }
}