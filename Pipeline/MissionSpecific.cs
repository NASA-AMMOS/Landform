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

        public virtual string RoverMotionCounter(PDSParser parser)
        {
            return parser.RMC;
        }

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

        public virtual RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            return cam;
        }

        public abstract RoverProductCamera GetRoverProductCamera(string instrumentId);
        public abstract double GetSensorPixelSizeMM(RoverProductCamera camera);
        public abstract double GetFocalLengthMM(RoverProductCamera camera);

        public abstract double GetMinimumFocusDistance(PDSMetadata metadata);
        public abstract double? GetMaximumFocusDistance(PDSMetadata metadata);

        public virtual bool AllowLocationsDB()
        {
            return false;
        }

        public virtual bool AllowPlacesDB()
        {
            return true;
        }

        public virtual bool AllowLegacyManifestDB()
        {
            return false;
        }
    }

    public class MissionMSL : MissionSpecific
    {
        public const int MIN_NAV_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344; //TODO this is unused

        public override bool UseForReconstruction(PDSParser parser)
        {
            // Partial downloads
            if (parser.IsPartial)
            {
                return false;
            }

            RoverProductCamera roverProdCam = GetRoverProductCamera(parser.InstrumentId);
            // Low exposure hazcams
            if (parser.DerivedImageType == RoverProductType.Image)
            {
                if (IsHazcam(roverProdCam) &&
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

            if (IsHazcam(roverProdCam))
            {
                return false;
            }

            // Only use single and 3 band images
            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                return false;
            }

            if (IsMastcam(roverProdCam))
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
                if (GetMaximumFocusDistance(parser.metadata as PDSMetadata).HasValue &&
                    GetMaximumFocusDistance(parser.metadata as PDSMetadata) < MIN_MASTCAM_FOCUS_CUTOFF)
                {
                    return false;
                }
            }

            if (IsNavcam(roverProdCam) && parser.IsDownsampled)
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

        public override RoverProductCamera GetRoverProductCamera(string instrumentId)
        {
            if (instrumentId.StartsWith("FHAZ_LEFT"))
            {
                return RoverProductCamera.FrontHazcamLeft;
            }
            else if (instrumentId.StartsWith("FHAZ_RIGHT"))
            {
                return RoverProductCamera.FrontHazcamRight;
            }
            else if (instrumentId.StartsWith("RHAZ_LEFT"))
            {
                return RoverProductCamera.RearHazcamLeft;
            }
            else if (instrumentId.StartsWith("RHAZ_RIGHT"))
            {
                return RoverProductCamera.RearHazcamRight;
            }
            else if (instrumentId.StartsWith("NAV_LEFT"))
            {
                return RoverProductCamera.NavcamLeft;
            }
            else if (instrumentId.StartsWith("NAV_RIGHT"))
            {
                return RoverProductCamera.NavcamRight;
            }
            else if (instrumentId.StartsWith("MAST_LEFT"))
            {
                return RoverProductCamera.MastcamLeft;
            }
            else if (instrumentId.StartsWith("MAST_RIGHT"))
            {
                return RoverProductCamera.MastcamRight;
            }
            else if (instrumentId.StartsWith("MAHLI"))
            {
                return RoverProductCamera.MAHLI;
            }
            
            return RoverProductCamera.Unknown;
        }

        public override bool AllowLocationsDB()
        {
            return true;
        }

        public override bool AllowLegacyManifestDB()
        {
            return true;
        }

        public override double GetFocalLengthMM(RoverProductCamera camera)
        {
            switch (camera)
            {
                case RoverProductCamera.NavcamLeft:
                    return 14.67; //source SIS: https://pds-imaging.jpl.nasa.gov/data/msl/MSLNAV_0XXX/DOCUMENT/MSL_CAMERA_SIS_latest.PDF
                case RoverProductCamera.NavcamRight:
                    return 14.67; //source SIS: https://pds-imaging.jpl.nasa.gov/data/msl/MSLNAV_0XXX/DOCUMENT/MSL_CAMERA_SIS_latest.PDF
                case RoverProductCamera.MastcamLeft:
                    return 34.0; //https://www.lpi.usra.edu/meetings/lpsc2010/pdf/1123.pdf
                case RoverProductCamera.MastcamRight:
                    return 10.0; //https://www.lpi.usra.edu/meetings/lpsc2010/pdf/1123.pdf
                default:
                    throw new NotImplementedException("focal length for camera " + camera + " not added yet");
            }
        }
      
        public override double GetSensorPixelSizeMM(RoverProductCamera camera)
        {
            switch (camera)
            {
                case RoverProductCamera.NavcamLeft:
                    return 0.012; //source Maki, J.N., et al., Mars Exploration Rover Engineering Cameras, J. Geophys. Res., 108(E12), 8071, doi:10.1029/2003JE002077, 2003. (navcam uses same CCD)
                case RoverProductCamera.NavcamRight:
                    return 0.012; //source Maki, J.N., et al., Mars Exploration Rover Engineering Cameras, J. Geophys. Res., 108(E12), 8071, doi:10.1029/2003JE002077, 2003. (navcam uses same CCD)
                case RoverProductCamera.MastcamLeft:
                    return 0.0074; //calculated
                case RoverProductCamera.MastcamRight:
                    return 0.0074; //calculated
                default:
                    throw new NotImplementedException("sensor pixel size for camera " + camera + " not added yet");
            }
        }

        // Mastcam only
        public override double? GetMaximumFocusDistance(PDSMetadata metadata)
        {            
            if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE"))
            {
                return metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE");
            }
            return null;
        }

        public override double GetMinimumFocusDistance(PDSMetadata metadata)
        {          
            if (metadata.ReadAsString("INSTRUMENT_HOST_ID") == "MSL")
            {
                if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE"))
                {
                    double nearFocus = metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE");

                    if (metadata.HasKey("INSTRUMENT_ID"))
                    {
                        string instrumentId = metadata.ReadAsString("INSTRUMENT_ID");

                        if (instrumentId.StartsWith("MAHLI"))
                        {
                            nearFocus /= 1000.0; //mahli is in millimeters
                        }
                    }

                    return nearFocus;
                }
            }
            return 0;
        }
    }

    public class MissionM2020 : MissionSpecific
    {
        // ROASTT: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame.
        public override string RoverMotionCounter(PDSParser parser)
        {          
            return ((M2020OPGSProductId)parser.ProductId).GetConcatenatedTimeString();
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

            RoverProductCamera roverProdCam = GetRoverProductCamera(parser.InstrumentId);
            if (IsHazcam(roverProdCam))
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

        public override RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            switch (cam)
            {
                case RoverProductCamera.MastcamLeft: return RoverProductCamera.MastcamZLeft;
                case RoverProductCamera.MastcamRight: return RoverProductCamera.MastcamZRight;
                default: return cam;
            }
        }

        public override RoverProductCamera GetRoverProductCamera(string instrumentId)
        {
            if (instrumentId.StartsWith("FHAZ_LEFT"))
            {
                return RoverProductCamera.FrontHazcamLeft;
            }
            else if (instrumentId.StartsWith("FHAZ_RIGHT"))
            {
                return RoverProductCamera.FrontHazcamRight;
            }
            else if (instrumentId.StartsWith("RHAZ_LEFT"))
            {
                return RoverProductCamera.RearHazcamLeft;
            }
            else if (instrumentId.StartsWith("RHAZ_RIGHT"))
            {
                return RoverProductCamera.RearHazcamRight;
            }
            else if (instrumentId.StartsWith("NAVCAM_LEFT"))
            {
                return RoverProductCamera.NavcamLeft;
            }
            else if (instrumentId.StartsWith("NAVCAM_RIGHT"))
            {
                return RoverProductCamera.NavcamRight;
            }
            else if (instrumentId.StartsWith("MAST_LEFT"))
            {
                return RoverProductCamera.MastcamLeft;
            }
            else if (instrumentId.StartsWith("MAST_RIGHT"))
            {
                return RoverProductCamera.MastcamRight;
            }
            else if (instrumentId.StartsWith("MAHLI"))
            {
                return RoverProductCamera.MAHLI;
            }
            else if (instrumentId.StartsWith("MCZ_LEFT"))
            {
                return RoverProductCamera.MastcamZLeft;
            }
            else if (instrumentId.StartsWith("MCZ_RIGHT"))
            {
                return RoverProductCamera.MastcamZRight;
            }

            return RoverProductCamera.Unknown;
        }

        public override double GetFocalLengthMM(RoverProductCamera rovProdCam) { throw new NotImplementedException("focal lengths not implemented for 2020 instruments yet"); }
        public override double GetSensorPixelSizeMM(RoverProductCamera camera) { throw new NotImplementedException("sensor pixels size not implemented for 2020 instruments yet"); }

        public override double? GetMaximumFocusDistance(PDSMetadata metadata) { throw new NotImplementedException("max focus distance not implemented for 2020 instruments yet"); }
        public override double GetMinimumFocusDistance(PDSMetadata metadata) { throw new NotImplementedException("min focus distance not implemented for 2020 instruments yet"); }

        public override bool AllowPlacesDB()
        {
            return false;
        }
    }
}
