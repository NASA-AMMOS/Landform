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

        protected TilingInput(string name, string projectName, string meshUrl, string imageUrl, string id)
        {
            Name = name;
            ProjectName = projectName;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            TileId = id;
            Chunked = TileId != null;
            this.IsValid();
        }

        public static TilingInput Create(DynamoDBContext context, string name, TilingProject project,
                                         string meshUrl, string imageUrl, string id)
        {
            if (project.InputNames == null)
            {
                project.InputNames = new List<string>();
            }
            else if (project.InputNames.Contains(name))
            {
                throw new ArgumentException("project \"" + project.Name + "\" already contains input \"" + name + "\"");
            }
            TilingInput input = new TilingInput(name, project.Name, meshUrl, imageUrl, id);
            context.Save(input);
            project.InputNames.Add(name);
            context.Save(project);
            return input;
        }

        public static TilingInput Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<TilingInput>(name, projectName);
        }


        public static IEnumerable<TilingInput> Find(DynamoDBContext context, TilingProject project)
        {
            if (project.InputNames != null && project.InputNames.Count > 0)
            {
                //DynamoDB Scan() can cause throughput exceptions
                //https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-query-scan.html
                //for new projects we can avoid it here because we save the input names in the project record
                List<TilingInput> inputs = new List<TilingInput>();
                foreach (var name in project.InputNames)
                {
                    inputs.Add(Find(context, project.Name, name));
                    
                }
                return inputs;
            }
            else
            {
                //for legacy projects fall back to scanning for all input records that match the project name
                return context.Scan<TilingInput>(new ScanCondition("ProjectName",
                                                                   Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal,
                                                                   project.Name));
            }
        }

        public void Save(DynamoDBContext context)
        {
            this.IsValid();
            context.Save(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ILog logger = null)
        {
            if (ChunkIds != null)
            {
                foreach (var chunkId in ChunkIds)
                {
                    TilingInputChunk.Find(pipeline.DynamoContext, chunkId).Delete(pipeline, ignoreErrors, logger);
                }
            }

            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.Storage(MeshUrl).DeleteObject(MeshUrl, ignoreErrors: ignoreErrors, logger: logger);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                pipeline.Storage(ImageUrl).DeleteObject(ImageUrl, ignoreErrors: ignoreErrors, logger: logger);
            }

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

