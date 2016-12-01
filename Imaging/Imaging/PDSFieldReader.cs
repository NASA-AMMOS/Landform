using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{

    public class PDSFieldReader
    {

        public PDSFieldReader(PDSMetadata metadata)
        {
            // Or could do something like
            
            ParseBaseParameters(metadata);
            ParseImageParameters(metadata);
            ParseDerivedImageParamaters(metadata);
            ParseInstrumentStateParamaters(metadata);
            ParseImageRequestParamaters(metadata);
            ParseRoverCoordinateSystem(metadata);
            metadata.Articulation = new RoverArticulationParser(metadata).Parse();
            metadata.CameraModel = new PDSCameraModeParser(metadata).Parse();
        }

        void ParseBaseParameters(PDSMetadata metadata)
        {
            if (metadata.HasKey("RECORD_BYTES"))
            {
                metadata.RecordBytes = (long)ParseNumber(metadata["RECORD_BYTES"]);
            }
            if (metadata.HasKey("^IMAGE"))
            {
                metadata.Carrot = (int)ParseNumber(metadata["^IMAGE"]);
            }
            if (metadata.HasKey("ROVER_MOTION_COUNTER"))
            {
                metadata.MotionCounter = ParseNumberArray(metadata["ROVER_MOTION_COUNTER"]).Select(x => (int)x).ToArray();
                metadata.SiteDrive = metadata.MotionCounter[0].ToString().PadLeft(5, '0') + metadata.MotionCounter[1].ToString().PadLeft(5, '0');
            }
            if (metadata.HasKey("INSTRUMENT_ID"))
            {
                string id = ParseString(metadata["INSTRUMENT_ID"]);
                if (id.StartsWith("FHAZ_LEFT"))
                {
                    metadata.Instrument = PDSInstrument.FrontHazcamLeft;
                }
                else if (id.StartsWith("FHAZ_RIGHT"))
                {
                    metadata.Instrument = PDSInstrument.FrontHazcamRight;
                }
                else if (id.StartsWith("RHAZ_LEFT"))
                {
                    metadata.Instrument = PDSInstrument.RearHazcamLeft;
                }
                else if (id.StartsWith("RHAZ_RIGHT"))
                {
                    metadata.Instrument = PDSInstrument.RearHazcamRight;
                }
                else if (id.StartsWith("NAV_LEFT"))
                {
                    metadata.Instrument = PDSInstrument.NavcamLeft;
                }
                else if (id.StartsWith("NAV_RIGHT"))
                {
                    metadata.Instrument = PDSInstrument.NavcamRight;
                }
                else if (id.StartsWith("MAST_LEFT"))
                {
                    metadata.Instrument = PDSInstrument.MastcamLeft;
                }
                else if (id.StartsWith("MAST_RIGHT"))
                {
                    metadata.Instrument = PDSInstrument.MastcamRight;
                }
                else if (id.StartsWith("MAHLI"))
                {
                    metadata.Instrument = PDSInstrument.MAHLI;
                }
            }
            if(metadata.HasKey("GEOMETRY_PROJECTION_TYPE"))
            {
                string geoType = ParseString(metadata["GEOMETRY_PROJECTION_TYPE"]);
                if (geoType == "RAW")
                {
                    metadata.GeometryProjection = PDSGeometryProjection.Raw;
                }
                else if (geoType == "LINEARIZED")
                {
                    metadata.GeometryProjection = PDSGeometryProjection.Linearized;
                }
            }
            if (metadata.HasKey("IMAGE_TYPE"))
            {
                string imageType = ParseString(metadata["IMAGE_TYPE"]);
                if (imageType == "REGULAR")
                {
                    metadata.ImageSizeType = PDSImageSizeType.Regular;
                }
                else if (imageType == "THUMBNAIL")
                {
                    metadata.ImageSizeType = PDSImageSizeType.Thumbnail;
                }
            }
            if (metadata.HasKey("SPACECRAFT_CLOCK_START_COUNT"))
            {
                metadata.SpacecraftClock = ParseNumber(metadata["SPACECRAFT_CLOCK_START_COUNT"]);
            }
            if (metadata.HasKey("PRODUCT_CREATION_TIME"))
            {
                string tmp = ParseString(metadata["PRODUCT_CREATION_TIME"]);
                metadata.ProductCreationTime = DateTime.Parse(tmp);
            }
            if (metadata.HasKey("PRODUCT_ID"))
            {
                metadata.ProductId = PDSProductID.ParseFromString(ParseString(metadata["PRODUCT_ID"]));
            }
            if (metadata.HasKey("PRODUCER_INSTITUTION_NAME") && ParseString(metadata["PRODUCER_INSTITUTION_NAME"]).Contains("MULTIMISSION INSTRUMENT PROCESSING"))
            {
                metadata.ProducingInstitution = Institution.OPGS;
            }
            else if (metadata.HasKey("INSTITUTION_NAME") && ParseString(metadata["INSTITUTION_NAME"]).Contains("MALIN SPACE SCIENCE SYSTEMS"))
            {
                metadata.ProducingInstitution = Institution.MSSS;
            }
            if (metadata.HasKey("PLANET_DAY_NUMBER"))
            {
                metadata.PlanetDayNumber = (int)ParseNumber(metadata["PLANET_DAY_NUMBER"]);
            }
        }

        void ParseDerivedImageParamaters(PDSMetadata metadata)
        {
            if (metadata.HasKey("DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE"))
            {
                string imageType = ParseString(metadata["DERIVED_IMAGE_PARMS", "DERIVED_IMAGE_TYPE"]);
                if (imageType == "IMAGE")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.Image;
                }
                else if (imageType == "MASK")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.RoverMask;
                }
                else if (imageType == "RANGE_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.Range;
                }
                else if (imageType == "REACHABILITY_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.ReachabilityMap;
                }
                else if (imageType == "XYZ_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.XYZ;
                }
                else if (imageType == "RANGE_ERROR_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.RangeErrorMap;
                }
                else if (imageType == "UVW_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.NormalMap;
                }
                else if (imageType == "XYZ_ERROR_MAP")
                {
                    metadata.DerivedImageType = PDSDerivedImageType.XYZErrorMap;
                }
            }
            else if (metadata.DerivedImageType == PDSDerivedImageType.Unknown && (metadata.Instrument == PDSInstrument.MastcamLeft || 
                                                                                  metadata.Instrument == PDSInstrument.MastcamRight || 
                                                                                  metadata.Instrument == PDSInstrument.MAHLI))
            {
                // If this is a mastcam and we didn't detect a derived image type, assume its an image
                metadata.DerivedImageType = PDSDerivedImageType.Image;
            }

            if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE"))
            {
                string value = ParseString(metadata["DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE"]);
                value = value.Replace("<m>", "");
                double.TryParse(value, out metadata.MaximumFocusDistance);
            }

        }

        void ParseImageParameters(PDSMetadata metadata)
        {
            if (metadata.HasGroup("IMAGE"))
            {
                // Read some basic info about the image
                metadata.Width = (int)ParseNumber(metadata["IMAGE", "LINE_SAMPLES"]);
                metadata.Height = (int)ParseNumber(metadata["IMAGE", "LINES"]);
                metadata.Bands = (int)ParseNumber(metadata["IMAGE", "BANDS"]);
                metadata.FirstLine = (int)ParseNumber(metadata["IMAGE", "FIRST_LINE"]);
                metadata.FirstSample = (int)ParseNumber(metadata["IMAGE", "FIRST_LINE_SAMPLE"]);
                metadata.BitDepth = (int)ParseNumber(metadata["IMAGE", "SAMPLE_BITS"]);

                // Read bit mask
                string[] tokens = ParseString(metadata["IMAGE", "SAMPLE_BIT_MASK"]).Split('#');
                metadata.BitMask = Convert.ToUInt32(tokens[1], int.Parse(tokens[0]));

                // Read sample type
                string sampleTypeString = ParseString(metadata["IMAGE", "SAMPLE_TYPE"]);
                if ((sampleTypeString == "MSB_INTEGER" || sampleTypeString == "MSB_UNSIGNED_INTEGER") && metadata.BitDepth == 16)
                {
                    metadata.SampleType = typeof(ushort);
                }
                else if (sampleTypeString == "IEEE_REAL" && metadata.BitDepth == 32)
                {
                    metadata.SampleType = typeof(float);
                }
                else if ((sampleTypeString == "UNSIGNED_INTEGER" || sampleTypeString == "MSB_UNSIGNED_INTEGER") && metadata.BitDepth == 8)
                {
                    metadata.SampleType = typeof(byte);
                }       
            }
        }

        void ParseInstrumentStateParamaters(PDSMetadata metadata)
        {
            if (metadata.HasKey("INSTRUMENT_STATE_PARMS", "EXPOSURE_DURATION"))
            {
                string exposureDurationStr = ParseString(metadata["INSTRUMENT_STATE_PARMS", "EXPOSURE_DURATION"]);
                exposureDurationStr = exposureDurationStr.Replace("<ms>", "");
                double.TryParse(exposureDurationStr, out metadata.ExposureDuration);
            }
        }

        void ParseImageRequestParamaters(PDSMetadata metadata)
        {
            if (metadata.HasKey("IMAGE_REQUEST_PARMS", "FILTER_NUMBER"))
            {
                string filter = ParseString(metadata["IMAGE_REQUEST_PARMS", "FILTER_NUMBER"]);
                int.TryParse(filter, out metadata.FilterNumber);
            }
        }

        void ParseRoverCoordinateSystem(PDSMetadata metadata)
        {
            foreach (string group in new string[] { "ROVER_COORDINATE_SYSTEM", "ROVER_COORDINATE_SYSTEM_PARMS" })
            {
                if (metadata.HasGroup(group))
                {
                    double[] qvals = ParseNumberArray(metadata[group, "ORIGIN_ROTATION_QUATERNION"]);
                    // IMG stores quaternions in WXYZ order but our class needs them in XYZW
                    metadata.RoverOriginRotation = new Quaternion(qvals[1], qvals[2], qvals[3], qvals[0]);
                    double[] offset = ParseNumberArray(metadata[group, "ORIGIN_OFFSET_VECTOR"]);
                    metadata.OriginOffset = new Vector3(offset);
                    return;
                }
            }
        }

        protected static string ParseString(string s)
        {
            if (s.StartsWith("\"") && s.EndsWith("\""))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s;
        }

        protected static string[] ParseStringArray(string s)
        {
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseString(x)).ToArray();
        }

        protected static double ParseNumber(string s)
        {
            s = ParseString(s);
            return double.Parse(s);
        }

        protected static double[] ParseNumberArray(string s)
        {
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseNumber(x)).ToArray();
        }

        class PDSCameraModeParser
        {
            const int CAHVORE_CMOD_TYPE_PERSPECTIVE = 1;
            const int CAHVORE_CMOD_TYPE_FISHEYE = 2;
            const int CAHVORE_CMOD_TYPE_GENERAL = 3;

            PDSMetadata metadata;
            public PDSCameraModeParser(PDSMetadata metadata)
            {
                this.metadata = metadata;
            }

            public CameraModel Parse()
            {
                // Read camera model type
                string cameraModelType = null;
                if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                {
                    cameraModelType = PDSFieldReader.ParseString(metadata["GEOMETRIC_CAMERA_MODEL", "MODEL_TYPE"]);
                }
                else if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL_PARMS"))
                {
                    cameraModelType = PDSFieldReader.ParseString(metadata["GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_TYPE"]);
                }
                if (cameraModelType == "CAHV")
                {
                    return new CAHV(CAHV_C, CAHV_A, CAHV_H, CAHV_V);
                }
                else if (cameraModelType == "CAHVOR")
                {
                    return new CAHVOR(CAHV_C, CAHV_A, CAHV_H, CAHV_V, CAHV_O, CAHV_R);
                }
                else if (cameraModelType == "CAHVORE")
                {
                    bool validMode = false;
                    LinearityMode mode = LinearityMode.Perspective;
                    if (CAHVORE_MType == CAHVORE_CMOD_TYPE_PERSPECTIVE)
                    {
                        mode = LinearityMode.Perspective;
                        validMode = true;
                    }
                    else if (CAHVORE_MType == CAHVORE_CMOD_TYPE_FISHEYE)
                    {
                        mode = LinearityMode.Fisheye;
                        validMode = true;
                    }
                    else if (CAHVORE_MType == CAHVORE_CMOD_TYPE_GENERAL)
                    {
                        mode = new LinearityMode(CAHVORE_MParm);
                        validMode = true;
                    }
                    if (validMode)
                    {
                        return new CAHVORE(CAHV_C, CAHV_A, CAHV_H, CAHV_V, CAHV_O, CAHV_R, CAHV_E, mode);
                    }
                }

                return null;
            }

            Vector2 Ortho_P
            {
                get { return ReadOrthoComponent(1); }
            }
            Vector2 Ortho_S
            {
                get { return ReadOrthoComponent(2); }
            }
            Vector3 CAHV_C
            {
                get { return ReadCAHVORComponent(1); }
            }
            Vector3 CAHV_A
            {
                get { return ReadCAHVORComponent(2); }
            }
            Vector3 CAHV_H
            {
                get { return ReadCAHVORComponent(3); }
            }
            Vector3 CAHV_V
            {
                get { return ReadCAHVORComponent(4); }
            }
            Vector3 CAHV_O
            {
                get { return ReadCAHVORComponent(5); }
            }
            Vector3 CAHV_R
            {
                get { return ReadCAHVORComponent(6); }
            }
            Vector3 CAHV_E
            {
                get { return ReadCAHVORComponent(7); }
            }

            double CAHVORE_MParm
            {
                get
                {
                    if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                        return PDSFieldReader.ParseNumber(metadata["GEOMETRIC_CAMERA_MODEL", "MODEL_COMPONENT_9"]);
                    return PDSFieldReader.ParseNumber(metadata["GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_COMPONENT_9"]);
                }
            }

            int CAHVORE_MType
            {
                get
                {
                    if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                        return (int)PDSFieldReader.ParseNumber(metadata["GEOMETRIC_CAMERA_MODEL", "MODEL_COMPONENT_8"]);
                    return (int)PDSFieldReader.ParseNumber(metadata["GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_COMPONENT_8"]);
                }
            }

            Vector3 ReadCAHVORComponent(int i)
            {
                string componentName = "MODEL_COMPONENT_" + i;
                if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                    return new Vector3(ParseNumberArray(metadata["GEOMETRIC_CAMERA_MODEL", componentName]));
                return new Vector3(ParseNumberArray(metadata["GEOMETRIC_CAMERA_MODEL_PARMS", componentName]));
            }

            private Vector2 ReadOrthoComponent(int i)
            {
                string componentName = "MODEL_COMPONENT_" + i;
                if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                    return new Vector2(ParseNumberArray(metadata["GEOMETRIC_CAMERA_MODEL", componentName]));
                return new Vector2(ParseNumberArray(metadata["GEOMETRIC_CAMERA_MODEL_PARMS", componentName]));
            }
        }

        class RoverArticulationParser
        {

            /// <summary>
            /// This value is used in image header to indicate that an arm resolver angle is
            /// not available, and that the encoder angle should be used instead.
            /// </summary>
            private const double INVALID_ARM_ANGLE = 1e30;

            PDSMetadata metadata;
            public RoverArticulationParser(PDSMetadata metadata)
            {
                this.metadata = metadata;
            }

            public RoverArticulation Parse()
            {
                RoverArticulation ra = new RoverArticulation();
                ParseChassisArticulation(ra);
                ParseArmArticulation(ra);
                ParseRMSArticulation(ra);
                return ra;
            }


            /// <summary>
            /// Parse chassis articulation angles.
            /// </summary>
            /// <param name="rover">Rover articulation object to populate.</param>
            private void ParseChassisArticulation(RoverArticulation rover)
            {
                // Some images store the chassis state in the CHASSIS_ARTICULATION_STATE group, and others store it
                // in as CHASSIS_ARTICULATION_STATE_PARAMS. Still others use CHASSIS_ARTICULATION_STATE_PARAMS. Check
                // for all of them.
                string[] groupKeys = {
                    "CHASSIS_ARTICULATION_STATE",
                    "CHASSIS_ARTICULATION_STATE_PARAMS",
                    "CHASSIS_ARTICULATION_STATE_PARMS"
                };

                foreach (string group in groupKeys)
                {
                    if (metadata.HasGroup(group))
                    {
                        Dictionary<string, double> angles = ParseAngles(group);

                        // Header gives angle as differential angle, but this seems to actually be the rocker angle.
                        string diffentialName = angles.ContainsKey("LEFT DIFFERENTIAL") ? "LEFT DIFFERENTIAL" : "LEFT DIFFERENTIAL BOGIE";
                        rover.LeftRockerAngle = angles[diffentialName];

                        rover.LeftBogieAngle = angles["LEFT BOGIE"];
                        rover.RightBogieAngle = angles["RIGHT BOGIE"];
                        break;
                    }
                }
            }

            /// <summary>
            /// Parse arm articulation angles.
            /// </summary>
            /// <param name="rover">Rover articulation object to populate.</param>
            private void ParseArmArticulation(RoverArticulation rover)
            {
                string[] groupKeys = {
                    "ARM_ARTICULATION_STATE",
                    "ARM_ARTICULATION_STATE_PARMS"
                };

                foreach (string group in groupKeys)
                {
                    if (metadata.HasGroup(group))
                    {
                        Dictionary<string, double> angles = ParseAngles(group);

                        rover.ArmAngle1 = GetArmAngle(angles, "JOINT 1 AZIMUTH");
                        rover.ArmAngle2 = GetArmAngle(angles, "JOINT 2 ELEVATION");
                        rover.ArmAngle3 = GetArmAngle(angles, "JOINT 3 ELBOW");
                        rover.ArmAngle4 = GetArmAngle(angles, "JOINT 4 WRIST");
                        rover.ArmAngle5 = GetArmAngle(angles, "JOINT 5 TURRET");
                        break;
                    }
                }
            }

            /// <summary>
            /// Parse RMS articulation angles.
            /// </summary>
            /// <param name="rover">Rover articulation object to populate.</param>
            private void ParseRMSArticulation(RoverArticulation rover)
            {
                string[] groupKeys = {
                    "RSM_ARTICULATION_STATE",
                    "RSM_ARTICULATION_STATE_PARMS"
                };

                foreach (string group in groupKeys)
                {
                    if (metadata.HasGroup(group))
                    {
                        Dictionary<string, double> angles = ParseAngles(group);

                        rover.MastAzimuth = angles["AZIMUTH-MEASURED"];
                        rover.MastElevation = angles["ELEVATION-MEASURED"];
                        break;
                    }
                }
            }

            /// <summary>
            /// Read an arm angle from a parsed dictionary of angles. This method first attempts to read
            /// the resolver angle for the given joint. If the resolver angle is missing or invalid (1e30),
            /// then the method reads the corresponding encoder angle.
            /// 
            /// In some cases, images contain an incorrect resolver angle that is very different than the
            /// encoder angle. This method detects cases in which the resolver angle is more than 10 degrees
            /// different than the encoder angle. In these cases, the encoder angle is returned.
            /// </summary>
            /// 
            /// <param name="angleDict">Named angles.</param>
            /// <param name="joint">Name of the joint for which to retrieve angle.</param>
            /// <param name="resolverToleranceDegrees">Use resolver angle is resolver and encoder are within this tolerance. If
            /// angles are not within this tolerance, assume that resolver is incorrect and return encoder.</param>
            /// <returns></returns>
            private double GetArmAngle(Dictionary<string, double> angleDict, string joint, double resolverToleranceDegrees = 10)
            {
                string resolverKey = joint + "-RESOLVER";
                string encoderKey = joint + "-ENCODER";
                if (!angleDict.ContainsKey(resolverKey) || !angleDict.ContainsKey(encoderKey))
                {
                    throw new ArgumentException("Malformed image header. Missing arm angle: " + joint);
                }

                double resolverAngle = angleDict[resolverKey];
                double encoderAngle = angleDict[encoderKey];

                // In normal cases the encoder and resolver angles are similar. In some abnormal
                // cases we have observed resolver angles that are very wrong. Detect cases
                // in which the two angles are very different, and return the encoder angle in
                // those cases (if it is valid).
                if (IsValidArmAngle(resolverAngle))
                {
                    bool anglesVeryDifferent = Math.Abs(resolverAngle - encoderAngle) >  MathHelper.ToRadians(resolverToleranceDegrees);
                    if (anglesVeryDifferent && IsValidArmAngle(encoderAngle))
                    {
                        //logger.Warn("Resolver and encoder angles more than " + resolverToleranceDegrees + " degrees different. " +
                        //    "Using encoder angle. (" + Path.GetFileName(Filename) + ")");
                        return encoderAngle;
                    }
                    return resolverAngle;
                }
                return encoderAngle;
            }

            /// <summary>
            /// Determine if an arm angle read from an image header is valid. The image
            /// </summary>
            /// <param name="angle"></param>
            /// <returns></returns>
            private bool IsValidArmAngle(double angle)
            {
                const double epsilon = 1e-10;
                return Math.Abs(angle - INVALID_ARM_ANGLE) > epsilon;
            }

            /// <summary>
            /// Parse a list of named angles into a dictionary. This method reads the "ARTICULATION_DEVICE_ANGLE_NAME"
            /// and "ARTICULATION_DEVICE_ANGLE" keys, and creates a dictionary mapping the names to angles.
            /// </summary>
            /// <param name="imgHeader">Header from which to read angles.</param>
            /// <param name="groupKey">Group key to read.</param>
            /// <returns></returns>
            private Dictionary<string, double> ParseAngles(string groupKey)
            {
                Dictionary<string, double> dict = new Dictionary<string, double>();

                double[] angles = ReadAngleList(groupKey, "ARTICULATION_DEVICE_ANGLE");
                string[] angleNames = PDSFieldReader.ParseStringArray(metadata[groupKey, "ARTICULATION_DEVICE_ANGLE_NAME"]);

                if (angles == null || angleNames == null)
                {
                    throw new ArgumentException("Malformed image header, missing or invalid ARM_ARTICULATION_STATE group");
                }
                if (angles.Length != angleNames.Length)
                {
                    throw new ArgumentException("Malformed image header. Length of articulation angle list should match length of angle name list");
                }

                for (int i = 0; i < angles.Length; i++)
                {
                    // Trim whitespace and quotation marks
                    string name = angleNames[i].Trim('"', ' ', '\t', '\n', '\r');
                    dict[name] = angles[i];
                }
                return dict;
            }

            /// <summary>
            /// Read a list of angles from a group and key.  Convert them all to double regardless
            /// if they have <rad> tags or not.  Assume they are radians.
            /// </summary>
            /// <param name="group"></param>
            /// <param name="key"></param>
            /// <param name="imgHeader"></param>
            /// <returns></returns>
            private double[] ReadAngleList(string group, string key)
            {
                string[] parts = PDSFieldReader.ParseStringArray(metadata[group, key]);
                return parts.Select(x => ParseAngle(x)).ToArray();
            }


            /// <summary>
            /// Parse an angle from a string in the form "##.#### <rad>".
            /// </summary>
            /// <param name="angleString">String to parse</param>
            /// <returns>Angle in radians</returns>
            private double ParseAngle(string angleString)
            {
                string[] parts = angleString.Trim().Split(' ');

                // Invalid angles don't always have <rad> on them.  If this angle is invalid
                // don't assert that we need <rad> to be specified
                if (parts.Length == 1)
                {
                    double d = double.Parse(parts[0]);
                    if (!IsValidArmAngle(d))
                    {
                        return d;
                    }
                }
                if (parts.Length != 2)
                {
                    throw new ArgumentException("Unexpected angle format: " + angleString + ". Expecting '##.### <rad>'");
                }
                if (parts[1] != "<rad>")
                {
                    throw new NotImplementedException("Parsing non-radian angles is not supported");
                }
                return double.Parse(parts[0]);
            }

        }
    }
}