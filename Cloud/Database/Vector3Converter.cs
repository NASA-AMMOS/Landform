using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Xna.Framework;

namespace OPS.Cloud
{
    public class Vector3Converter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            PrimitiveList l = entry as PrimitiveList;
            
            if (l == null || l.Entries.Count != 3 || l.Type != DynamoDBEntryType.Numeric)
            {
                throw new InvalidCastException("Not a Vector3");
            }
            return new Vector3(
                l.Entries[0].AsDouble(),
                l.Entries[1].AsDouble(),
                l.Entries[2].AsDouble()
                );
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Vector3 v = (Vector3)value;
            var res = new PrimitiveList(DynamoDBEntryType.Numeric);
            res.Add(Convert.ToDecimal(v.X));
            res.Add(Convert.ToDecimal(v.Y));
            res.Add(Convert.ToDecimal(v.Z));
            return res;
        }
    }
}
