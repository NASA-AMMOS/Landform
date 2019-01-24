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
