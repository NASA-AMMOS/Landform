using System;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace JPLOPS.Pipeline
{
    public class VectorNConverter : JsonConverter
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
    }
}
