using System;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.MathExtensions;

namespace OPS.Pipeline.AlignmentServer
{
    public class XNAMatrixConverter : JsonConverter, IPropertyConverter
    {
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Matrix);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((Matrix)value).ToDoubleArray());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return XNAExtensions.MatrixFromArray(serializer.Deserialize<double[]>(reader));
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            return XNAExtensions.MatrixFromArray(JsonConvert.DeserializeObject<double[]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            return JsonConvert.SerializeObject(((Matrix)value).ToDoubleArray());
        }
    }
}
