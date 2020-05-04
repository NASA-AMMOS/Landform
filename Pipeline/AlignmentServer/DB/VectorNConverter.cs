using System;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace OPS.Pipeline.AlignmentServer
{
    public class VectorNConverter : JsonConverter, IPropertyConverter
    {
        public override bool CanRead { get { return true; } } 
        public override bool CanWrite { get { return true; } } 

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector<double>);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((Vector<double>)value).ToArray());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return CreateVector.DenseOfArray(serializer.Deserialize<double[]>(reader));
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            return CreateVector.DenseOfArray(JsonConvert.DeserializeObject<double[]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            return JsonConvert.SerializeObject(((Vector<double>)value).ToArray());
        }
    }
}
