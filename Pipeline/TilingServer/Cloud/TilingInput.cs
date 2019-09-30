using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using log4net;
using OPS.Cloud;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline.TilingServer
{
    [DynamoDBTable("TilingInput")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingInput
    {
        [DynamoDBHashKey]
        public string Name;

        [DynamoDBRangeKey]
        public string ProjectName;

        public string MeshUrl;

        public string ImageUrl;

        public int ImageBands;

        public int ImageWidth;

        public int ImageHeight;
        
        public string TileId;

        public bool Chunked;

        public HashSet<string> ChunkIds = new HashSet<string>(); //MT safety: lock before accessing

        //This constructor must be public for DynamoDB but should not be used
        public TilingInput() { }

        protected TilingInput(string name, string projectName, string meshUrl, string imageUrl, string id) : this()
        {
            Name = name;
            ProjectName = projectName;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            TileId = id;
            Chunked = TileId != null;
            this.IsValid();
        }

        public static TilingInput Create(PipelineCore pipeline, string name, TilingProject project,
                                         string meshUrl, string imageUrl, string id)
        {
            TilingInput input = new TilingInput(name, project.Name, meshUrl, imageUrl, id);
            input.Save(pipeline);

            bool added = false;
            lock (project.InputNames)
            {
                added = project.InputNames.Add(name);
            }
            if (added)
            {
                pipeline.SaveDatabaseItem(project);
            }
            
            return input;
        }

        public static TilingInput Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<TilingInput>(name, projectName);
        }

        public static IEnumerable<TilingInput> Find(PipelineCore pipeline, TilingProject project, ILog logger = null)
        {
            foreach (var name in project.InputNames)
            {
                yield return Find(pipeline, project.Name, name);
            }
        }

        public void Save(PipelineCore pipeline)
        {
            this.IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ISet<string> keepMeshes = null)
        {
            lock (ChunkIds)
            {
                foreach (var chunkId in ChunkIds)
                {
                    TilingInputChunk.Find(pipeline, chunkId).Delete(pipeline, ignoreErrors);
                }
            }

            if (keepMeshes == null || !keepMeshes.Contains(TileId))
            {
                if (!string.IsNullOrEmpty(MeshUrl))
                {
                    pipeline.DeleteFile(MeshUrl, ignoreErrors);
                }
                
                if (!string.IsNullOrEmpty(ImageUrl))
                {
                    pipeline.DeleteFile(ImageUrl, ignoreErrors);
                }
            }

            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        private void IsValid()
        {
            if (!(Name != null && ProjectName != null && MeshUrl != null))
            {
                throw new Exception("TilingInput is missing a required field");
            }
        }
    }
}

