using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Pipeline.TilingServer
{
    public class TilingInputChunk
    {
        [DBHashKey]
        public string Id;

        public string MeshUrl;

        public string ImageUrl;

        public string Bounds;

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
