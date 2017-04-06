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
                else
                {
                    RoverProductCamera inst = Camera;
                    if(inst == RoverProductCamera.MastcamLeft || inst == RoverProductCamera.MastcamRight || inst == RoverProductCamera.MAHLI)
                    {
                        return RoverProductType.Image;
                    }
                }
                return RoverProductType.Unknown;            
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

        public bool IsMastcam
        {
            get
            {
                return Camera == RoverProductCamera.MastcamLeft || Camera == RoverProductCamera.MastcamLeft;
            }
        }

        /// <summary>
        /// Indicates whether or not this image was captured with a navigation camera.
        /// </summary>
        public bool IsNavcam
        {
            get
            {
                return Camera == RoverProductCamera.NavcamLeft || Camera == RoverProductCamera.NavcamRight;
            }
        }

        public bool IsDownsampled
        {
            get
            {
                if (metadata.ReadAsString("IMAGE_REQUEST_PARMS", "PIXEL_DOWNSAMPLE_OPTION") != "NONE")
                {
                    int avgWidth = metadata.ReadAsInt("IMAGE_REQUEST_PARMS", "PIXEL_AVERAGING_WIDTH");
                    int avgHeight = metadata.ReadAsInt("IMAGE_REQUEST_PARMS", "PIXEL_AVERAGING_HEIGHT");
                    return avgWidth > 1 || avgHeight > 1;
                      
                }
                return false;
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
