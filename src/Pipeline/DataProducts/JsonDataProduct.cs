using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace JPLOPS.Pipeline
{
    public class JsonDataProduct : DataProduct
    {
        public override void Deserialize(byte[] data)
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (StreamReader sr = new StreamReader(ms, Encoding.UTF8))
                {
                    serializer.Populate(sr, this);
                }
            }
        }

        public override byte[] Serialize()
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;

            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            {
                using (StreamWriter sw = new StreamWriter(ms, Encoding.UTF8))
                {
                    serializer.Serialize(sw, this);
                }
                data = ms.ToArray();
            }
            return data;
        }
    }
}
