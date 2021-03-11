using System;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace OPS.Pipeline
{
    public class SquareMatrixConverter : JsonConverter, IPropertyConverter
    {
        public override bool CanRead { get { return true; } } 
        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Matrix<double>);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((Matrix<double>)value).ToArray());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return CreateMatrix.DenseOfArray(serializer.Deserialize<double[,]>(reader));
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            return CreateMatrix.DenseOfArray(JsonConvert.DeserializeObject<double[,]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            return JsonConvert.SerializeObject(((Matrix<double>)value).ToArray());
        }
    }
}
