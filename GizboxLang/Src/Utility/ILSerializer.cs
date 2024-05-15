using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Gizbox.IL
{
    public class ILSerializer
    {
        public static void Serialize(string path, ILUnit unit)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                formatter.Serialize(stream, unit);
            }
        }
        public static ILUnit Deserialize(string path)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ILUnit unit = (ILUnit)formatter.Deserialize(stream);
                return unit;
            }
        }

    }
}
