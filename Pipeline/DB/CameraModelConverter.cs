using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class CameraModelConverter : JsonConverter
    {
        public override bool CanRead { get { return true; } } 
        public override bool CanWrite { get { return true; } } 

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(CameraModel);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((CameraModel)value).Serialize());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return CameraModel.Deserialize(serializer.Deserialize<string>(reader));
        }
    }
}
