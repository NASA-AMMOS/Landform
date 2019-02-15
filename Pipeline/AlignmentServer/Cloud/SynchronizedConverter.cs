using System;
using System.Reflection;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace OPS.Pipeline.AlignmentServer
{
    public class SynchronizedConverter<T> : JsonConverter
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
            return objectType == typeof(T);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            lock (value)
            {
                serializer.Serialize(writer, value);
            }
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return serializer.Deserialize<T>(reader);
        }
    }
}
