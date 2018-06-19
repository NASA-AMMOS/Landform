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
        public S3ImageRef(string url)
        {
            Url = url;
        }

        /// <summary>
        /// S3 URL containing the image.
        /// </summary>
        public readonly string Url;

        Image image;
        public override Image Load(PipelineCore pipeline)
        {
            if (image == null)
            {
                string fn = pipeline.DownloadCached(Url, "images");
                image = Image.Load(fn);
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
