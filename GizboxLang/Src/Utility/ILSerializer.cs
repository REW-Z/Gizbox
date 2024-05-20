using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

namespace Gizbox.IL
{
    public class ILSerializer
    {
        public static void Serialize(string path, ILUnit unit)
        {
            var serializer = new DataContractSerializer(typeof(ILUnit));

            using (var stream = new System.IO.FileStream(path, FileMode.Create))
            {
                serializer.WriteObject(stream, unit);
                stream.Position = 0;
            }
        }
        public static ILUnit Deserialize(string path)
        {
            var serializer = new DataContractSerializer(typeof(ILUnit));

            using (var stream = new System.IO.FileStream(path, FileMode.Open))
            {
                return (ILUnit)serializer.ReadObject(stream);
            }
        }
        //public static void Serialize(string path, ILUnit unit)
        //{
        //    BinaryFormatter formatter = new BinaryFormatter();
        //    using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        //    {
        //        formatter.Serialize(stream, unit);
        //    }
        //}
        //public static ILUnit Deserialize(string path)
        //{
        //    BinaryFormatter formatter = new BinaryFormatter();
        //    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        //    {
        //        ILUnit unit = (ILUnit)formatter.Deserialize(stream);
        //        return unit;
        //    }
        //}

    }
}
