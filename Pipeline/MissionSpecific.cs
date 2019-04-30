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

        /// <summary>
        /// Return true if this file should be used for reconstruction
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        bool UseForReconstruction(PDSParser parser);

    }

    public class MissionMSL : MissionSpecific
    {
        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }

        public bool AllowDownsampledImages() { return false;  } //images are too low res

        public bool UseForReconstruction(PDSParser parser)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }

            // Low exposure hazcams
            if (parser.DerivedImageType == RoverProductType.Image)
            {
                if (parser.IsHazcam && parser.ExposureDuration != 0 && parser.ExposureDuration < MSLProject.MIN_NAV_HAZ_EXPOSURE)
                {
                    return false;
                }
            }

            //Needed for mask computation
            try
            {
                if (parser.Articulation == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (parser.IsHazcam)
            {
                return false;
            }

            // Only use single and 3 band images
            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                return false;
            }

            if (parser.IsMastcam)
            {
                // Skip mastcam taken with color filters
                try
                {
                    if (!parser.FilterNumber.HasValue || parser.FilterNumber != 0)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                // Skip mastcam with short focal distances (probably closeup of rover part with terrain out of focus in background)
                if (parser.MaximumFocusDistance.HasValue && parser.MaximumFocusDistance < MSLProject.MIN_MASTCAM_FOCUS_CUTOFF)
                {
                    return false;
                }
            }

            if (parser.IsNavcam && parser.IsDownsampled)
            {
                return false;
            }

            return true;
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

        public bool UseForReconstruction(PDSParser parser)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }
           
            //Needed for mask computation
            try
            {
                if (parser.Articulation == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (parser.IsHazcam)
            {
                return false;
            }

            // Only use single and 3 band images
            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                return false;
            }
            
            return true;
        }
    }
}
