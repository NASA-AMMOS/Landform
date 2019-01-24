using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Plumbing;
using Amazon.DynamoDBv2.DataModel;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.AlignmentServer
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

        public static FrameGeohash Find(PipelineCore pipeline, string projectName, string id)
        {
            return pipeline.LoadDatabaseItem<FrameGeohash>(id, projectName);
        }

        /// <summary>
        /// Find all geohashes associated with a frame.
        /// </summary>
        public static IEnumerable<FrameGeohash> Find(PipelineCore pipeline, Frame frame)
        {
            return pipeline.ScanDatabase<FrameGeohash>(new Dictionary<string, string>()
                                                       {
                                                           { "ProjectName", frame.ProjectName },
                                                           { "FrameName", frame.Name }
                                                       });
        }

        /// <summary>
        /// Find all geohashes associated with a frame on a specific index.
        /// </summary>
        public static IEnumerable<FrameGeohash> Find(PipelineCore pipeline, SpatialIndex index, Frame frame)
        {
            return pipeline.ScanDatabase<FrameGeohash>(new Dictionary<string, string>()
                                                       {
                                                           { "ProjectName", frame.ProjectName },
                                                           { "SpatialIndexId", index.Id },
                                                           { "FrameName", frame.Name }
                                                       });
        }

        /// <summary>
        /// Return all geohashes on an index that overlap a bounding box.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="index"></param>
        /// <param name="bounds"></param>
        /// <returns></returns>
        public static IEnumerable<FrameGeohash> FindOverlapping(PipelineCore pipeline, SpatialIndex index, BoundingBox bounds)
        {
            foreach (var prefix in index.Geohash.Overlapping(bounds.Min.ToDoubleArray(), bounds.Max.ToDoubleArray(), index.MaxPrecision))
            {
                foreach (var geohash in
                         pipeline.ScanDatabase<FrameGeohash>(new Dictionary<string, string>()
                                                             {
                                                                 { "SpatialIndexId", index.Id },
                                                                 { "Geohash", "^" + prefix }
                                                             }))
                {
                    yield return geohash;
                }
            }
        }

        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public static FrameGeohash Create(PipelineCore pipeline, Project project, SpatialIndex index, Frame frame, string geohash)
        {
            var res = new FrameGeohash(Guid.NewGuid().ToString(), project, index, frame, geohash);
            res.Save(pipeline);
            return res;
        }
    }
}
