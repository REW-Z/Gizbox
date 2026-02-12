using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Gizbox
{
    public static class Utils
    {
        public static string Mangle(string funcname, params string[] paramTypes)
        {
            string result = funcname + "@";
            foreach (var paramType in paramTypes)
            {
                result += '_';
                result += paramType;
            }
            return result;
        }

        public static string ToExternFuncName(string gizFullName)
        {
            return gizFullName.Replace("::", "__");
        }

        public static string MangleTypeName(string baseName, IEnumerable<string> argTypes)
        {
            if (argTypes == null)
                return baseName;

            var args = argTypes.Where(a => !string.IsNullOrWhiteSpace(a)).Select(SanitizeTypeName).ToArray();
            if (args.Length == 0)
                return baseName;

            return baseName + "_" + string.Join("_", args);
        }

        private static string SanitizeTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            return name.Replace("::", "_")
                .Replace("[]", "Arr")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace(" ", "");
        }

        public static GType CtorType(SymbolTable.Record classRec)
        {
            return GType.Parse($"{classRec.name} => void");
        }

        public static GType DtorType(SymbolTable.Record classRec)
        {
            return GType.Parse($"{classRec.name} => void");
        }
    }

    public static class DictionaryExtensions
    {
        public static Tv GetOrCreate<Tk, Tv>(this Dictionary<Tk, Tv> dict, Tk key)
        {
            if(dict.TryGetValue(key, out var val) == true)
            {
                return val;
            }
            else
            {
                dict[key] = Activator.CreateInstance<Tv>();
                return dict[key];
            }
        }
    }

    public static class StringExtensions
    {
        /// <summary>
        /// 字符串中字符串的起止idx
        /// </summary>
        public static (int, int) GetSubStringIndex(this string _this, string subStr)
        {
            if(_this.Contains(subStr) == false)
                throw new ArgumentException("Input string does not contain the specified substring.");

            for(int i = 0; i <= _this.Length - subStr.Length; i++)
            {
                for(int j = 0; j < subStr.Length; j++)
                {
                    if(_this[i + j] != subStr[j])
                        break;
                    if(j == subStr.Length - 1)
                        return (i, i + subStr.Length - 1);
                }
            }

            throw new Exception("Unexpected error: substring not found after initial check.");
        }

        /// <summary>
        /// 通过字符串分割字符串
        /// </summary>
        public static string[] SplitViaStr(this string _this, string splitStr)
        {
            List<string> parts = new List<string>();
            int start = 0;
            int index;
            while((index = _this.IndexOf(splitStr, start)) != -1)
            {
                parts.Add(_this.Substring(start, index - start));
                start = index + splitStr.Length;
            }
            parts.Add(_this.Substring(start));
            return parts.ToArray();
        }
    }



    public class MemUtility
    {
        //类成员布局  
        public static long ClassLayout(int headerSize, (int size, int align)[] fields, out long[] allocAddrs)
        {
            //headerSize是对象头信息，一般是虚函数表指针等信息  
            if(fields == null || fields.Length == 0)
            {
                allocAddrs = new long[0];
                return headerSize;
            }

            allocAddrs = new long[fields.Length];
            long cursor = headerSize;

            for(int i = 0; i < fields.Length; i++)
            {
                int fsize = fields[i].size;
                int falign = fields[i].align <= 0 ? 1 : fields[i].align;

                if((falign & (falign - 1)) != 0)
                    throw new ArgumentException("Alignment must be power of two");

                cursor = AlignUp(cursor, falign);
                allocAddrs[i] = cursor;
                cursor += fsize;
            }

            //返回类的size
            var classSize = cursor;
            return classSize;
        }

        //局部变量布局（栈帧向低地址增长）(不负责对齐)  
        public static long LocalVarLayout((int size, int align)[] fields, out long[] allocAddrs)
        {
            if(fields == null || fields.Length == 0)
            {
                allocAddrs = new long[0];
                return 0;
            }

            allocAddrs = new long[fields.Length];

            long cursor = 0;
            for(int i = 0; i < fields.Length; i++)
            {
                int fsize = fields[i].size;
                int falign = fields[i].align <= 0 ? 1 : fields[i].align;

                if((falign & (falign - 1)) != 0)
                    throw new ArgumentException("Alignment must be power of two");

                // 预留空间后按对齐向下取整，得到变量起始地址（相对 RBP 的负偏移）
                long next = cursor - fsize;
                long addr = AlignDown(next, falign);

                allocAddrs[i] = addr;
                cursor = addr;
            }

            // 返回 RBP 以下总大小
            long frameSize = -cursor;
            return frameSize;
        }

        private static long AlignUp(long value, int alignment)
        {
            if(alignment <= 0 || (alignment & (alignment - 1)) != 0)
                throw new ArgumentException("Alignment must be power of two");

            long mask = alignment - 1;
            return (value + mask) & ~mask;
        }
        // 内存向下对齐计算函数
        private static long AlignDown(long value, int alignment)
        {
            if(alignment <= 0 || (alignment & (alignment - 1)) != 0)
                throw new ArgumentException("Alignment must be power of two");

            // 1. (alignment - 1): 获取低位掩码
            //    例如: alignment=8 (1000)，则 (alignment-1)=7 (0111)
            // 2. ~(alignment - 1): 取反获得高位掩码
            //    例如: ~7 = ~(0111) = ...11111000
            // 3. value & ~(alignment - 1): 按位与操作清除低位，保留高位
            return value & ~(alignment - 1);
        }



        public static int GetGizboxTypeAlignment(string typeExpression)
        {
            // 这里可以根据Gizbox的类型系统定义来计算对齐
            // 假设所有基本类型的对齐都是1字节
            switch (typeExpression)
            {
                case "bool":
                case "byte":
                    return 1;
                case "short":
                case "char":
                    return 2;
                case "int":
                case "float":
                    return 4;
                case "long":
                case "double":
                    return 8;
                case "string":
                    return 8;//指针
                default:
                    return 8;//引用类型指针
            }
        }

        public static int GetGizboxTypeSize(string typeExpression)
        {
            switch(typeExpression)
            {
                case "bool":
                case "byte":
                    return 1;
                case "short":
                case "char":
                    return 2;
                case "int":
                case "float":
                    return 4;
                case "long":
                case "double":
                    return 8;
                case "string":
                    return 8;//指针
                default:
                    return 8;//引用类型指针
            }
        }


        // 类型大小计算
        public static int GetCSTypeSize(Type type)
        {
            if(type.IsPrimitive)
            {
                return System.Runtime.InteropServices.Marshal.SizeOf(type);
            }

            if(type.IsValueType)
            {
                int size = 0;
                int maxAlignment = 1;

                foreach(var field in type.GetFields())
                {
                    int fieldSize = GetCSTypeSize(field.FieldType);
                    int fieldAlignment = GetCSTypeAlignment(field.FieldType);

                    // 添加填充以满足字段对齐
                    int padding = (fieldAlignment - (size % fieldAlignment)) % fieldAlignment;
                    size += padding + fieldSize;

                    maxAlignment = Math.Max(maxAlignment, fieldAlignment);
                }

                // 结构体尾部填充
                int tailPadding = (maxAlignment - (size % maxAlignment)) % maxAlignment;
                return size + tailPadding;
            }

            throw new NotSupportedException($"Unsupported type: {type}");
        }

        // 类型对齐计算
        public static int GetCSTypeAlignment(Type type)
        {
            if(type.IsPrimitive)
            {
                return type switch
                {
                    _ when type == typeof(byte) => 1,
                    _ when type == typeof(short) => 2,
                    _ when type == typeof(int) => 4,
                    _ when type == typeof(long) => 8,
                    _ when type == typeof(float) => 4,
                    _ when type == typeof(double) => 8,
                    _ => throw new NotSupportedException()
                };
            }

            if(type.IsValueType)
            {
                int maxAlignment = 1;
                foreach(var field in type.GetFields())
                {
                    maxAlignment = Math.Max(maxAlignment, GetCSTypeAlignment(field.FieldType));
                }
                return maxAlignment;
            }

            throw new NotSupportedException($"Unsupported type: {type}");
        }

    }

}
