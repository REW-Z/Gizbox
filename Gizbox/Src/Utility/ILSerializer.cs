using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using Gizbox.IR;

namespace Gizbox.IR
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
    }
}
