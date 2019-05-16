using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum Mission
    {
        MSL,
        M2020
    }

    public abstract class MissionSpecific
    {
        public static MissionSpecific GetInstance(Mission mission)
        {
            switch (mission)
            {
                case Mission.MSL: return new MissionMSL();
                case Mission.M2020: return new MissionM2020();
                default: throw new NotImplementedException("unknown mission");
            }
        }

        public static MissionSpecific GetInstance(string mission)
        {
            return GetInstance((Mission)Enum.Parse(typeof(Mission), mission, ignoreCase: false));
        }

        public virtual string RootFrameName()
        {
            return "root";
        }

        /// <summary>
        /// Map metadata to an observation frame name based on RMC
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public abstract string ObservationFrameName(PDSParser parser);

        /// <summary>
        /// Return true if this file should be used for reconstruction
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public abstract bool UseForReconstruction(PDSParser parser);

        public abstract int DayNumber(PDSParser parser);

        public class RoverObservationComparator : IComparer<RoverObservation>
        {
            private string pointsType = ObservationType.Points.ToString(), rangeType = ObservationType.Range.ToString();
            private string msss = RoverProductProducer.MSSS.ToString(), opgs = RoverProductProducer.OPGS.ToString();

            public int Compare (RoverObservation a, RoverObservation b)
            {
                // Return should be:
                // negative if a is "better" than b
                // 0 if a and b are equivalently good
                // positive if a is "worse than" b

                // always prefer XYZ to RNG if both are available
                // https://github.jpl.nasa.gov/OnSight/Landform/issues/471
                if (a.ObservationType == pointsType && b.ObservationType == rangeType)
                {
                    return -1;
                }
                if (a.ObservationType == rangeType && b.ObservationType == pointsType)
                {
                    return 1;
                }
                
                // sort next by producer, prefer MSSS "because people like the colors better"
                if (a.Producer == msss && b.Producer == opgs)
                {
                    return -1;
                }
                if (a.Producer == opgs && b.Producer == msss)
                {
                    return 1;
                }

                // sort next by linear-ness, prefer linear
                var linearA = a.IsLinear();
                var linearB = b.IsLinear();
                if (linearA && !linearB)
                {
                    return -1;
                }
                if (!linearA && linearB)
                {
                    return 1;
                }

                // finally sort by version, prefer higer versions
                // versions go numeric 1 to 9, A-Z, _ (opgs) and numeric 0 to 9, A-Z (msss)
                return (int)b.Version[0] - (int)a.Version[0];
            }
        }

        /// <summary>
        /// ordering a sequence with this function should put the "better" observations earlier in the list
        /// this a "better" observation should be *less than* a "worse" observation
        /// </summary>
        public virtual IComparer<RoverObservation> GetRoverObservationComparator()
        {
            return new RoverObservationComparator();
        }

        public abstract RoverMasker GetMasker();

        public virtual bool IsNavcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.NavcamLeft || camera == RoverProductCamera.NavcamRight;
        }

        public virtual bool IsHazcam(RoverProductCamera camera)
        {
                return camera == RoverProductCamera.FrontHazcamLeft
                    || camera == RoverProductCamera.FrontHazcamRight
                    || camera == RoverProductCamera.RearHazcamLeft
                    || camera == RoverProductCamera.RearHazcamRight;
        }

        public virtual bool IsMastcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.MastcamLeft || camera == RoverProductCamera.MastcamRight;
        }
    }

    public class MissionMSL : MissionSpecific
    {
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344; //TODO this is unused

        public override string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }

        public override bool UseForReconstruction(PDSParser parser)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }

            // Low exposure hazcams
            if (parser.DerivedImageType == RoverProductType.Image)
            {
                if (IsHazcam(parser.Camera) &&
                    parser.ExposureDuration != 0 && parser.ExposureDuration < MIN_NAV_HAZ_EXPOSURE)
                {
                    return false;
                }
            }

            //we used to try to check here that the parser could supply rover articulation, and if not return false
            //articulation is needed for mask computation
            //however, I think the check was bogus, it was always returning true
            //even if the parser could not supply the data
            //
            //and I don't think it's really appropriate to force the parser to have the articulation data
            //because it may not always be necessary to compute a mask
            //the mask may not be needed
            //or it may already be provided by the mission as its own product

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

                // Skip mastcam with short focal distances
                // (probably closeup of rover part with terrain out of focus in background)
                if (parser.MaximumFocusDistance.HasValue && parser.MaximumFocusDistance < MIN_MASTCAM_FOCUS_CUTOFF)
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

        public override int DayNumber(PDSParser parser)
        {
            return parser.PlanetDayNumber;
        }

        public override RoverMasker GetMasker()
        {
            return new MSLRoverMasker(this);
        }
    }

    public class MissionM2020 : MissionSpecific
    {
        // ROASTT: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame
        public override string ObservationFrameName(PDSParser parser)
        {          
            return parser.Camera.ToString() + "_" + ((M2020OPGSProductId)parser.ProductId).GetConcatenatedTimeString();
        }

        public override bool IsHazcam(RoverProductCamera camera)
        {
            return base.IsHazcam(camera) ||
                camera == RoverProductCamera.FrontHazcamLeftB || camera == RoverProductCamera.FrontHazcamRightB;
        }

        public override bool IsMastcam(RoverProductCamera camera)
        {
            return base.IsMastcam(camera) ||
                camera == RoverProductCamera.MastcamZLeft || camera == RoverProductCamera.MastcamZRight;
        }

        public override bool UseForReconstruction(PDSParser parser)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }
           
            //we used to try to check here that the parser could supply rover articulation, and if not return false
            //articulation is needed for mask computation
            //however, I think the check was bogus, it was always returning true
            //even if the parser could not supply the data
            //
            //and I don't think it's really appropriate to force the parser to have the articulation data
            //because it may not always be necessary to compute a mask
            //the mask may not be needed
            //or it may already be provided by the mission as its own product

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

        // ROASTT: some images have invalid PLANET_DAY_NUMBER
        public override int DayNumber(PDSParser parser)
        {
            try
            {
                return parser.PlanetDayNumber;
            }
            catch (MetadataException)
            {
                return ((M2020OPGSProductId)parser.ProductId).GetDayNumber();
            }
        }

        public override RoverMasker GetMasker()
        {
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/554
            return new M2020RoverMasker(this);
        }
    }
}
