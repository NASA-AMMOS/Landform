using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{

    public enum PDSInstrument
    {
        Unknown,
        FrontHazcamLeft,
        FrontHazcamRight,
        RearHazcamLeft,
        RearHazcamRight,
        NavcamLeft,
        NavcamRight,
        MastcamLeft,
        MastcamRight,
        MAHLI
    }

    public enum PDSGeometryProjection
    {
        Unknown,
        Raw,
        Linearized
    }

    public enum PDSImageSizeType
    {
        Unknown,
        Regular,
        Thumbnail
    }
    public enum PDSDerivedImageType
    {
        Unknown,
        Image,
        Range,
        RoverMask,
        ReachabilityMap,
        XYZ,
        RangeErrorMap,
        NormalMap,
        XYZErrorMap
    }

    public enum PDSInstitution
    {
        Unknown,
        OPGS,
        MSSS
    }

    public class PDSParser
    {

        PDSMetadata metadata;

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
            get { return metadata.ReadAsInt("IMAGE", "FIRST_LINE"); }
        }

        public int FirstSample
        {
            get { return metadata.ReadAsInt("IMAGE", "FIRST_LINE_SAMPLE"); }
        }

        public PDSProductID ProductId
        {
            get { return PDSProductID.ParseFromString(metadata.ReadAsString("PRODUCT_ID")); }
        }

        public double SpacecraftClock
        {
            get
            {
                return metadata.ReadAsDouble("SPACECRAFT_CLOCK_START_COUNT");
            }
        }

        public PDSInstrument Instrument
        {
            get
            {
                if (metadata.HasKey("INSTRUMENT_ID"))
                {
                    string id = metadata.ReadAsString("INSTRUMENT_ID");
                    if (id.StartsWith("FHAZ_LEFT"))
                    {
                        return PDSInstrument.FrontHazcamLeft;
                    }
                    else if (id.StartsWith("FHAZ_RIGHT"))
                    {
                        return PDSInstrument.FrontHazcamRight;
                    }
                    else if (id.StartsWith("RHAZ_LEFT"))
                    {
                        return PDSInstrument.RearHazcamLeft;
                    }
                    else if (id.StartsWith("RHAZ_RIGHT"))
                    {
                        return PDSInstrument.RearHazcamRight;
                    }
                    else if (id.StartsWith("NAV_LEFT"))
                    {
                        return PDSInstrument.NavcamLeft;
                    }
                    else if (id.StartsWith("NAV_RIGHT"))
                    {
                        return PDSInstrument.NavcamRight;
                    }
                    else if (id.StartsWith("MAST_LEFT"))
                    {
                        return PDSInstrument.MastcamLeft;
                    }
                    else if (id.StartsWith("MAST_RIGHT"))
                    {
                        return PDSInstrument.MastcamRight;
                    }
                    else if (id.StartsWith("MAHLI"))
                    {
                        return PDSInstrument.MAHLI;
                    }
                }
                return PDSInstrument.Unknown;
            }
        }

        public PDSGeometryProjection GeometricProjection
        {
            get
            {
                if (metadata.HasKey("GEOMETRY_PROJECTION_TYPE"))
                {
                    string geoType = metadata.ReadAsString("GEOMETRY_PROJECTION_TYPE");
                    if (geoType == "RAW")
                    {
                        return PDSGeometryProjection.Raw;
                    }
                    else if (geoType == "LINEARIZED")
                    {
                       return PDSGeometryProjection.Linearized;
                    }
                }
                return PDSGeometryProjection.Unknown;
            }
        }

        public PDSImageSizeType ImageSizeType
        {
            get
            {
                if (metadata.HasKey("IMAGE_TYPE"))
                {
                    string imageType = metadata.ReadAsString("IMAGE_TYPE");
                    if (imageType == "REGULAR")
                    {
                        return PDSImageSizeType.Regular;
                    }
                    else if (imageType == "THUMBNAIL")
                    {
                      return PDSImageSizeType.Thumbnail;
                    }
                }
                return PDSImageSizeType.Unknown;
            }          
        }

        public PDSDerivedImageType DerivedImageType
        {
            get
            {
                if (metadata.HasKey("DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE"))
                {
                    string imageType = metadata.ReadAsString("DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE");
                    if (imageType == "IMAGE")
                    {
                       return PDSDerivedImageType.Image;
                    }
                    else if (imageType == "MASK")
                    {
                       return PDSDerivedImageType.RoverMask;
                    }
                    else if (imageType == "RANGE_MAP")
                    {
                       return PDSDerivedImageType.Range;
                    }
                    else if (imageType == "REACHABILITY_MAP")
                    {
                       return PDSDerivedImageType.ReachabilityMap;
                    }
                    else if (imageType == "XYZ_MAP")
                    {
                       return PDSDerivedImageType.XYZ;
                    }
                    else if (imageType == "RANGE_ERROR_MAP")
                    {
                       return PDSDerivedImageType.RangeErrorMap;
                    }
                    else if (imageType == "UVW_MAP")
                    {
                       return PDSDerivedImageType.NormalMap;
                    }
                    else if (imageType == "XYZ_ERROR_MAP")
                    {
                       return PDSDerivedImageType.XYZErrorMap;
                    }
                }
                else
                {
                    PDSInstrument inst = Instrument;
                    if(inst == PDSInstrument.MastcamLeft || inst == PDSInstrument.MastcamRight || inst == PDSInstrument.MAHLI)
                    {
                        return PDSDerivedImageType.Image;
                    }
                }
                return PDSDerivedImageType.Unknown;            
            }
        }

        public PDSInstitution ProducingInstitution
        {
            get
            {
                if (metadata.HasKey("PRODUCER_INSTITUTION_NAME") && metadata.ReadAsString("PRODUCER_INSTITUTION_NAME").Contains("MULTIMISSION INSTRUMENT PROCESSING"))
                {
                    return PDSInstitution.OPGS;
                }
                else if (metadata.HasKey("INSTITUTION_NAME") && metadata.ReadAsString("INSTITUTION_NAME").Contains("MALIN SPACE SCIENCE SYSTEMS"))
                {
                    return PDSInstitution.MSSS;
                }
                return PDSInstitution.Unknown;
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
        public int FilterNumber
        {
            get
            {    
                 return metadata.ReadAsInt("IMAGE_REQUEST_PARMS", "FILTER_NUMBER");
            }
        }

        // Mastcam only
        public double MaximumFocusDistance
        {
            get
            {
                return metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE");
            }
        }

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
                return metadata.ReadAsIntArray("ROVER_MOTION_COUNTER");
            }
        }

        public string SiteDrive
        {
            get
            {
                int[] mc = MotionCounter;
                return mc[0].ToString().PadLeft(5, '0') + mc[1].ToString().PadLeft(5, '0');
            }
        }

        public int PlanetDayNumber
        {
            get
            {
                return metadata.ReadAsInt("PLANET_DAY_NUMBER");
            }
        }

        public RoverArticulation Articulation
        {
            get
            {
                return new PDSRoverArticulationParser(this.metadata).Parse();
            }
        }
    }
}
