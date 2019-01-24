using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;

namespace OPS.Pipeline.AlignmentServer
{
    public class SquareMatrixConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            return CreateMatrix.DenseOfArray(JsonConvert.DeserializeObject<double[,]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Matrix<double> m = (Matrix<double>)value;
            return JsonConvert.SerializeObject(m.ToArray());
        }
    }
}
