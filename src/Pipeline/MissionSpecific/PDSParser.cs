using System;
using System.Text;
using JPLOPS.Imaging;
using Microsoft.Xna.Framework;

namespace JPLOPS.Pipeline
{
    public class PDSParser
    {
        private const string UNKNOWN = "UNK";
        private const string NULL = "NULL";

        private static string[] ID_GROUPS = new string[] { RawMetadata.NULL_GROUP /* PDS */, "IDENTIFICATION" /*VIC*/ };

        private static string[] IMAGE_GROUPS = new string[] { "IMAGE" /* PDS */, "IMAGE_DATA" /* VIC */ };

        private static string[] DERIVED_IMAGE_GROUPS = new string[] { "DERIVED_IMAGE_PARMS" /* (sic) PDS and VIC */ };

        private static string[] IMAGE_REQUEST_GROUPS = new string[] { "IMAGE_REQUEST_PARMS" /* (sic) PDS and VIC */ };

        private static string[] VIDEO_REQUEST_GROUPS = new string[] { "VIDEO_REQUEST_PARMS" /* (sic) PDS and VIC */ };

        private static string[] CAMERA_FRAME_GROUPS = new string[]
        {
            "GEOMETRIC_CAMERA_MODEL", //MSL OPGS, M2020 OPGS (and MSSS?)
            "GEOMETRIC_CAMERA_MODEL_PARMS" //MSL MSSS
        };

        private static string[] ROVER_FRAME_GROUPS = new string[]
        {
            "ROVER_COORDINATE_SYSTEM",
            "ROVER_COORDINATE_SYSTEM_PARMS" //MSL MSSS
        };

        private static string[] SITE_FRAME_GROUPS = new string[]
        {
            "SITE_COORDINATE_SYSTEM",
            "SITE_COORDINATE_SYSTEM_PARMS" //MSL MSSS
        };

        private static string[] INSTRUMENT_STATE_GROUPS = new string[]
        {
            "INSTRUMENT_STATE_PARMS",
            "MINI_HEADER" //??
        };

        private static string[] HFOV_NAMES = new string[] { "AZIMUTH_FOV", "HORIZONTAL_FOV" };

        private static string[] VFOV_NAMES = new string[] { "ELEVATION_FOV", "VERTICAL_FOV" };

        private static string[] DOWNSAMPLE_NAMES = new string[] { "PIXEL_AVERAGING_WIDTH", "PIXEL_AVERAGING_HEIGHT" };

        public readonly PDSMetadata metadata;

        public PDSParser(PDSMetadata metadata)
        {
            this.metadata = metadata;
        }

        protected int GetInt(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsInt(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected double GetDouble(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsDouble(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected string GetString(string[] groups, string name, bool throwOnFail = true)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsString(g, name);
                }
            }
            if (throwOnFail)
            {
                throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
            }
            return null;
        }

        protected int[] GetIntArray(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsIntArray(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected float[] GetFloatArray(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsFloatArray(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected double[] GetDoubleArray(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsDoubleArray(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected DateTime GetDateTime(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return metadata.ReadAsDateTime(g, name);
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected Vector3 GetVector(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    return new Vector3(metadata.ReadAsDoubleArray(g, name));
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected Quaternion GetQuaternion(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    double[] qvals = metadata.ReadAsDoubleArray(g, name);
                    // IMG stores quaternions in WXYZ order but our class needs them in XYZW
                    return new Quaternion(qvals[1], qvals[2], qvals[3], qvals[0]);                       
                }
            }
            throw new PDSParserException(name + " not found in " + string.Join(", ", groups));
        }

        protected bool IsRelativeToSite(string[] groups, int site, bool def = true)
        {
            string rcsn = "REFERENCE_COORD_SYSTEM_NAME"; 
            string rcsi = "REFERENCE_COORD_SYSTEM_INDEX";
            foreach (var g in groups)
            {
                if (metadata.HasGroup(g))
                {
                    if (metadata.HasKey(g, rcsn))
                    {
                        if (metadata.ReadAsString(g, rcsn) != "SITE_FRAME")
                        {
                            return false;
                        }
                        if (metadata.HasKey(g, rcsi))
                        {
                            if (metadata.ReadAsInt(g, rcsi) != site)
                            {
                                return false;
                            }
                            return true;
                        }
                    }
                }
            }
            return def;
        }

        protected bool HasConstant(string[] groups, string name)
        {
            foreach (var g in groups)
            {
                if (metadata.HasKey(g, name))
                {
                    string v = metadata.ReadAsString(g, name);
                    if (v != UNKNOWN && v != NULL)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected T GetEnum<T>(string[] groups, string name, T def) where T : struct
        {
            string str = GetString(groups, name, throwOnFail: false);
            return  (!string.IsNullOrEmpty(str) && Enum.TryParse<T>(str, true, out T val)) ? val : def;
        }

        protected double GetRadians(string[] groups, string[] names)
        {
            string group = null, name = null;
            string val = null;
            foreach (var g in groups)
            {
                foreach (var n in names)
                {
                    if (metadata.HasKey(g, n))
                    {
                        group = g;
                        name = n;
                        val = metadata.ReadAsString(g, n);
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(val))
            {
                throw new PDSParserException(string.Join(", ", names) + " not found in " + string.Join(", ", groups));
            }
            string[] tok = val.Split();
            string units = "deg"; //default to degrees, e.g. M20 VIC may not have units
            if (tok.Length > 1)
            {
                val = tok[0];
                units = tok[1];
            }
            if (metadata.HasKey(group, name + "__UNIT")) //MSL VIC
            {
                units = metadata.ReadAsString(group, name + "__UNIT");
            }
            switch (units.ToLower())
            {
                case "deg": case "<deg>": return MathHelper.ToRadians(double.Parse(val));
                case "rad": case "<rad>": return double.Parse(val);
                default: throw new PDSParserException($"unknown units \"{units}\" for {group} {name} {val}");
                    
            }
        }

        public string ProductIdString { get { return GetString(ID_GROUPS, "PRODUCT_ID"); } }

        public DateTime ProductCreationTime { get { return GetDateTime(ID_GROUPS, "PRODUCT_CREATION_TIME"); } }

        public double SpacecraftClock { get { return GetDouble(ID_GROUPS, "SPACECRAFT_CLOCK_START_COUNT"); } }

        public int PlanetDayNumber { get { return GetInt(ID_GROUPS, "PLANET_DAY_NUMBER"); } }

        public string InstrumentId { get { return GetString(ID_GROUPS, "INSTRUMENT_ID"); } }

        public RoverProductGeometry GeometricProjection
        {
            get
            {
                return GetEnum<RoverProductGeometry>(ID_GROUPS, "GEOMETRY_PROJECTION_TYPE",
                                                     RoverProductGeometry.Unknown);
            }
        }

        public RoverProductSize ImageSizeType
        {
            get
            {
                return GetEnum<RoverProductSize>(ID_GROUPS, "IMAGE_TYPE", RoverProductSize.Unknown);
            }
        }

        public int[] MotionCounter { get { return GetIntArray(ID_GROUPS, "ROVER_MOTION_COUNTER"); } }

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

        public string SiteDrive
        {
            get
            {
                int[] mc = MotionCounter;
                if(mc == null)
                {
                    return null;
                }
                return (new SiteDrive(mc[0], mc[1])).ToString();
            }
        }

        public int Site { get { return MotionCounter[0]; } }

        public int Drive { get { return MotionCounter[1]; } }

        public int FirstLine { get { return GetInt(IMAGE_GROUPS, "FIRST_LINE"); } }

        public int FirstSample { get { return GetInt(IMAGE_GROUPS, "FIRST_LINE_SAMPLE"); } }

        public bool HasMissingConstant { get { return HasConstant(IMAGE_GROUPS, "MISSING_CONSTANT"); } }

        public float[] MissingConstant { get { return GetFloatArray(IMAGE_GROUPS, "MISSING_CONSTANT"); } }

        public bool HasInvalidConstant { get { return HasConstant(IMAGE_GROUPS, "INVALID_CONSTANT"); } }

        public float[] InvalidConstant { get { return GetFloatArray(IMAGE_GROUPS, "INVALID_CONSTANT"); } }

        public RoverProductProducer ProducingInstitution
        {
            get
            {
                string inst = GetString(ID_GROUPS, "PRODUCER_INSTITUTION_NAME", throwOnFail: false);
                inst = inst ?? GetString(ID_GROUPS, "INSTITUTION_NAME", throwOnFail: false); //MSL MSSS
                if (!string.IsNullOrEmpty(inst))
                {
                    if (inst.Contains("MULTIMISSION INSTRUMENT PROCESSING"))
                    {
                        return RoverProductProducer.OPGS;
                    }
                    if (inst.Contains("MALIN SPACE SCIENCE SYSTEMS"))
                    {
                        return RoverProductProducer.MSSS;
                    }
                }
                return RoverProductProducer.Unknown;
            }
        }

        // navcam and hazcam only
        public double ExposureDuration { get { return GetDouble(INSTRUMENT_STATE_GROUPS, "EXPOSURE_DURATION"); } }

        // mastcam only
        public int FilterNumber { get { return GetInt(INSTRUMENT_STATE_GROUPS, "FILTER_NUMBER"); } }

        /// ROVER to LOCAL_LEVEL rotation
        public Quaternion RoverOriginRotation
        {
            get
            {
                return GetQuaternion(ROVER_FRAME_GROUPS, "ORIGIN_ROTATION_QUATERNION");
            }
        }

        /// LOCAL_LEVEL (and ROVER) to SITE translation
        public Vector3 OriginOffset { get { return GetVector(ROVER_FRAME_GROUPS, "ORIGIN_OFFSET_VECTOR"); } }

        //check if this image has a rover coordinate system
        //and if so check if that rover coordinate system is relative to the site of the image
        public bool RoverCoordinateSystemRelativeToSite { get { return IsRelativeToSite(ROVER_FRAME_GROUPS, Site); } }

        public bool HasSiteCoordinateSystem { get { return metadata.HasGroup("SITE_COORDINATE_SYSTEM"); } }

        public Vector3 OffsetToPreviousSite
        {
            get
            {
                if (Site < 2) //landing is site 1
                {
                    return Vector3.Zero;
                }
                int prevSite = Site - 1;
                if (!IsRelativeToSite(SITE_FRAME_GROUPS, prevSite))
                {
                    throw new PDSParserException("site frame is not relative to previous site " + prevSite);
                }
                return GetVector(SITE_FRAME_GROUPS, "ORIGIN_OFFSET_VECTOR");
            }
        }

        public enum ReferenceCoordinateFrame { RoverNav, LocalLevel, Site }

        private ReferenceCoordinateFrame ParseFrame(string frame)
        {
            switch (frame)
            {
                case "ROVER_NAV_FRAME": return ReferenceCoordinateFrame.RoverNav;
                case "LOCAL_LEVEL_FRAME": return ReferenceCoordinateFrame.LocalLevel;
                case "SITE_FRAME": return ReferenceCoordinateFrame.Site;
                default: throw new PDSParserException("unknown REFERENCE_COORDINATE_SYSTEM " + frame);
            }
        }

        public ReferenceCoordinateFrame CameraModelRefFrame
        {
            get
            {
                return ParseFrame(GetString(CAMERA_FRAME_GROUPS, "REFERENCE_COORD_SYSTEM_NAME"));
            }
        }

        //this is the RDR type, e.g. XYZ, UVW, RAS, etc
        public RoverProductType DerivedImageType
        {
            get
            {
                var s = GetString(DERIVED_IMAGE_GROUPS, "DERIVED_IMAGE_TYPE", throwOnFail: false);
                return !string.IsNullOrEmpty(s) ? RoverProduct.FromPDSDerivedImageType(s) : RoverProductType.Unknown;
            }
        }

        //only some RDR types have this, e.g. XYZ, UVW, RAS
        public ReferenceCoordinateFrame DerivedImageRefFrame
        {
            get
            {
                return ParseFrame(GetString(DERIVED_IMAGE_GROUPS, "REFERENCE_COORD_SYSTEM_NAME"));
            }
        }

        //I think this will only work for RNG products (range maps)
        public Vector3 RangeOrigin
        {
            get
            {
                return new Vector3(GetDoubleArray(DERIVED_IMAGE_GROUPS, "RANGE_ORIGIN_VECTOR"));
            } 
        }

        public double HorizontalFOV { get { return GetRadians(INSTRUMENT_STATE_GROUPS, HFOV_NAMES); } }

        public double VerticalFOV { get { return GetRadians(INSTRUMENT_STATE_GROUPS, VFOV_NAMES); } }

        /// <summary>
        /// Indicates that the image was only partially transmitted (i.e. image checksum failed).
        /// The image may contains regions of 0 value.
        /// </summary>
        public bool IsPartial
        {
            get
            {
                string key = "PRODUCT_COMPLETION_STATUS"; //MSL VIC, M2020
                if (!metadata.HasKey(key))
                {
                    key = "MSL:" + key; //MSL PDS
                }
                if (!metadata.HasKey(key))
                {
                    return false;
                }
                return metadata.ReadAsString(key) == "PARTIAL";
            }
        }

        public bool IsDownsampled
        {
            get
            {             
                foreach (var name in DOWNSAMPLE_NAMES)
                {
                    string v = GetString(INSTRUMENT_STATE_GROUPS, name, throwOnFail: false);
                    if (!string.IsNullOrEmpty(v) && v != UNKNOWN && int.Parse(v) > 1)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        //MSSS doesn't put this flag in there
        public bool IsSunFinding
        {
            get
            {
                return GetString(IMAGE_REQUEST_GROUPS, "SOURCE_ID", throwOnFail: false) == "SUN";
            }
        }

        public bool IsVideoFrame
        {
            get
            {
                string flag = GetString(VIDEO_REQUEST_GROUPS, "GROUP_APPLICABILITY_FLAG", throwOnFail: false);
                return !string.IsNullOrEmpty(flag) && flag.ToUpper() == "TRUE";
            }
        }
    }
}
