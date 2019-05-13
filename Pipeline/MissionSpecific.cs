using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

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

        int SolNumber(PDSParser parser);
    }

    public class MissionMSL : MissionSpecific
    {
        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }

        private bool IsMastcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.MastcamLeft || camera == RoverProductCamera.MastcamRight;
        }

        /// <summary>
        /// Indicates whether or not this image was captured with a navigation camera.
        /// </summary>
        private bool IsNavcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.NavcamLeft || camera == RoverProductCamera.NavcamRight;
        }

        static public bool IsHazcam(RoverProductCamera camera)
        {
                return camera == RoverProductCamera.FrontHazcamLeft
                    || camera == RoverProductCamera.FrontHazcamRight
                    || camera == RoverProductCamera.RearHazcamLeft
                    || camera == RoverProductCamera.RearHazcamRight;
        }

        static public bool IsMAHLI(RoverProductCamera camera)
        {
            return camera == RoverProductCamera.MAHLI;
        }

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
                if (IsHazcam(parser.Camera) && parser.ExposureDuration != 0 && parser.ExposureDuration < MSLProject.MIN_NAV_HAZ_EXPOSURE)
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

            if (IsHazcam(parser.Camera))
            {
                return false;
            }

            // Only use single and 3 band images
            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                return false;
            }

            if (IsMastcam(parser.Camera))
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

            if (IsNavcam(parser.Camera) && parser.IsDownsampled)
            {
                return false;
            }

            return true;
        }

        public int SolNumber(PDSParser parser)
        {
            return parser.PlanetDayNumber;
        }
    }

    public class MissionM2020 : MissionSpecific
    {
        //ROASTT: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame
        public string ObservationFrameName(PDSParser parser)
        {          
            return parser.Camera.ToString() + "_" + ((M20OPGSProductId)parser.ProductId).GetConcatenatedTimeString();
        }

        private bool IsHazcam(RoverProductCamera camera)
        {
            return camera == RoverProductCamera.FrontHazcamLeft
                || camera == RoverProductCamera.FrontHazcamRight
                || camera == RoverProductCamera.RearHazcamLeft
                || camera == RoverProductCamera.RearHazcamRight;
            //|| camera == RoverProductCamera.FrontHazcamLeftB //ISSUE #534
            //|| camera == RoverProductCamera.FrontHazcamRightB
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

            if (IsHazcam(parser.Camera))
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

        //ROASTT: some images have invalid PLANET_DAY_NUMBER
        public int SolNumber(PDSParser parser)
        {
            try
            {
                return parser.PlanetDayNumber;
            }
            catch (MetadataException)
            {
                return ((M20OPGSProductId)parser.ProductId).GetSolNumber();
            }
        }
    }
}
