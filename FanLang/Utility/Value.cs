using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace FanLang
{
    //Fan值类型    
    [StructLayout(LayoutKind.Explicit)]
    public struct Value
    {
        // ---------- DATA --------------
        [FieldOffset(0)]
        private FanType type;

        [FieldOffset(4)]
        public bool AsBool;

        [FieldOffset(4)]
        public int AsInt;

        [FieldOffset(4)]
        public float AsFloat;

        [FieldOffset(4 + 16)]
        public object AsObject;


        // ---------- INTERFACE --------------

        public FanType Type => this.type;

        public bool IsVoid => (this.type == FanType.Void);

        public static Value Void => new Value() { type = FanType.Void };


        // ---------- ASSIGN --------------
        public static implicit operator Value(int v)
        {
            return new Value() { type = FanType.Int, AsInt = v };
        }
        public static implicit operator Value(float v)
        {
            return new Value() { type = FanType.Float, AsFloat = v };
        }
        public static implicit operator Value(bool v)
        {
            return new Value() { type = FanType.Bool, AsBool = v };
        }
        public static implicit operator Value(string str)
        {
            if (str != null)
            {
                return new Value() { type = FanType.String, AsObject = str };
            }
            else
            {
                return Void;
            }
        }
        public static implicit operator Value(FanObject obj)
        {
            if (obj != null)
            {
                return new Value() { type = FanType.FanObject, AsObject = obj };
            }
            else
            {
                return Void;
            }
        }

        public static Value Array(Value[] array)
        {
            if(array != null)
            {
                return new Value() { type = FanType.FanArray, AsObject = array };
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
                case FanType.Bool: throw new Exception("运算类型错误!");
                case FanType.Int: return v1.AsInt + v2.AsInt;
                case FanType.Float: return v1.AsFloat + v2.AsFloat;
                case FanType.String: return (string)v1.AsObject + (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator -(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt - v2.AsInt;
                case FanType.Float: return v1.AsFloat - v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator *(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt * v2.AsInt;
                case FanType.Float: return v1.AsFloat * v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator /(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt / v2.AsInt;
                case FanType.Float: return v1.AsFloat / v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator %(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt % v2.AsInt;
                case FanType.Float: return v1.AsFloat % v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator >(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt > v2.AsInt;
                case FanType.Float: return v1.AsFloat > v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator <(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt < v2.AsInt;
                case FanType.Float: return v1.AsFloat < v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator <=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt <= v2.AsInt;
                case FanType.Float: return v1.AsFloat <= v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator >=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Int: return v1.AsInt >= v2.AsInt;
                case FanType.Float: return v1.AsFloat >= v2.AsFloat;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator ==(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Void: return v1.type == v2.type;
                case FanType.Bool: return v1.AsBool == v2.AsBool;
                case FanType.Int: return v1.AsInt == v2.AsInt;
                case FanType.Float: return v1.AsFloat == v2.AsFloat;
                case FanType.String: return (string)v1.AsObject == (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }
        public static Value operator !=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new Exception("运算类型错误!");
            switch (v1.type)
            {
                case FanType.Bool: return v1.AsBool != v2.AsBool;
                case FanType.Int: return v1.AsInt != v2.AsInt;
                case FanType.Float: return v1.AsFloat != v2.AsFloat;
                case FanType.String: return (string)v1.AsObject != (string)v2.AsObject;
                default: throw new Exception("运算类型错误!");
            }
        }

        // ---------- BOX --------------
        public object Box()
        {
            switch (this.type)
            {
                case FanType.Void:
                    return null;
                case FanType.Int:
                    return AsInt;
                case FanType.Float:
                    return AsFloat;
                case FanType.Bool:
                    return AsBool;
                case FanType.String:
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
                case FanObject s: return (FanObject)obj;
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
                case FanType.Void: return "Void";
                case FanType.Bool: return AsBool.ToString();
                case FanType.Int: return AsInt.ToString();
                case FanType.Float: return AsFloat.ToString();
                case FanType.String: return (string)AsObject;
                case FanType.FanObject: return ((FanObject)AsObject).ToString();
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

    // Fan对象  
    public class FanObject
    {
        private static int currentMaxId = 0;

        public int instanceID = 0;
        public Dictionary<string, Value> fields = new Dictionary<string, Value>();

        public FanObject()
        {
            this.instanceID = currentMaxId++;
        }
        public override string ToString()
        {
            return "FanObect(instanceID:" + this.instanceID + ")";
        }
    }




}
