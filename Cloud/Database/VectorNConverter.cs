using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace OPS.Cloud
{
    public class VectorNConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            return CreateVector.DenseOfArray(JsonConvert.DeserializeObject<double[]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Vector<double> v = (Vector<double>)value;
            return JsonConvert.SerializeObject(v.ToArray());
        }
    }
}
