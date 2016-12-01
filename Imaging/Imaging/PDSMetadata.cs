using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.Text.RegularExpressions;

namespace OPS.Imaging
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

    public enum Institution
    {
        Unknown,
        OPGS,
        MSSS
    }

    public class PDSProductID
    {
        public string filename = null,
                         camera = null,
                         config = null,
                         clock = null,
                         framesize = null,
                         product = null,
                         site = null,
                         drive = null,
                         seqnum = null,
                         ver = null;

        public static PDSProductID ParseFromString(string productId)
        {
            // MIPL pattern
            string miplPattern = @"^(NL|FL|FR|RL|ML|MR|NR|MH)([AB01234567RGBFULDCA_])[_TA-SU-Z](\d{9})([A-Z]{3}[LR_]|[A-Z]{4})(S|F|T|D)(\d{3})(\d{4})([A-Z_]{4})([0-9A-Z_]{5})M([1-9A-Z_]+)$";
            Match match = Regex.Match(productId, miplPattern);
            if (match.Success)
            {
                PDSProductID id = new PDSProductID();
                id.filename = productId;
                id.camera = match.Groups[1].Value;
                id.config = match.Groups[2].Value;
                id.clock = match.Groups[3].Value;
                id.product = match.Groups[4].Value.Replace("_", "");
                id.framesize = match.Groups[5].Value;
                id.site = match.Groups[6].Value;
                id.drive = match.Groups[7].Value;
                id.seqnum = match.Groups[9].Value;
                id.ver = match.Groups[10].Value;
                return id;
            }
            string mailinPattern = @"^(\d{4})(ML|MR|MH)(\d{16})(E|I|C|D)(\d{2})_D([RCXL][RCXL][RCXL])$";
            match = Regex.Match(productId, mailinPattern);
            if (match.Success)
            {
                PDSProductID id = new PDSProductID();
                id.filename = productId;
                id.camera = match.Groups[2].Value;
                id.product = match.Groups[6].Value;
                return id;
            }
            return null;
        }
    }

    /// <summary>
    /// Articulation parameters for a rover pose. All angles are in radians.
    /// </summary>
    public class RoverArticulation
    {
        public double LeftRockerAngle;
        public double LeftBogieAngle;
        public double RightBogieAngle;
        public double RightRockerAngle
        {
            get
            {
                return -LeftRockerAngle;
            }
        }
        public double ArmAngle1;
        public double ArmAngle2;
        public double ArmAngle3;
        public double ArmAngle4;
        public double ArmAngle5;

        public double MastAzimuth;
        public double MastElevation;
    }

    public class PDSMetadata : ImageMetadata
    {
        // File Metadata
        public string Filename;
        public DateTime ProductCreationTime;
        public long RecordBytes;
        public int Carrot;
        // Image format metadata
        public int FirstLine;
        public int FirstSample;
        public Type SampleType;
        public int BitDepth;
        public uint BitMask;
        // Image properties
        public PDSProductID ProductId;
        public PDSInstrument Instrument = PDSInstrument.Unknown;
        public PDSGeometryProjection GeometryProjection = PDSGeometryProjection.Unknown;
        public PDSImageSizeType ImageSizeType = PDSImageSizeType.Unknown;
        public PDSDerivedImageType DerivedImageType = PDSDerivedImageType.Unknown;
        public Institution ProducingInstitution = Institution.Unknown;
        // Image settings
        public double ExposureDuration = 0;                     // Navcam HazCam specific
        public int FilterNumber = 0;                            // Mastcam specific
        public double MaximumFocusDistance = double.MaxValue;   // Mastcam specific
        // Camera metadata
        public CameraModel CameraModel;
        // Spacecraft metadata
        public double SpacecraftClock;
        // Rover metadata
        public Quaternion RoverOriginRotation;
        public Vector3 OriginOffset;
        public int[] MotionCounter;
        public string SiteDrive;
        public int PlanetDayNumber;
        public RoverArticulation Articulation;


        protected Dictionary<string, Dictionary<string, string>> rawHeader;
        PDSFieldReader fieldReader;
        const string NULL_GROUP = "";

        public PDSMetadata(string filename) 
        {
            this.Filename = filename;
            using (FileStream fs = File.OpenRead(filename))
            {
                this.rawHeader = ReadHeader(fs);
            }
            fieldReader = new PDSFieldReader(this);
        }

        public PDSMetadata(PDSMetadata that)
        {
            this.rawHeader = new Dictionary<string, Dictionary<string, string>>();
            foreach(var group in that.Groups())
            {
                this.rawHeader.Add(group, new Dictionary<string, string>());
                foreach (var key in that.Keys(group))
                {
                    this.rawHeader[group].Add(key, that.rawHeader[group][key]);
                }
            }
            fieldReader = new PDSFieldReader(this);
        }

        public override object Clone()
        {
            return new PDSMetadata(this);
        }

        public bool HasGroup(string group)
        {
            return rawHeader.ContainsKey(group);
        }

        public bool HasKey(string group, string key)
        {
            if(!rawHeader.ContainsKey(group))
            {
                return false;
            }
            return rawHeader[group].ContainsKey(key);
        }

        public bool HasKey(string key)
        {
            return HasKey(NULL_GROUP, key);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Dictionary<string,Dictionary<string,string>>.KeyCollection Groups()
        {
            return this.rawHeader.Keys;
        }

        public Dictionary<string, string>.KeyCollection Keys(string group = NULL_GROUP)
        {
            return this.rawHeader[group].Keys;
        }

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string this[string key]
        {
            get
            { 
                return this[NULL_GROUP, key];
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="group"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public string this[string group, string key]
        {
            get
            {
                if(!HasKey(group, key))
                {
                    return null;
                }
                return rawHeader[group][key];
            }
        }


        Dictionary<string, Dictionary<string, string>> ReadHeader(FileStream fs)
        {
            var header = new Dictionary<string, Dictionary<string,string>>();
            using (StreamReader file = new System.IO.StreamReader(fs))
            {
                long fileLengthAsRead = file.BaseStream.Length;
                List<String> lines = new List<string>();

                // Loop through entire header, strip comments and empty lines
                // For lines whose values span multiple lines, concat them into one line
                string line = null;
                while ((line = file.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line == "END")
                    {
                        break;
                    }
                    if (line.Length > 0 && line.IndexOf("/*") != 0)
                    {
                        if (line.Split('=').Length == 2)
                        {
                            lines.Add(line);
                        }
                        else
                        {
                            // This is a continuation of the last line
                            lines[lines.Count - 1] += " " + line;
                        }
                    }
                }

                // Read values out of the cleaned lines
                // Values with no group use null as the group key (i.e. header[null])
                string curGroup = NULL_GROUP;
                foreach (string curLine in lines)
                {
                    // Detect group open and close
                    string[] tokens = curLine.Split('=');
                    string key = tokens[0].Trim();
                    string value = tokens[1].Trim();

                    if (key == "GROUP" || key == "OBJECT")
                    {
                        curGroup = value;
                        continue;
                    }
                    if (key == "END_GROUP" || key == "END_OBJECT")
                    {
                        curGroup = NULL_GROUP;
                        continue;
                    }
                    if (!header.ContainsKey(curGroup))
                    {
                        header.Add(curGroup, new Dictionary<string, string>());
                    }

                    if (tokens.Length == 2)
                    {
                        header[curGroup].Add(key, value);
                    }
                }
            }
            return header;
        }

        

        
    }
}
