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

namespace OPS.Pipeline.TileServer
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

        [JsonConverter(typeof(SynchronizedConverter<List<string>>))]
        public List<string> ChunkIds;

        public TilingInput()
        {
            ChunkIds = new List<string>();
        }

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

            if (project.AddInput(name))
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
            foreach (var name in project.GetInputs())
            {
                yield return Find(pipeline, project.Name, name);
            }
        }

        public void Save(PipelineCore pipeline)
        {
            this.IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            lock (ChunkIds)
            {
                foreach (var chunkId in ChunkIds)
                {
                    TilingInputChunk.Find(pipeline, chunkId).Delete(pipeline, ignoreErrors);
                }
            }

            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.DeleteFile(MeshUrl, ignoreErrors);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                pipeline.DeleteFile(ImageUrl, ignoreErrors);
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

        public bool AddChunk(string id)
        {
            lock (ChunkIds)
            {
                if (ChunkIds.Contains(id))
                {
                    return false;
                }
                ChunkIds.Add(id);
                return true;
            }
        }

        public int AddChunks(IEnumerable<string> ids)
        {
            lock (ChunkIds)
            {
                int old = ChunkIds.Count;
                HashSet<string> unique = new HashSet<string>();
                unique.UnionWith(ChunkIds);
                unique.UnionWith(ids);
                ChunkIds.Clear();
                ChunkIds.AddRange(unique);
                return ChunkIds.Count - old;
            }
        }

        public IEnumerable<string> GetChunks()
        {
            lock (ChunkIds)
            {
                return ChunkIds.ToArray();
            }
        }
    }
}

