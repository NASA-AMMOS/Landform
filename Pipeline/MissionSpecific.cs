using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public enum Mission
    {
        MSL,
        M2020
    }

    public interface MissionSpecific
    {
        /// <summary>
        /// Map metadata to an observation frame name based on RMC
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        string ObservationFrameName(PDSParser parser);
    }

    public class MissionMSL : MissionSpecific
    {
        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }
    }

    public class MissionM2020 : MissionSpecific
    {
        //ROASTT: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame
        public string ObservationFrameName(PDSParser parser)
        {
            M20OPGSProductId pid = (M20OPGSProductId)parser.ProductId;
            return parser.Camera.ToString() + "_" + pid.GetConcatenatedTimeString();
        }
    }
   
}
