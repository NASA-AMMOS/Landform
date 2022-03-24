using System;
using System.IO;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Adapts SparseImage to use pipeline persistence.
    ///
    /// Note that if the LRU chunk cache is used any disk backing thereof will still use a local temp directory.
    /// </summary>
    public class SparsePipelineImage : SparseImage
    {
        private PipelineCore pipeline;

        public SparsePipelineImage(PipelineCore pipeline, int bands, int width, int height, int chunkSize = 256,
                                   int cacheSize = 0, bool diskBackedCache = false)
            : base(bands, width, height, chunkSize, cacheSize, diskBackedCache)
        {
            this.pipeline = pipeline;
        }

        public SparsePipelineImage(PipelineCore pipeline, Image largeImage, int chunkSize = 256, int cacheSize = 0,
                                   bool diskBackedCache = false)
            : base(largeImage, chunkSize, cacheSize, diskBackedCache)
        {
            this.pipeline = pipeline;
        }

        public SparsePipelineImage(PipelineCore pipeline, int bands, int width, int height, string basePath,
                                   string extension, int chunkSize = 256, int cacheSize = 0,
                                   bool diskBackedCache = false)
            : base(bands, width, height, basePath, extension, chunkSize, cacheSize, diskBackedCache)
        {
            this.pipeline = pipeline;
        }

        public SparsePipelineImage(PipelineCore pipeline, string largeImagePath, int chunkSize = 256, int cacheSize = 0,
                                   bool diskBackedCache = false)
        {
            this.pipeline = pipeline;
            InitFromLargeImage(largeImagePath, chunkSize, cacheSize, diskBackedCache);
        }

        public SparsePipelineImage(SparsePipelineImage that) : base(that)
        {
            this.pipeline = that.pipeline;
        }

        public override object Clone()
        {
            return new SparsePipelineImage(this);
        }

        protected override bool IsPersisted(string path)
        {
            return pipeline.FileExists(path);
        }

        protected override void SaveChunk<T>(Image img, string path)
        {         
            TemporaryFile.GetAndDelete(Path.GetExtension(path), f => {
                base.SaveChunk<T>(img, f);
                pipeline.SaveFile(f, path);
            });
        }

        protected override Image LoadChunk(string url)
        {
            return pipeline.LoadImage(url, GetReadConverter());
        }

        protected override string PartialReadFile(string path)
        {
            return pipeline.GetFileCached(path);
        }

        protected override void Progress(string msg, params Object[] args)
        {
            pipeline.LogVerbose(msg, args);
        }     
    }
}
