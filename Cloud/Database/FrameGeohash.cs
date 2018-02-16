using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Xna.Framework;

namespace OPS.Cloud
{
    [DynamoDBTable("FrameGeohashes")]
    public class FrameGeohash
    {
        [DynamoDBHashKey]
        [DynamoDBProperty]
        public string Id { get; set; }

        [DynamoDBRangeKey]
        [DynamoDBProperty]
        public string ProjectName { get; set; }
        
        [DynamoDBProperty]
        public string SpatialIndexId { get; set; }

        [DynamoDBProperty]
        public string FrameName { get; set; }

        [DynamoDBProperty]
        public string Geohash { get; set; }

        public FrameGeohash() { }
        protected FrameGeohash(string id, Project project, SpatialIndex index, Frame frame, string geohash)
        {
            Id = id;
            ProjectName = project.Name;
            SpatialIndexId = index.Id;
            FrameName = frame.Name;
            Geohash = geohash;
        }

        public static FrameGeohash Find(DynamoDBContext context, string projectName, string id)
        {
            return context.Load<FrameGeohash>(id, projectName);
        }

        /// <summary>
        /// Find all geohashes associated with a frame.
        /// </summary>
        public static IEnumerable<FrameGeohash> Find(DynamoDBContext context, Frame frame)
        {
            return context.Scan<FrameGeohash>(
                new ScanCondition("ProjectName", ScanOperator.Equal, frame.ProjectName),
                new ScanCondition("FrameName", ScanOperator.Equal, frame.Name));
        }

        /// <summary>
        /// Find all geohashes associated with a frame on a specific index.
        /// </summary>
        public static IEnumerable<FrameGeohash> Find(DynamoDBContext context, SpatialIndex index, Frame frame)
        {
            return context.Scan<FrameGeohash>(
                new ScanCondition("ProjectName", ScanOperator.Equal, frame.ProjectName),
                new ScanCondition("SpatialIndexId", ScanOperator.Equal, index.Id),
                new ScanCondition("FrameName", ScanOperator.Equal, frame.Name));
        }

        /// <summary>
        /// Return all geohashes on an index that overlap a bounding box.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="index"></param>
        /// <param name="bounds"></param>
        /// <returns></returns>
        public static IEnumerable<FrameGeohash> FindOverlapping(DynamoDBContext context, SpatialIndex index, BoundingBox bounds)
        {
            foreach (var prefix in index.Geohash.Overlapping(bounds.Min.ToDoubleArray(), bounds.Max.ToDoubleArray(), index.MaxPrecision))
            {
                foreach (var geohash in context.Scan<FrameGeohash>(
                    new ScanCondition("SpatialIndexId", ScanOperator.Equal, index.Id), 
                    new ScanCondition("Geohash", ScanOperator.BeginsWith, prefix)))
                {
                    yield return geohash;
                }
            }
        }

        public void Save(DynamoDBContext context)
        {
            context.Save(this, new DynamoDBOperationConfig { IgnoreNullValues = true });
        }

        public static FrameGeohash Create(DynamoDBContext context, Project project, SpatialIndex index, Frame frame, string geohash)
        {
            var res = new FrameGeohash(Guid.NewGuid().ToString(), project, index, frame, geohash);
            res.Save(context);
            return res;
        }
    }
}
