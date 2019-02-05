using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class CachedImageRef : ImageRef
    {
        private Image image;
        private PipelineCore pipeline;

        public CachedImageRef(PipelineCore pipeline, string url)
        {
            this.pipeline = pipeline;
            Url = url;
        }

        public override Image Load(IImageConverter converter = null)
        {
            //if (pipeline.ImageCache.ContainsKey(this)) return pipeline.ImageCache[this];

            if (image == null)
            {
                string f = GetFile();
                image = converter != null ? Image.Load(f, converter) : Image.Load(f);
            }

            //pipeline.ImageCache[this] = image;

            return image;
        }

        public override string GetFile()
        {
            return pipeline.GetFileCached(Url, "images");
        }

        public override string DisplayName
        {
            get
            {
                return Path.GetFileNameWithoutExtension(Url);
            }
        }

        public override int GetHashCode()
        {
            return Url.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is CachedImageRef)) return false;
            return ((CachedImageRef)obj).Url == Url;
        }
    }
}
