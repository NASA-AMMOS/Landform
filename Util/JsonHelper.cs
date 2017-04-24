using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OPS.Util
{
    public class JsonHelper
    {
        public static string ToJson(Object o, bool indent = false)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            Formatting formatting = indent ? Formatting.Indented : Formatting.None;
            var json = JsonConvert.SerializeObject(o, typeof(object), formatting, settings);
            return json;
        }

        public static object FromJson(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json, new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto });
        }

    }
}
