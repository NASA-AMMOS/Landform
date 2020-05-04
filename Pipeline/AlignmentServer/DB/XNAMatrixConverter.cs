using System;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.MathExtensions;

namespace OPS.Pipeline.AlignmentServer
{
    public class XNAMatrixConverter : XNAMatrixJsonConverter, IPropertyConverter
    {
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
