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
    public class SquareMatrixConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            PrimitiveList l = entry as PrimitiveList;
            if (l == null || l.Type != DynamoDBEntryType.Numeric)
            {
                throw new InvalidCastException("Matrix must be list of numbers");
            }
            double sqrtSize = Math.Sqrt(l.Entries.Count);
            int rows = (int)Math.Floor(sqrtSize);
            if (rows * rows != l.Entries.Count)
            {
                throw new InvalidCastException("Non-square matrix");
            }

            return new DenseMatrix(rows, rows, l.Entries.Select(e => e.AsDouble()).ToArray());
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Matrix<double> m = (Matrix<double>)value;

            var res = new PrimitiveList(DynamoDBEntryType.Numeric);
            foreach (var x in m.ToColumnMajorArray())
            {
                res.Add(x);
            }
            return res;
        }
    }
}
