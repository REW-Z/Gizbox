using System;
using System.Collections.Generic;
using System.Text;

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

        public static bool IsPrimitiveType(string typeExpr)
        {
            switch (typeExpr)
            {
                case "bool": return true;
                case "int": return true;
                case "float": return true;
                case "string": return true;
                default: return false;
            }
        }

        public static bool IsArrayType(string typeExpr)
        {
            return typeExpr.EndsWith("[]");
        }

        public static bool IsFunType(string typeExpr)
        {
            return typeExpr.Contains("->");
        }

        public static bool IsClassType(string typeExpr)
        {
            if(IsPrimitiveType(typeExpr)) 
                return false;
            if(IsArrayType(typeExpr))
                return false;
            if(IsFunType(typeExpr))
                return false;

            return true;
        }
    }
}
