using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class PDSParser
    {

        public PDSMetadata metadata;

        public PDSParser(PDSMetadata metadata)
        {
            this.metadata = metadata;
        }

        public DateTime ProductCreationTime
        { 
            get { return metadata.ReadAsDateTime("PRODUCT_CREATION_TIME"); }
        }

        public int FirstLine
        {
            get
            {
                if (metadata.HasKey("IMAGE", "FIRST_LINE"))
                {
                    return metadata.ReadAsInt("IMAGE", "FIRST_LINE");
                }
                return metadata.ReadAsInt("IMAGE_DATA", "FIRST_LINE");
            }
        }

        public int FirstSample
        {
            get {

                if (metadata.HasKey("IMAGE", "FIRST_LINE_SAMPLE"))
                {
                    return metadata.ReadAsInt("IMAGE", "FIRST_LINE_SAMPLE");
                }
                return metadata.ReadAsInt("IMAGE_DATA", "FIRST_LINE_SAMPLE");

            }
        }

        private const string Unknown = "UNK";
        private const string NullStr = "NULL";

        public bool HasMissingConstant
        {
            get { return metadata.HasKey("IMAGE", "MISSING_CONSTANT") &&
                    metadata.ReadAsString("IMAGE", "MISSING_CONSTANT") != Unknown &&
                    metadata.ReadAsString("IMAGE", "MISSING_CONSTANT") != NullStr;
            }
        }

        public double[] MissingConstant
        {
            get { return metadata.ReadAsDoubleArray("IMAGE", "MISSING_CONSTANT"); }
        }

        public bool HasInvalidConstant
        {
            get { return metadata.HasKey("IMAGE", "INVALID_CONSTANT") && metadata.ReadAsString("IMAGE", "INVALID_CONSTANT") != Unknown; }
        }

        public float InvalidConstant
        {
            get { return (float)metadata.ReadAsDouble("IMAGE", "INVALID_CONSTANT"); }
        }

        public RoverProductId ProductId
        {
            get { return RoverProductId.ParseFromString(metadata.ReadAsString("PRODUCT_ID")); }
        }

        public string ProductIdString
        {
            get { return metadata.ReadAsString("PRODUCT_ID"); }
        }

        public double SpacecraftClock
        {
            get
            {
                return metadata.ReadAsDouble("SPACECRAFT_CLOCK_START_COUNT");
            }
        }

        static public double GetFocalLengthMM(RoverProductCamera camera)
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
        public double FocalLengthMM
        {
            get
            {
               return GetFocalLengthMM(Camera);
            }
        }

        static public double GetSensorPixelSizeMM(RoverProductCamera camera)
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
        public double SensorPixelSizeMM
        {
            get
            {
                return GetSensorPixelSizeMM(Camera);
            }
        }

        public RoverProductCamera Camera
        {
            get
            {
                if (metadata.HasKey("INSTRUMENT_ID"))
                {
                    string id = metadata.ReadAsString("INSTRUMENT_ID");
                    if (id.StartsWith("FHAZ_LEFT"))
                    {
                        return RoverProductCamera.FrontHazcamLeft;
                    }
                    else if (id.StartsWith("FHAZ_RIGHT"))
                    {
                        return RoverProductCamera.FrontHazcamRight;
                    }
                    else if (id.StartsWith("RHAZ_LEFT"))
                    {
                        return RoverProductCamera.RearHazcamLeft;
                    }
                    else if (id.StartsWith("RHAZ_RIGHT"))
                    {
                        return RoverProductCamera.RearHazcamRight;
                    }
                    else if (id.StartsWith("NAV_LEFT"))
                    {
                        return RoverProductCamera.NavcamLeft;
                    }
                    else if (id.StartsWith("NAV_RIGHT"))
                    {
                        return RoverProductCamera.NavcamRight;
                    }
                    else if (id.StartsWith("MAST_LEFT"))
                    {
                        return RoverProductCamera.MastcamLeft;
                    }
                    else if (id.StartsWith("MAST_RIGHT"))
                    {
                        return RoverProductCamera.MastcamRight;
                    }
                    else if (id.StartsWith("MAHLI"))
                    {
                        return RoverProductCamera.MAHLI;
                    }
                    else if(id.StartsWith("MCZ_LEFT"))
                    {
                        return RoverProductCamera.MastcamZLeft;
                    }
                    else if (id.StartsWith("MCZ_RIGHT"))
                    {
                        return RoverProductCamera.MastcamZRight;
                    }
                }
                return RoverProductCamera.Unknown;
            }
        }

        public RoverProductGeometry GeometricProjection
        {
            get
            {
                if (metadata.HasKey("GEOMETRY_PROJECTION_TYPE"))
                {
                    string geoType = metadata.ReadAsString("GEOMETRY_PROJECTION_TYPE");
                    if (geoType == "RAW")
                    {
                        return RoverProductGeometry.Raw;
                    }
                    else if (geoType == "LINEARIZED")
                    {
                       return RoverProductGeometry.Linearized;
                    }
                }
                return RoverProductGeometry.Unknown;
            }
        }

        public RoverProductSize ImageSizeType
        {
            get
            {
                if (metadata.HasKey("IMAGE_TYPE"))
                {
                    string imageType = metadata.ReadAsString("IMAGE_TYPE");
                    if (imageType == "REGULAR")
                    {
                        return RoverProductSize.Regular;
                    }
                    else if (imageType == "THUMBNAIL")
                    {
                      return RoverProductSize.Thumbnail;
                    }
                }
                return RoverProductSize.Unknown;
            }          
        }

        public RoverProductType DerivedImageType
        {
            get
            {
                if (metadata.HasKey("DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE"))
                {
                    string imageType = metadata.ReadAsString("DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE");
                    if (imageType == "IMAGE")
                    {
                       return RoverProductType.Image;
                    }
                    else if (imageType == "MASK")
                    {
                       return RoverProductType.RoverMask;
                    }
                    else if (imageType == "RANGE_MAP")
                    {
                       return RoverProductType.Range;
                    }
                    else if (imageType == "REACHABILITY_MAP")
                    {
                       return RoverProductType.ReachabilityMap;
                    }
                    else if (imageType == "XYZ_MAP")
                    {
                       return RoverProductType.XYZ;
                    }
                    else if (imageType == "RANGE_ERROR_MAP")
                    {
                       return RoverProductType.RangeErrorMap;
                    }
                    else if (imageType == "UVW_MAP")
                    {
                       return RoverProductType.NormalMap;
                    }
                    else if (imageType == "XYZ_ERROR_MAP")
                    {
                       return RoverProductType.XYZErrorMap;
                    }
                }
                else if(this.ProductId.ProductType != RoverProductType.Unknown)
                {
                    //fallback to filename
                    return this.ProductId.ProductType;
                }
                else
                { 
                    //ISSUE: #550
                    //fallback to instrument name and the incorrect hope there are no other products built with that camera
                    RoverProductCamera inst = Camera;
                    if(inst == RoverProductCamera.MastcamLeft || inst == RoverProductCamera.MastcamRight || inst == RoverProductCamera.MAHLI)
                    {
                        return RoverProductType.Image;
                    }
                }
                return RoverProductType.Unknown;
            }
        }

        public bool IsSunFinding
        {
            get
            {
                //MSSS doesn't put the flag in there
                return ProducingInstitution == RoverProductProducer.OPGS && metadata.ReadAsString("IMAGE_REQUEST_PARMS", "SOURCE_ID") == "SUN";
            }
        }

        public RoverProductProducer ProducingInstitution
        {
            get
            {
                if (metadata.HasKey("PRODUCER_INSTITUTION_NAME") && metadata.ReadAsString("PRODUCER_INSTITUTION_NAME").Contains("MULTIMISSION INSTRUMENT PROCESSING"))
                {
                    return RoverProductProducer.OPGS;
                }
                else if (metadata.HasKey("INSTITUTION_NAME") && metadata.ReadAsString("INSTITUTION_NAME").Contains("MALIN SPACE SCIENCE SYSTEMS"))
                {
                    return RoverProductProducer.MSSS;
                }
                return RoverProductProducer.Unknown;
            }
        }

        // Nav and Haz cam only
        public double ExposureDuration
        {
            get
            {
                return metadata.ReadAsDouble("INSTRUMENT_STATE_PARMS", "EXPOSURE_DURATION");               
            }
        }

        // Mastcam only
        public int? FilterNumber
        {
            get
            {
                if (metadata.HasKey("INSTRUMENT_STATE_PARMS", "FILTER_NUMBER"))
                {
                    return metadata.ReadAsInt("INSTRUMENT_STATE_PARMS", "FILTER_NUMBER");
                }
                if (metadata.HasKey("MINI_HEADER", "FILTER_NUMBER"))
                {
                    return metadata.ReadAsInt("MINI_HEADER", "FILTER_NUMBER");
                }
                return null;
            }
        }

        // Mastcam only
        public double? MaximumFocusDistance
        {
            get
            {
                if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE"))
                {
                    return metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE");
                }
                return null;
            }
        }

        public double MinimumFocusDistance
        {
            get
            {
                if (metadata.ReadAsString("INSTRUMENT_HOST_ID") == "MSL")
                {
                    if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE"))
                    {
                        double nearFocus = metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE");
                 
                        if (Camera == RoverProductCamera.MAHLI)
                        {
                            nearFocus /= 1000.0; //mahli is in millimeters
                        }

                        return nearFocus;
                    }
                }
                return 0;
            }
        }


        /// <summary>
        /// Rover to local level
        /// </summary>
        public Quaternion RoverOriginRotation
        {
            get
            {
                foreach (string group in new string[] { "ROVER_COORDINATE_SYSTEM", "ROVER_COORDINATE_SYSTEM_PARMS" })
                {
                    if (metadata.HasGroup(group))
                    {
                        double[] qvals = metadata.ReadAsDoubleArray(group, "ORIGIN_ROTATION_QUATERNION");
                        // IMG stores quaternions in WXYZ order but our class needs them in XYZW
                        return new Quaternion(qvals[1], qvals[2], qvals[3], qvals[0]);                       
                    }
                }
                throw new PDSParserException("ORIGIN_ROTATION_QUATERNION not found");
            }
        }

        public Vector3 OriginOffset
        {
            get
            {
                foreach (string group in new string[] { "ROVER_COORDINATE_SYSTEM", "ROVER_COORDINATE_SYSTEM_PARMS" })
                {
                    if (metadata.HasGroup(group))
                    {
                        double[] offset = metadata.ReadAsDoubleArray(group, "ORIGIN_OFFSET_VECTOR");
                        return new Vector3(offset);
                    }
                }
                throw new PDSParserException("ORIGIN_OFFSET_VECTOR not found");
            }
        }

        public int[] MotionCounter
        {
            get
            {
                if (metadata.HasKey("IDENTIFICATION", "ROVER_MOTION_COUNTER"))
                {
                    return metadata.ReadAsIntArray("IDENTIFICATION", "ROVER_MOTION_COUNTER");
                }
                if (metadata.HasKey("ROVER_MOTION_COUNTER"))
                {
                    return metadata.ReadAsIntArray("ROVER_MOTION_COUNTER");
                }
                return null;
            }
        }

        public string SiteDrive
        {
            get
            {
                int[] mc = MotionCounter;
                if(mc == null)
                {
                    return null;
                }
                return mc[0].ToString().PadLeft(5, '0') + mc[1].ToString().PadLeft(5, '0');
            }
        }

        public int Site
        {
            get { return MotionCounter[0]; }
        }

        public int Drive
        {
            get { return MotionCounter[1]; }
        }

        public string RMC
        {
            get
            {
                int[] mc = MotionCounter;
                StringBuilder builder = new StringBuilder();
                foreach(int i in mc)
                {
                    builder.Append(i.ToString().PadLeft(5, '0'));
                }
                return builder.ToString();
            }
        }

        public int PlanetDayNumber
        {
            get
            {
                return metadata.ReadAsInt("PLANET_DAY_NUMBER");
            }
        }


        /// <summary>
        /// Indicates that the image was only partially transmitted (i.e. image checksum failed).
        /// The image may contains regions of 0 value.
        /// </summary>
        public bool IsPartial
        {
            get
            {
                string key = "MSL:PRODUCT_COMPLETION_STATUS";
                if (!metadata.HasKey(key))
                    return false; // Assume full image if key missing
                return (string)metadata[key] == "PARTIAL";
            }
        }

        public bool IsDownsampled
        {
            get
            {             
                int avgWidth = 1;
                if (metadata.HasKey("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_WIDTH") &&
                    metadata.ReadAsString("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_WIDTH") != Unknown)
                {
                    avgWidth = metadata.ReadAsInt("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_WIDTH");
                }

                int avgHeight = 1;
                if (metadata.HasKey("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_HEIGHT") &&
                    metadata.ReadAsString("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_HEIGHT") != Unknown)
                {
                    avgHeight = metadata.ReadAsInt("INSTRUMENT_STATE_PARMS", "PIXEL_AVERAGING_HEIGHT");
                }

                return avgWidth > 1 || avgHeight > 1;
            }
        }

        public double HorizontalFOV
        {
            get
            {
                // Todo: write read angle: issues/465
                if (this.metadata.HasKey("INSTRUMENT_STATE_PARMS", "AZIMUTH_FOV"))
                {
                    return MathHelper.ToRadians(this.metadata.ReadAsDouble("INSTRUMENT_STATE_PARMS", "AZIMUTH_FOV"));
                }

                return MathHelper.ToRadians(this.metadata.ReadAsDouble("INSTRUMENT_STATE_PARMS", "HORIZONTAL_FOV"));
                
               
            }
        }

        public double VerticleFOV
        {
            get
            {
                // Todo: write read angle: issues/465

                if (this.metadata.HasKey("INSTRUMENT_STATE_PARMS", "ELEVATION_FOV"))
                {
                    return MathHelper.ToRadians(this.metadata.ReadAsDouble("INSTRUMENT_STATE_PARMS", "ELEVATION_FOV"));
                }
                return MathHelper.ToRadians(this.metadata.ReadAsDouble("INSTRUMENT_STATE_PARMS", "VERTICAL_FOV"));
                
            }
        }

        public enum ReferenceCoordinateFrame
        {
            RoverNav,
            LocalLevel,
            Site
        }

        private ReferenceCoordinateFrame GetReferenceCoordinateFrame(string group)
        {
            if (metadata.ReadAsString(group, "REFERENCE_COORD_SYSTEM_NAME") == "ROVER_NAV_FRAME")
                return ReferenceCoordinateFrame.RoverNav;
            else if (metadata.ReadAsString(group, "REFERENCE_COORD_SYSTEM_NAME") == "SITE_FRAME")
                return ReferenceCoordinateFrame.Site;
            else if (metadata.ReadAsString(group, "REFERENCE_COORD_SYSTEM_NAME") == "LOCAL_LEVEL_FRAME")
                return ReferenceCoordinateFrame.LocalLevel;
            else
                throw new PDSParserException("unknown reference coordinate system");
        }

        public ReferenceCoordinateFrame DerivedImageRefFrame
        {
            get
            {
                return GetReferenceCoordinateFrame("DERIVED_IMAGE_PARMS");
            }
        }


        public ReferenceCoordinateFrame CameraModelRefFrame
        {
            get
            {
                if(this.metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                {
                    //MSL/M2020 opgs
                    //M2020 MSSS
                    return GetReferenceCoordinateFrame("GEOMETRIC_CAMERA_MODEL");
                }
                else
                {
                    //MSL MSSS
                    return GetReferenceCoordinateFrame("GEOMETRIC_CAMERA_MODEL_PARMS"); //MSL Specific
                }
            }
        }
        
        public Vector3 RangeOrigin
        {
            get
            {
              double[] originVecv = metadata.ReadAsDoubleArray("DERIVED_IMAGE_PARMS", "RANGE_ORIGIN_VECTOR");
              return new Vector3(originVecv);
            } 
        }
    
    }
}
