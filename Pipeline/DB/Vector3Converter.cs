using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.Pipeline
{
    public class Vector3Converter : JsonConverter
    {
        public override bool CanRead { get { return true; } } 
        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector3);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((Vector3)value).ToDoubleArray());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return new Vector3(serializer.Deserialize<double[]>(reader));
        }
    }
}
