using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Util;
namespace OPS.Pipeline
{
    public class SparseCloudImage : SparseImage
    {
        PipelineCore pipeline;
        string storageUrl;

        public SparseCloudImage(Image largeImage, PipelineCore pipeline, int chunkSize = 256) : base(largeImage, chunkSize)
        {
            this.pipeline = pipeline;
        }

        public SparseCloudImage(int bands, int width, int height, string baseUrl, string extension, PipelineCore pipeline, int chunkSize = 256) : base(bands, width, height, baseUrl, extension, chunkSize)
        {
            this.pipeline = pipeline;
        }

        public SparseCloudImage(string baseUrl, string extension, string storageUrl, PipelineCore pipeline, int chunkSize = 256) : base(0, 0, 0, baseUrl, extension, chunkSize)
        {
            this.pipeline = pipeline;
            this.storageUrl = storageUrl;
            pipeline.GetFile(baseUrl, f => {
                ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
                if (s.GetType() != typeof(GDALSerializer))
                {
                    throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
                }
                Vector3 metadata = ((GDALSerializer)s).GetMetadata(f);
                Bands = (int)metadata[0];
                Width = (int)metadata[1];
                Height = (int)metadata[2];
                Partition(f);
            });
        }

        /// <summary>
        /// Split an image into chunks with dimensions chunkSize without loading the full image into memory; chunks stored on disk for on demand loading.
        /// </summary>
        protected void Partition(string filename)
        {
            
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(extension);
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Partial image read only supported for GDALSerializer.");
            }
            for (int r = 0; r <= Height / chunkSize; r++)
            {
                for (int c = 0; c <= Width / chunkSize; c++)
                {
                    pipeline.LogInfo("Creating chunk (" + r + ", " + c + "), " + (r * Width / chunkSize + c) + " / " + (Width / chunkSize * Height / chunkSize) + " complete.");
                    Image chunk = ((GDALSerializer)s).PartialRead(filename, c * chunkSize, r * chunkSize, Math.Min(Width - c * chunkSize, chunkSize), Math.Min(Height - r * chunkSize, chunkSize), s.DefaultReadConverter());
                    SaveChunk<byte>(chunk, CreateFileName(r, c, storageUrl, extension));
                }
            }
        }

        protected override Image LoadChunk(string url)
        {
            return pipeline.LoadImage(url);
        }

        protected override void SaveChunk<T>(Image img, string url)
        {         
            TemporaryFile.GetAndDelete(Path.GetExtension(url), f => {
                base.SaveChunk<T>(img, f);
                pipeline.SaveFile(f, url);

            });
        }
    }
}
