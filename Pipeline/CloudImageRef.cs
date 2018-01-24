using OPS.Cloud;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class CloudImageRef : ImageRef
    {
        public CloudImageRef(DataProductManager manager, Observation observation)
        {
            Manager = manager;
            Observation = observation;
        }

        /// <summary>
        /// The Observation database entry corresponding to this image.
        /// </summary>
        public readonly Observation Observation;
        public readonly DataProductManager Manager;

        Image image;
        public override Image Image
        {
            get
            {
                if (image == null)
                {
                    string fn = Manager.DownloadCached(Observation.Url, "images");
                    image = Image.Load(fn);
                }
                return image;
            }
        }

        public override ImageMetadata Metadata
        {
            get
            {
                return Image.Metadata;
            }
        }
    }
}
