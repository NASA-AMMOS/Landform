using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Geometry;

namespace OPS.Pipeline.AlignmentServer
{
    public class SpatialIndex
    {
        [DBHashKey]
        public string Id;

        [DBRangeKey]
        public string ProjectName;
        
        [JsonConverter(typeof(Vector3Converter))]
        public Vector3 Center;

        [JsonConverter(typeof(Vector3Converter))]
        public Vector3 Size;
        
        public int MaxPrecision;

        [JsonIgnore]
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

        [JsonIgnore]
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

        public static SpatialIndex Create(PipelineCore pipeline, Project p, BoundingBox bounds)
        {
            SpatialIndex si = new SpatialIndex(Guid.NewGuid().ToString(), p.Name, bounds);
            si.Save(pipeline);
            return si;
        }
        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public static SpatialIndex Find(PipelineCore pipeline, string projectName, string id)
        {
            return pipeline.LoadDatabaseItem<SpatialIndex>(id, projectName);
        }
    }
}
