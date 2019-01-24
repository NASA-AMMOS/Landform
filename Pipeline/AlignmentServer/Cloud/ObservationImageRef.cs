using OPS.Imaging;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    public class ObservationImageRef : ImageRef
    {
        public ObservationImageRef(Observation observation)
        {
            Url = observation.Url;
            Observation = observation;

            if (string.IsNullOrEmpty(Url))
            {
                throw new Exception(string.Format("cannot create ObservationImageRef from observation {0}: empty URL",
                                                  observation.Name));
            }

            string lc = Url.ToLower();
            if (lc.StartsWith("s3://"))
            {
                this.ImageRef = new S3ImageRef(Url);
            }
            else if (lc.StartsWith("file://"))
            {
                this.ImageRef = new DiskImageRef(Url.Substring(7));
            }
            else
            {
                throw new Exception(string.Format("cannot create ObservationImageRef from observation {0}: " +
                                                  "unrecognized protocol in URL {1}", observation.Name, Url));
            }
        }

        /// <summary>
        /// The wrapped ImageRef.
        /// </summary>
        public readonly ImageRef ImageRef;

        /// <summary>
        /// The Observation database entry corresponding to this image.
        /// </summary>
        public readonly Observation Observation;
        
        public override string DisplayName
        {
            get
            {
                return this.Observation.Name;
            }
        }

        public override Image Load(PipelineCore pipeline)
        {
            return this.ImageRef.Load(pipeline);
        }

        public override Image Load(PipelineCore pipeline, IImageConverter imageConverter)
        {
            return this.ImageRef.Load(pipeline, imageConverter);
        }

        public override int GetHashCode()
        {
            return this.ImageRef.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return this.ImageRef.Equals(obj);
        }

        public override string ToString()
        {
            return this.ImageRef.ToString();
        }
    }
}
