using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Cloud
{
    public class VectorNConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            PrimitiveList l = entry as PrimitiveList;

            if (l == null || l.Type != DynamoDBEntryType.Numeric)
            {
                throw new InvalidCastException("Not a numeric vector");
            }
            return new DenseVector(l.Entries.Select(e => e.AsDouble()).ToArray());
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Vector<double> v = (Vector<double>)value;
            var res = new PrimitiveList(DynamoDBEntryType.Numeric);
            foreach (var x in v.ToArray())
            {
                res.Add(x);
            }
            return res;
        }
    }
}
