using System;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace JPLOPS.Pipeline
{
    public class SquareMatrixConverter : JsonConverter
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
    }
}
