using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class JsonDataProduct : DataProduct
    {
        public override void Deserialize(byte[] data)
        {
            JsonConvert.PopulateObject(Encoding.UTF8.GetString(data), this);
        }

        public override byte[] Serialize()
        {
            string data = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(data);
        }
    }
}
