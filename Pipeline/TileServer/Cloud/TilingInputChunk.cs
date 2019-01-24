using Amazon.DynamoDBv2.DataModel;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Plumbing;
using log4net;

namespace OPS.Pipeline.TileServer
{
    [DynamoDBTable("TilingInputChunk")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingInputChunk
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Id { get; set; }

        public string MeshUrl { get; set; }

        public string ImageUrl { get; set; }

        public string Bounds { get; set; }

        public TilingInputChunk()
        {

        }

        protected TilingInputChunk(string id, string meshUrl, string imageUrl, BoundingBox bounds)
        {
            Id = id;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            Bounds = JsonHelper.ToJson(bounds);
        }


        public static TilingInputChunk Create(PipelineCore pipeline, string id, TilingProject project,
                                              string meshUrl, string imageUrl, BoundingBox bounds)
        {
            TilingInputChunk chunk = new TilingInputChunk(id, meshUrl, imageUrl, bounds);
            pipeline.DynamoContext.Save(chunk);
            return chunk;
        }

        public static TilingInputChunk Find(PipelineCore pipeline, string id)
        {
            return pipeline.DynamoContext.Load<TilingInputChunk>(id);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.Storage(MeshUrl).DeleteObject(MeshUrl, ignoreErrors: ignoreErrors, logger: pipeline.Logger);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                //note this call is DeleteObjects() not DeleteObject()
                //because there can be multiple files with the same basename for these images
                pipeline.Storage(ImageUrl).DeleteObjects(ImageUrl, ignoreErrors: ignoreErrors, logger: pipeline.Logger);
            }
                
            pipeline.DeleteDynamoItem(this, ignoreErrors);
        }

        public BoundingBox GetBounds()
        {
            return (BoundingBox)JsonHelper.FromJson(this.Bounds);
        }
    }
}
