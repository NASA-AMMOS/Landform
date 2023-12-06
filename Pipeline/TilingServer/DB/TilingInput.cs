using System;
using System.Collections.Generic;


namespace JPLOPS.Pipeline.TilingServer
{
    public class TilingInput
    {
        [DBHashKey]
        public string Name;

        [DBRangeKey]
        public string ProjectName;

        public string MeshUrl;

        public string ImageUrl;

        public string IndexUrl;

        public int ImageBands;

        public int ImageWidth;

        public int ImageHeight;
        
        public string TileId;

        public bool Chunked;

        public HashSet<string> ChunkIds = new HashSet<string>(); //MT safety: lock before accessing

        public TilingInput() { }

        protected TilingInput(string name, string projectName, string meshUrl, string imageUrl, string indexUrl, string id) : this()
        {
            Name = name;
            ProjectName = projectName;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            IndexUrl = indexUrl;
            TileId = id;
            Chunked = TileId != null;
            this.IsValid();
        }

        public static TilingInput Create(PipelineCore pipeline, string name, TilingProject project,
                                         string meshUrl, string imageUrl, string indexUrl, string id)
        {
            TilingInput input = new TilingInput(name, project.Name, meshUrl, imageUrl, indexUrl, id);
            input.Save(pipeline);
            return input;
        }

        public static TilingInput Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<TilingInput>(name, projectName);
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

