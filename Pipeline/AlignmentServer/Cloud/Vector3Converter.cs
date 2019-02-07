using System;
using System.Reflection;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace OPS.Pipeline.AlignmentServer
{
    public class Vector3Converter : JsonConverter, IPropertyConverter
    {
        public override bool CanRead
        {
            get { return true; }
        }
        
        public override bool CanWrite
        {
            get { return true; }
        }

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

        public object FromEntry(DynamoDBEntry entry)
        {
            return new Vector3(JsonConvert.DeserializeObject<double[]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            return JsonConvert.SerializeObject(((Vector3)value).ToDoubleArray());
        }
    }
}
