using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Plumbing;
using log4net;

namespace OPS.Pipeline.TileServer
{
    
    [DynamoDBTable("TilingInput")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingInput
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Name { get; set; }

        [DynamoDBRangeKey]
        public string ProjectName { get; set; }

        public string MeshUrl { get; set; }

        public string ImageUrl { get; set; }

        public int ImageBands { get; set; }

        public int ImageWidth { get; set; }

        public int ImageHeight { get; set; }
        
        public string TileId { get; set; }

        public bool Chunked { get; set; }

        public List<string> ChunkIds { get; set; }

        public TilingInput()
        {

        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingInput(string name, TilingProject project, string meshUrl, string imageUrl, string id)
        {
            Name = name;
            ProjectName = project.Name;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            TileId = id;
            Chunked = TileId != null;
            this.IsValid();
        }

        public static TilingInput Create(DynamoDBContext context, string name, TilingProject project,
                                         string meshUrl, string imageUrl, string id)
        {
            TilingInput input = new TilingInput(name, project, meshUrl, imageUrl, id);
            context.Save(input, new DynamoDBOperationConfig() { IgnoreNullValues = true });
            return input;
        }

        public static TilingInput Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<TilingInput>(name, projectName);
        }


        public static IEnumerable<TilingInput> Find(DynamoDBContext context, string projectName)
        {
            return context.Scan<TilingInput>(
                new ScanCondition("ProjectName", Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, projectName)
                );
        }

        public void Save(DynamoDBContext context)
        {
            this.IsValid();
            context.Save(this, new DynamoDBOperationConfig() { IgnoreNullValues = true });
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ILog logger = null)
        {
            foreach (var chunkId in ChunkIds)
            {
                TilingInputChunk.Find(pipeline.DynamoContext, chunkId).Delete(pipeline, ignoreErrors, logger);
            }

            pipeline.Storage(MeshUrl).DeleteObject(MeshUrl, ignoreErrors: ignoreErrors, logger: logger);
            pipeline.Storage(ImageUrl).DeleteObject(ImageUrl, ignoreErrors: ignoreErrors, logger: logger);

            pipeline.DeleteDynamoItem(this, ignoreErrors, logger);
        }

        private void IsValid()
        {
            if (!(Name != null && ProjectName != null && MeshUrl != null))
            {
                throw new CloudException("TilingInput is missing a required field");
            }
        }
    }
}

