using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Cloud
{
    [DynamoDBTable("SpatialIndex")]
    public class SpatialIndex
    {
        [DynamoDBHashKey]
        [DynamoDBProperty]
        public string Id { get; set; }

        [DynamoDBRangeKey]
        [DynamoDBProperty]
        public string ProjectName { get; set; }
        
        [DynamoDBProperty(Converter = typeof(Vector3Converter))]
        public Vector3 Center { get; set; }
        [DynamoDBProperty(Converter = typeof(Vector3Converter))]
        public Vector3 Size { get; set; }
        
        [DynamoDBProperty]
        public int MaxPrecision { get; set; }

        [DynamoDBIgnore]
        public BoundingBox Bounds
        {
            get
            {
                return new BoundingBox(Center - Size / 2, Center + Size / 2);
            }
            set
            {
                Center = (value.Max + value.Min) / 2;
                Size = value.Min - value.Max;
            }
        }

        [DynamoDBIgnore]
        public Geohash Geohash
        {
            get
            {
                return new Geohash(Bounds.Min.ToDoubleArray(), Bounds.Max.ToDoubleArray());
            }
        }

        public SpatialIndex() { }
        protected SpatialIndex(string id, string projectName, BoundingBox bounds)
        {
            Id = id;
            ProjectName = projectName;
            Bounds = bounds;
        }

        public static SpatialIndex Create(DynamoDBContext context, Project p, BoundingBox bounds)
        {
            SpatialIndex si = new SpatialIndex(Guid.NewGuid().ToString(), p.Name, bounds);
            si.Save(context);
            return si;
        }
        public void Save(DynamoDBContext context)
        {
            context.Save(this, new DynamoDBOperationConfig { IgnoreNullValues = true });
        }

        public static SpatialIndex Find(DynamoDBContext context, string projectName, string id)
        {
            return context.Load<SpatialIndex>(id, projectName);
        }
    }
}
