using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class S3ImageRef : ImageRef
    {
        private Image image;

        public S3ImageRef(string url)
        {
            Url = url;
        }

        public override Image Load(PipelineCore pipeline, IImageConverter converter = null)
        {
            if (image == null)
            {
                string f = pipeline.GetFileCached(Url, "images");
                image = converter != null ? Image.Load(f, converter) : Image.Load(f);
            }
            return image;
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
            if (obj == null || !(obj is S3ImageRef)) return false;
            return ((S3ImageRef)obj).Url == Url;
        }
    }
}
