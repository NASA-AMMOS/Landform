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
    public class ObservationImageRef : S3ImageRef
    {
        public ObservationImageRef(Observation observation)
            : base(observation.Url)
        {
            Observation = observation;
        }

        /// <summary>
        /// The Observation database entry corresponding to this image.
        /// </summary>
        public readonly Observation Observation;
        
        public override string DisplayName
        {
            get
            {
                return Observation.Name;
            }
        }
    }
}
