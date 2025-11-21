using JPLOPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Pipeline
{
    public abstract class PDSRoverArticulationParser
    {
        /// <summary>
        /// This value is used in image header to indicate that an arm resolver angle is
        /// not available, and that the encoder angle should be used instead.
        /// </summary>
        private const double INVALID_ARM_ANGLE = 1e30;

        protected readonly PDSMetadata metadata;

        public PDSRoverArticulationParser(PDSMetadata metadata)
        {
            this.metadata = metadata;
        }

        /// <summary>
        /// Return null if parse fails.
        /// </summary>
        public abstract RoverArticulation Parse();

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
        protected double GetArmAngle(Dictionary<string, double> angleDict, string joint, double resolverToleranceDegrees = 10)
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
                bool anglesVeryDifferent = Math.Abs(resolverAngle - encoderAngle) > MathHelper.ToRadians(resolverToleranceDegrees);
                if (anglesVeryDifferent && IsValidArmAngle(encoderAngle))
                {
                    //logger.Warn("Resolver and encoder angles more than " + resolverToleranceDegrees + " degrees");
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
        protected bool IsValidArmAngle(double angle)
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
        protected Dictionary<string, double> ParseAngles(string groupKey)
        {
            Dictionary<string, double> dict = new Dictionary<string, double>();

            double[] angles = ReadAngleList(groupKey, "ARTICULATION_DEVICE_ANGLE");
            string[] angleNames = metadata.ReadAsStringArray(groupKey, "ARTICULATION_DEVICE_ANGLE_NAME");

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
        protected double[] ReadAngleList(string group, string key)
        {
            string[] parts = metadata.ReadAsStringArray(group, key);
            return parts.Select(x => ParseAngle(x)).ToArray();
        }


        /// <summary>
        /// Parse an angle from a string in the form "##.#### <rad>".
        /// </summary>
        /// <param name="angleString">String to parse</param>
        /// <returns>Angle in radians</returns>
        protected double ParseAngle(string angleString)
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
            if (parts.Length > 2)
            {
                throw new PDSParserException("Unexpected angle format: " + angleString + ". Expecting '##.### <rad>'");
            }
            else if((parts.Length == 2) && (parts[1] != "<rad>"))
            {
                throw new NotImplementedException("Parsing non-radian angles is not supported");
            }

            return double.Parse(parts[0]);
        }
    }

    public class MSLRoverArticulationParser : PDSRoverArticulationParser
    {
        public MSLRoverArticulationParser(PDSMetadata metadata) : base(metadata) { }

        public override RoverArticulation Parse()
        {
            var ra = new MSLRoverArticulation();
            if (!ParseChassisArticulation(ra) || !ParseArmArticulation(ra) || !ParseMastArticulation(ra))
            {
                return null;
            }
            return ra;
        }

        /// <summary>
        /// Parse chassis articulation angles.
        /// </summary>
        /// <param name="rover">Rover articulation object to populate.</param>
        private bool ParseChassisArticulation(MSLRoverArticulation rover)
        {
            // Some images store the chassis state in the CHASSIS_ARTICULATION_STATE group, and others store it
            // in as CHASSIS_ARTICULATION_STATE_PARAMS. Still others use CHASSIS_ARTICULATION_STATE_PARAMS. Check
            // for all of them.
            string[] groupKeys =
                { "CHASSIS_ARTICULATION_STATE", "CHASSIS_ARTICULATION_STATE_PARAMS", "CHASSIS_ARTICULATION_STATE_PARMS" };

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
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parse arm articulation angles.
        /// </summary>
        /// <param name="rover">Rover articulation object to populate.</param>
        private bool ParseArmArticulation(MSLRoverArticulation rover)
        {
            string[] groupKeys = { "ARM_ARTICULATION_STATE", "ARM_ARTICULATION_STATE_PARMS" };

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
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parse mast articulation angles.
        /// </summary>
        /// <param name="rover">Rover articulation object to populate.</param>
        private bool ParseMastArticulation(MSLRoverArticulation rover)
        {
            string[] groupKeys = { "RSM_ARTICULATION_STATE", "RSM_ARTICULATION_STATE_PARMS" };

            foreach (string group in groupKeys)
            {
                if (metadata.HasGroup(group))
                {
                    Dictionary<string, double> angles = ParseAngles(group);

                    rover.MastAzimuth = angles["AZIMUTH-MEASURED"];
                    rover.MastElevation = angles["ELEVATION-MEASURED"];
                    return true;
                }
            }

            return false;
        }
    }

    public class M2020RoverArticulationParser : PDSRoverArticulationParser
    {
        public M2020RoverArticulationParser(PDSMetadata metadata) : base(metadata) { }

        public override RoverArticulation Parse() { return null; }
    }
}
