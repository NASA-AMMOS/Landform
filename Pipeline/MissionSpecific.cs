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

        public virtual bool UseForReconstruction(PDSParser parser)
        {
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
            if (!UseHazcamForReconstruction() && IsHazcam(roverProdCam))
            {
                return false;
            }

            if (!UseMastcamForReconstruction() && IsMastcam(roverProdCam))
            {
                return false;
            }

            return true;
        }

        public abstract int DayNumber(PDSParser parser);

        public class RoverObservationComparator : IComparer<RoverObservation>
        {
            private string pointsType = ObservationType.Points.ToString(), rangeType = ObservationType.Range.ToString();
            private string msss = RoverProductProducer.MSSS.ToString(), opgs = RoverProductProducer.OPGS.ToString();
            private bool preferMSSSToOPGS, preferLinearToNonlinear;

            public RoverObservationComparator(bool preferMSSSToOPGS, bool preferLinearToNonlinear)
            {
                this.preferMSSSToOPGS = preferMSSSToOPGS;
                this.preferLinearToNonlinear = preferLinearToNonlinear;
            }

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
                
                // sort next by producer
                if (a.Producer == msss && b.Producer == opgs)
                {
                    return preferMSSSToOPGS ? -1 : 1;
                }
                if (a.Producer == opgs && b.Producer == msss)
                {
                    return preferMSSSToOPGS ? 1 : -1;
                }

                // sort next by linear-ness, prefer linear
                var linearA = a.IsLinear();
                var linearB = b.IsLinear();
                if (linearA && !linearB)
                {
                    return preferLinearToNonlinear ? -1 : 1;
                }
                if (!linearA && linearB)
                {
                    return preferLinearToNonlinear ? 1 : -1;
                }

                // finally sort by version, prefer higer versions
                // versions go numeric 1 to 9, A-Z, _ (opgs) and numeric 0 to 9, A-Z (msss)
                return (int)b.Version[0] - (int)a.Version[0];
            }
        }

        /// <summary>
        /// ordering a sequence with this function should put the "better" observations earlier in the list
        /// thus a "better" observation should be *less than* a "worse" observation
        /// uses PreferMSSSToOPGS() and PreferLinearToNonlinear()
        /// so if a mission only differs from the default in one of those respects, just override that
        /// </summary>
        public virtual IComparer<RoverObservation> GetRoverObservationComparator()
        {
            return new RoverObservationComparator(PreferMSSSToOPGS(), PreferLinearToNonlinear());
        }

        public virtual RoverProductGeometry[] GetLinearPreference()
        {
            if (!AllowLinear() && !AllowNonlinear())
            {
                return new RoverProductGeometry[] {}; //yeah...
            }

            if (!AllowLinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Raw };
            }

            if (!AllowNonlinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Linearized };
            }

            if (PreferLinearToNonlinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Linearized, RoverProductGeometry.Raw };
            }

            return new RoverProductGeometry[] { RoverProductGeometry.Raw, RoverProductGeometry.Linearized };
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

        /// <summary>
        /// whether to ingest OPGS images
        /// </summary>
        public virtual bool AllowOPGS()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest MSSS images
        /// </summary>
        public virtual bool AllowMSSS()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest thumbnail images
        /// </summary>
        public virtual bool AllowThumbnails()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest partially downloaded images
        /// </summary>
        public virtual bool AllowPartialDownloads()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest sun finding images
        /// </summary>
        public virtual bool AllowSunFinding()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest linearized images
        /// </summary>
        public virtual bool AllowLinear()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest non-linearized images
        /// ISSUE #353: need to validate that alignment works across cameras with non-linearized images
        /// </summary>
        public virtual bool AllowNonlinear()
        {
            return false;
        }

        /// <summary>
        /// whether to prefer MSSS images to OPGS images when both are available
        /// default is to prefer MSSS "because people like the colors better"
        /// </summary>
        public virtual bool PreferMSSSToOPGS()
        {
            return true;
        }

        /// <summary>
        /// whether to prefer linear to nonlinear images when both are available
        /// </summary>
        public virtual bool PreferLinearToNonlinear()
        {
            return true;
        }

        /// <summary>
        /// whether to use hazcam images for reconstruction
        /// </summary>
        public virtual bool UseHazcamForReconstruction()
        {
            return false;
        }

        /// <summary>
        /// whether to use mastcam images for reconstruction
        /// </summary>
        public virtual bool UseMastcamForReconstruction()
        {
            return true;
        }

        /// <summary>
        /// Check if we should even bother downloading or ingesting based on filename.
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckFilename(string filename)
        {
            RoverProductId id = RoverProductId.ParseFromString(filename);

            if (id == null)
            {
                return false;
            }

            if (id.Camera == RoverProductCamera.Unknown)
            {
                return false;
            }

            if (id.ProductType == RoverProductType.Unknown || !Observation.AllowedProductType(id.ProductType))
            {
                return false;
            }

            if (!AllowOPGS() && id.Producer == RoverProductProducer.OPGS)
            {
                return false;
            }

            if (!AllowMSSS() && id.Producer == RoverProductProducer.MSSS)
            {
                return false;
            }

            if (!AllowThumbnails() && id.Producer == RoverProductProducer.OPGS &&
                ((OPGSProductId)id).Size != RoverProductSize.Regular)
            {
                return false;
            }

            if (!AllowLinear() && id.Geometry == RoverProductGeometry.Linearized)
            {
                return false;
            }

            if (!AllowNonlinear() && id.Geometry != RoverProductGeometry.Linearized)
            {
                return false;
            }

            if (id.Producer == RoverProductProducer.MSSS)
            {
                // Check that this is a DCX file
                MSSSProductId msssId = (MSSSProductId)id;
                if (!msssId.RadiometricallyCalibrated || !msssId.ColorCorrected || !msssId.Decompressed)
                {
                    return false;
                }
                // Filter for color or black and white jpegs that are not thumbnails
                if(msssId.MSSSProductType == MSSSProductType.Unknown)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Mostly just confirms what CheckFilename() did using metadata instead of the filename
        /// but some things are only checked by one or the other
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckMetadata(PDSParser parser)
        {
            if (GetRoverProductCamera(parser.InstrumentId) == RoverProductCamera.Unknown)
            {
                return false;
            }

            if (!AllowPartialDownloads() && parser.IsPartial)
            {
                return false;
            }

            var pt = parser.DerivedImageType;
            if (pt == RoverProductType.Unknown || !Observation.AllowedProductType(pt))
            {
                return false;
            }

            if (!AllowOPGS() && parser.ProducingInstitution == RoverProductProducer.OPGS)
            {
                return false;
            }

            if (!AllowMSSS() && parser.ProducingInstitution == RoverProductProducer.MSSS)
            {
                return false;
            }

            if (!AllowThumbnails() && parser.ImageSizeType != RoverProductSize.Regular)
            {
                return false;
            }

            if (!AllowLinear() && parser.GeometricProjection == RoverProductGeometry.Linearized)
            {
                return false;
            }

            if (!AllowNonlinear() && parser.GeometricProjection != RoverProductGeometry.Linearized)
            {
                return false;
            }

            if (!AllowSunFinding() && parser.IsSunFinding)
            {
                return false;
            }

            return true;
        }

        public abstract RoverProductCamera GetRoverProductCamera(string instrumentId);

        public virtual string CameraName(PDSParser parser)
        {
            return GetRoverProductCamera(parser.InstrumentId).ToString();
        }

        public virtual string GetObservationFrameName(PDSParser parser)
        {
            return string.Format("{0}_{1}", CameraName(parser), RoverMotionCounter(parser));
        }
        
        public abstract double GetSensorPixelSizeMM(RoverProductCamera camera);
        public abstract double GetFocalLengthMM(RoverProductCamera camera);

        public abstract double GetMinimumFocusDistance(PDSMetadata metadata);
        public abstract double? GetMaximumFocusDistance(PDSMetadata metadata);

        /// <summary>
        /// whether to allow priors from MSLLocations
        /// </summary>
        public virtual bool AllowLocationsDB()
        {
            return false;
        }

        /// <summary>
        /// whether to allow priors from the Places database
        /// </summary>
        public virtual bool AllowPlacesDB()
        {
            return true;
        }
             
        /// <summary>
        /// whether to allow priors from the OnSight legacy manifest
        /// </summary>
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
            if (!base.UseForReconstruction(parser))
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
                double? maxFocusDistance = GetMaximumFocusDistance(parser.metadata as PDSMetadata);
                if (maxFocusDistance.HasValue && maxFocusDistance < MIN_MASTCAM_FOCUS_CUTOFF)
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

        /// <summary>
        /// ROASTT: for some images the INSTRUMENT_ID says LEFT when it should say RIGHT, so use PRODUCT_ID instead
        /// </summary>
        public override string CameraName(PDSParser parser)
        {
            return TranslateCamera(parser.ProductId.Camera).ToString();
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
