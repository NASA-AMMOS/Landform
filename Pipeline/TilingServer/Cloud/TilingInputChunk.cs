using Amazon.DynamoDBv2.DataModel;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace OPS.Pipeline.TilingServer
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

        //This constructor must be public for DynamoDB but should not be used
        public TilingInputChunk() { }

        protected TilingInputChunk(string id, string meshUrl, string imageUrl, BoundingBox bounds)
        {
            Id = id;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            Bounds = JsonHelper.ToJson(bounds);
        }


        public static TilingInputChunk Create(PipelineCore pipeline, string id, string meshUrl, string imageUrl,
                                              BoundingBox bounds)
        {
            TilingInputChunk chunk = new TilingInputChunk(id, meshUrl, imageUrl, bounds);
            pipeline.SaveDatabaseItem(chunk);
            return chunk;
        }

        public static TilingInputChunk Find(PipelineCore pipeline, string id)
        {
            return pipeline.LoadDatabaseItem<TilingInputChunk>(id);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.DeleteFile(MeshUrl, ignoreErrors);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                //note this call is DeleteFiles() not DeleteFile()
                //because there can be multiple files with the same basename for these images
                pipeline.DeleteFiles(ImageUrl, "*", ignoreErrors);
            }
                
            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        public BoundingBox GetBounds()
        {
            return (BoundingBox)JsonHelper.FromJson(this.Bounds);
        }
    }
}
