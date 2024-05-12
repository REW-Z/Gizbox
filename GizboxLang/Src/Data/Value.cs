using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Gizbox
{
    public enum GizType : byte
    {
        Void,
        Int,
        Float,
        Bool,
        String,
        GizObject,
        GizArray,
    }




    //Giz值类型    
    [StructLayout(LayoutKind.Explicit)]
    public struct Value
    {
        // ---------- DATA --------------
        [FieldOffset(0)]
        private GizType type;

        [FieldOffset(4)]
        public bool AsBool;

        [FieldOffset(4)]
        public int AsInt;

        [FieldOffset(4)]
        public float AsFloat;

        [FieldOffset(4 + 16)]
        public object AsObject;


        // ---------- INTERFACE --------------

        public GizType Type => this.type;

        public bool IsVoid => (this.type == GizType.Void);

        public static Value Void => new Value() { type = GizType.Void };


        // ---------- ASSIGN --------------
        public static implicit operator Value(int v)
        {
            return new Value() { type = GizType.Int, AsInt = v };
        }
        public static implicit operator Value(float v)
        {
            return new Value() { type = GizType.Float, AsFloat = v };
        }
        public static implicit operator Value(bool v)
        {
            return new Value() { type = GizType.Bool, AsBool = v };
        }
        public static implicit operator Value(string str)
        {
            if (str != null)
            {
                return new Value() { type = GizType.String, AsObject = str };
            }
            else
            {
                return Void;
            }
        }
        public static implicit operator Value(GizObject obj)
        {
            if (obj != null)
            {
                return new Value() { type = GizType.GizObject, AsObject = obj };
            }
            else
            {
                return Void;
            }
        }


        public static Value FromArray(Value[] array)
        {
            if(array != null)
            {
                return new Value() { type = GizType.GizArray, AsObject = array };
            }
            else
            {
                return Void;
            }
        }


        // ---------- OPERATOR --------------
        public static Value operator +(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误! " + v1.type + " + " + v2.type);
            switch (v1.type)
            {
                case GizType.Bool: throw new Exception("运算类型错误!");
                case GizType.Int: return v1.AsInt + v2.AsInt;
                case GizType.Float: return v1.AsFloat + v2.AsFloat;
                case GizType.String: return (string)v1.AsObject + (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator -(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt - v2.AsInt;
                case GizType.Float: return v1.AsFloat - v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator *(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt * v2.AsInt;
                case GizType.Float: return v1.AsFloat * v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator /(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt / v2.AsInt;
                case GizType.Float: return v1.AsFloat / v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator %(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt % v2.AsInt;
                case GizType.Float: return v1.AsFloat % v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator >(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt > v2.AsInt;
                case GizType.Float: return v1.AsFloat > v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator <(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt < v2.AsInt;
                case GizType.Float: return v1.AsFloat < v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator <=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt <= v2.AsInt;
                case GizType.Float: return v1.AsFloat <= v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator >=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt >= v2.AsInt;
                case GizType.Float: return v1.AsFloat >= v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator ==(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Void: return v1.type == v2.type;
                case GizType.Bool: return v1.AsBool == v2.AsBool;
                case GizType.Int: return v1.AsInt == v2.AsInt;
                case GizType.Float: return v1.AsFloat == v2.AsFloat;
                case GizType.String: return (string)v1.AsObject == (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator !=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case GizType.Bool: return v1.AsBool != v2.AsBool;
                case GizType.Int: return v1.AsInt != v2.AsInt;
                case GizType.Float: return v1.AsFloat != v2.AsFloat;
                case GizType.String: return (string)v1.AsObject != (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }

        // ---------- BOX --------------
        public object Box()
        {
            switch (this.type)
            {
                case GizType.Void:
                    return null;
                case GizType.Int:
                    return AsInt;
                case GizType.Float:
                    return AsFloat;
                case GizType.Bool:
                    return AsBool;
                case GizType.String:
                    return AsObject;
                default:
                    return null;
            }
        }
        public static Value UnBox(object obj)
        {
            switch (obj)
            {
                case int i: return (int)obj;
                case bool b: return (bool)obj;
                case float f: return (float)obj;
                case string s: return (string)obj;
                case GizObject s: return (GizObject)obj;
                default:
                    {
                        if (obj == null) return Value.Void;
                        else throw new Exception();
                    }
                    break;
            }
        }

        // ----------TO STRING ----------
        public override string ToString()
        {
            switch (this.type)
            {
                case GizType.Void: return "Void";
                case GizType.Bool: return AsBool.ToString();
                case GizType.Int: return AsInt.ToString();
                case GizType.Float: return AsFloat.ToString();
                case GizType.String: return (string)AsObject;
                case GizType.GizObject: return ((GizObject)AsObject).ToString();
                default:
                    {
                        if (AsObject != null)
                        {
                            return "(Unknown Object)";
                        }
                        else
                        {
                            return "(Null Object)";
                        }
                    };
            }
        }
    }

    // Giz对象  
    public class GizObject
    {
        private static int currentMaxId = 0;

        // ***** Header *****
        public int instanceID = 0;
        public string truetype;
        public SymbolTable classEnv;
        public VTable vtable;

        // ***** Data *****
        public Dictionary<string, Value> fields = new Dictionary<string, Value>();

        public GizObject(string classname, ScriptEngine.ScriptEngine engineContext)
        {
            this.instanceID = currentMaxId++;
            this.truetype = classname;
            this.classEnv = engineContext.mainUnit.QueryClass(classname).envPtr;
            this.vtable = engineContext.mainUnit.vtables[classname];
        }
        public GizObject(string classname, SymbolTable classEnv, VTable vtable)
        {
            this.instanceID = currentMaxId++;
            this.truetype = classname;
            this.classEnv = classEnv;
            this.vtable = vtable;
        }
        public override string ToString()
        {
            return "GizObect(instanceID:" + this.instanceID + ")";
        }
    }




}
