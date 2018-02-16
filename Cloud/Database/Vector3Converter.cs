using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace OPS.Cloud
{
    public class Vector3Converter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            return new Vector3(JsonConvert.DeserializeObject<double[]>(entry.AsString()));
        }

        public DynamoDBEntry ToEntry(object value)
        {
            Vector3 v = (Vector3)value;
            return JsonConvert.SerializeObject(v.ToDoubleArray());
        }
    }
}
