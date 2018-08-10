using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
namespace OPS.Pipeline
{
    public class SparseCloudImage : SparseImage
    {
        PipelineCore pipeline;
        public SparseCloudImage(Image largeImage, PipelineCore pipeline, int chunkSize = 256) : base(largeImage, chunkSize)
        {
            this.pipeline = pipeline;
        }

        public SparseCloudImage(int bands, int width, int height, string baseUrl, string extension, PipelineCore pipeline, int chunkSize = 256) : base(bands, width, height, baseUrl, extension, chunkSize)
        {
            this.pipeline = pipeline;
        }

        protected override Image LoadChunk(string url)
        {
            S3ImageRef imgRef = new S3ImageRef(url);
            return imgRef.Load(pipeline);
        }

        protected override void SaveChunk<T>(Image img, string url)
        {         
            TemporaryFile.GetAndDelete(Path.GetExtension(url), f => {
                base.SaveChunk<T>(img, f);
                pipeline.Storage.UploadFile(f, url);

            });
        }
    }
}
