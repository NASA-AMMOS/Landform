using OPS.Cloud;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class CloudImageRef : ImageRef
    {
        public CloudImageRef(Observation observation)
        {
            Observation = observation;
        }

        /// <summary>
        /// The Observation database entry corresponding to this image.
        /// </summary>
        public readonly Observation Observation;

        Image image;
        public override Image Load(PipelineCore pipeline)
        {
            if (image == null)
            {
                string fn = pipeline.DownloadCached(Observation.Url, "images");
                image = Image.Load(fn);
            }
            return image;
        }

        public override string DisplayName
        {
            get
            {
                return Path.GetFileNameWithoutExtension(Observation.Name);
            }
        }

    }
}
