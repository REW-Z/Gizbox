using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Gizbox
{
    public enum GizType : byte
    {
        Void,

        //ValueType  
        Int,
        Float,
        Double,
        Bool,
        Char,

        //RefType  
        StringRef,
        ObjectRef,
        ArrayRef,
    }




    //Giz值类型    
    [StructLayout(LayoutKind.Explicit)]
    [Serializable]
    [DataContract]
    public struct Value
    {
        // ---------- DATA --------------
        [DataMember]
        [FieldOffset(0)]
        private GizType type;

        [DataMember]
        [FieldOffset(4)]
        public bool AsBool;

        [DataMember]
        [FieldOffset(4)]
        public int AsInt;

        [DataMember]
        [FieldOffset(4)]
        public float AsFloat;

        [DataMember]
        [FieldOffset(4)]
        public double AsDouble;

        [DataMember]
        [FieldOffset(4)]
        public float AsChar;

        [DataMember]
        [FieldOffset(4)]
        public long AsPtr;




        // ---------- INTERFACE --------------

        public GizType Type => this.type;

        public bool IsRefType => (this.type == GizType.ObjectRef || this.type == GizType.ArrayRef || this.type == GizType.StringRef);
        
        public bool IsValueType => (IsRefType == false && IsVoid == false);

        public bool IsVoid => (this.type == GizType.Void);

        public static Value Void => new Value() { type = GizType.Void };

        public static Value NULL => new Value() { type = GizType.ObjectRef, AsPtr = 0 };


        // ---------- ASSIGN --------------
        public static implicit operator Value(int v)
        {
            return new Value() { type = GizType.Int, AsInt = v };
        }
        public static implicit operator Value(float v)
        {
            return new Value() { type = GizType.Float, AsFloat = v };
        }
        public static implicit operator Value(double v)
        {
            return new Value() { type = GizType.Double, AsDouble = v };
        }
        public static implicit operator Value(bool v)
        {
            return new Value() { type = GizType.Bool, AsBool = v };
        }
        public static implicit operator Value(char v)
        {
            return new Value() { type = GizType.Char, AsChar = v };
        }

        public static Value FromConstStringPtr(long ptr)
        {
            return new Value() { type = GizType.StringRef, AsPtr = (-ptr) - 1 };
        }
        public static Value FromStringPtr(long ptr)
        {
            return new Value() { type = GizType.StringRef, AsPtr = ptr };
        }

        public static Value FromGizObjectPtr(long ptr)
        {
            return new Value() { type = GizType.ObjectRef, AsPtr = ptr };
        }

        public static Value FromArrayPtr(long arrayPtr)
        {
            return new Value() { type = GizType.ArrayRef, AsPtr = arrayPtr };
        }


        // ---------- OPERATOR --------------
        public static Value operator +(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError, v1.type + " + " + v2.type);
            switch (v1.type)
            {
                case GizType.Bool: throw new GizboxException(ExceptioName.OperationTypeError);
                case GizType.Int: return v1.AsInt + v2.AsInt;
                case GizType.Float: return v1.AsFloat + v2.AsFloat;
                //case GizType.String: return (string)v1.AsObject + (string)v2.AsObject;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator -(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt - v2.AsInt;
                case GizType.Float: return v1.AsFloat - v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator *(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt * v2.AsInt;
                case GizType.Float: return v1.AsFloat * v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator /(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt / v2.AsInt;
                case GizType.Float: return v1.AsFloat / v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator %(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt % v2.AsInt;
                case GizType.Float: return v1.AsFloat % v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator >(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt > v2.AsInt;
                case GizType.Float: return v1.AsFloat > v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator <(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt < v2.AsInt;
                case GizType.Float: return v1.AsFloat < v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator <=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt <= v2.AsInt;
                case GizType.Float: return v1.AsFloat <= v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator >=(Value v1, Value v2)
        {
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Int: return v1.AsInt >= v2.AsInt;
                case GizType.Float: return v1.AsFloat >= v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator ==(Value v1, Value v2)
        {
            //引用类型指针比较  
            if(v1.IsRefType && v2.IsRefType) 
                return v1.AsPtr == v2.AsPtr;

            //值类型比较  
            if (v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Void: return v1.type == v2.type;
                case GizType.Bool: return v1.AsBool == v2.AsBool;
                case GizType.Int: return v1.AsInt == v2.AsInt;
                case GizType.Float: return v1.AsFloat == v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }
        public static Value operator !=(Value v1, Value v2)
        {
            //引用类型指针比较  
            if(v1.IsRefType && v2.IsRefType)
                return v1.AsPtr != v2.AsPtr;

            //值类型比较  
            if(v1.type != v2.type) throw new GizboxException(ExceptioName.OperationTypeError);
            switch (v1.type)
            {
                case GizType.Bool: return v1.AsBool != v2.AsBool;
                case GizType.Int: return v1.AsInt != v2.AsInt;
                case GizType.Float: return v1.AsFloat != v2.AsFloat;
                default: throw new GizboxException(ExceptioName.OperationTypeError);
            }
        }



        // ----------TO STRING ----------
        public override string ToString()
        {
            switch (this.type)
            {
                case GizType.Void: return "";
                case GizType.Bool: return AsBool.ToString();
                case GizType.Int: return AsInt.ToString();
                case GizType.Float: return AsFloat.ToString();
                case GizType.StringRef:
                    {
                        return $"GizString(ptr:{this.AsPtr})";
                    }
                case GizType.ArrayRef:
                    {
                        return $"GizArray(ptr:{this.AsPtr})";
                    }
                case GizType.ObjectRef:
                    {
                        return $"GizObject(ptr:{this.AsPtr})";
                    }
                default:
                    {
                        return "???";
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

        public GizObject(string gzclassname, ScriptEngine.ScriptEngine engineContext)
        {
            this.instanceID = currentMaxId++;
            this.truetype = gzclassname;
            var classRec = engineContext.mainUnit.QueryClass(gzclassname); if (classRec == null || classRec.envPtr == null) throw new Exception("找不到" + gzclassname + "的记录和符号表");
            this.classEnv = engineContext.mainUnit.QueryClass(gzclassname).envPtr;
            if (engineContext.mainUnit.QueryVTable(gzclassname) == null) throw new Exception("找不到虚函数表：" + gzclassname);
            this.vtable = engineContext.mainUnit.QueryVTable(gzclassname);
        }
        public GizObject(string gzclassname, SymbolTable classEnv, VTable vtable)
        {
            this.instanceID = currentMaxId++;
            this.truetype = gzclassname;
            this.classEnv = classEnv;
            this.vtable = vtable;
        }
        public override string ToString()
        {
            return "GizObect(instanceID:" + this.instanceID + "  type:" + truetype + ")";
        }
    }



    public enum GizType2
    {
        Void,

        IntRef,
        FloatRef,
        DoubleRef,
        BoolRef,
        CharRef,

        StringRef,
        ObjectRef,
        ArrayRef,
    }

    public struct ValueV2
    {
        public GizType2 type;
        public long ptr;
    }
}
