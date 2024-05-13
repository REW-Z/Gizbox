using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Gizbox.IL
{
    //IL Unit的可序列化结构    
    [Serializable]
    public class ILLib
    {
        public string[] dependencyNames;

        public TAC[] codes;

        private Dictionary<string, int> label2Line;

        public Dictionary<int, Gizbox.GStack<SymbolTable>> stackDic;
    }

    public class ILSerializer
    {
        public static void SerializeDirectly(string path, ILUnit unit)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                formatter.Serialize(stream, unit);
            }
        }
        public static ILUnit DeserializeDirectly(string path)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ILUnit unit = (ILUnit)formatter.Deserialize(stream);
                return unit;
            }
        }


        public static ILLib RuntimeUnit2Lib(ILUnit unit)
        {
            ILLib lib = new ILLib();
            lib.codes = unit.codes.ToArray();
            return lib;
        }

        public static void Serialize(string path , ILLib lib)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                formatter.Serialize(stream, lib);
            }
        }
        public static ILLib Deserialize(string path)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ILLib unit = (ILLib)formatter.Deserialize(stream);
                return unit;
            }
        }
    }
}
