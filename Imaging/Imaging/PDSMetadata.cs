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
    public class PDSMetadataNullValueException : Exception
    {
        public PDSMetadataNullValueException() { }
        public PDSMetadataNullValueException(string message) : base(message) { }
        public PDSMetadataNullValueException(string message, Exception inner) : base(message, inner) { }
    }

    public class VICMetadataException : Exception
    {
        public VICMetadataException() { }
        public VICMetadataException(string message) : base(message) { }
        public VICMetadataException(string message, Exception inner) : base(message, inner) { }
    }


    public class PDSMetadata : ImageMetadata
    {

        static Dictionary<string, Type> TypeLookup;
        static Dictionary<string, int> BitDepthLookup;
        static Dictionary<string, uint> BitMaskLookup;

        static PDSMetadata()
        {
            TypeLookup = new Dictionary<string, Type>();
            TypeLookup.Add("BYTE", typeof(byte));
            TypeLookup.Add("HALF", typeof(ushort));
            TypeLookup.Add("FULL", typeof(int));
            TypeLookup.Add("REAL", typeof(float));
            TypeLookup.Add("DOUB", typeof(double));

            BitDepthLookup = new Dictionary<string, int>();
            BitDepthLookup.Add("BYTE", sizeof(byte));
            BitDepthLookup.Add("HALF", sizeof(ushort));
            BitDepthLookup.Add("FULL", sizeof(int));
            BitDepthLookup.Add("REAL", sizeof(float));
            BitDepthLookup.Add("DOUB", sizeof(double));

            BitMaskLookup = new Dictionary<string, uint>();
            BitMaskLookup.Add("BYTE", byte.MaxValue);
            BitMaskLookup.Add("HALF", (uint)ushort.MaxValue);
            BitMaskLookup.Add("FULL", int.MaxValue);
            BitMaskLookup.Add("REAL", 0);
            BitMaskLookup.Add("DOUB", 0);

        }

        // Essential Metadata
        public long RecordBytes;
        public Type SampleType;
        public int BitDepth;
        // Start location of image data
        public long DataOffset;
        public uint BitMask;
        public bool BigEndian = true;

        // Optional Metadata
        public CameraModel CameraModel;

        Dictionary<string, Dictionary<string, string>> rawHeader;
        const string NULL_GROUP = "";

        public PDSMetadata(Stream stream, bool loadAsVIC = false)
        {
            if (loadAsVIC)
            {
                InitVIC(stream);
            }
            else
            {
                InitPDS(stream);
            }
        }

        public PDSMetadata(string filename)
        {
            using (FileStream fs = File.OpenRead(filename))
            {
                string ext = Path.GetExtension(filename).ToUpper();
                if (ext == ".IMG")
                {
                    InitPDS(fs);
                }
                else if (ext == ".VIC")
                {
                    InitVIC(fs);
                }
                else
                {
                    throw new ImageSerializationException("Unexpected file extension");
                }                
            }
        }

        public PDSMetadata(PDSMetadata that)
        {
            this.rawHeader = new Dictionary<string, Dictionary<string, string>>();
            foreach (var group in that.Groups())
            {
                this.rawHeader.Add(group, new Dictionary<string, string>());
                foreach (var key in that.Keys(group))
                {
                    this.rawHeader[group].Add(key, that.rawHeader[group][key]);
                }
            }
            this.Width = that.Width;
            this.Height = that.Height;
            this.Bands = that.Bands;
            this.BitDepth = that.BitDepth;
            this.BitMask = that.BitMask;
            this.BigEndian = that.BigEndian;
            this.RecordBytes = that.RecordBytes;
            this.DataOffset = that.DataOffset;
            this.SampleType = that.SampleType;
            if (that.CameraModel != null)
            {
                this.CameraModel = (CameraModel)that.CameraModel.Clone();
            }
        }

        void InitPDS(Stream stream)
        {
            this.rawHeader = ReadPDSHeader(stream);
            this.Width = ReadAsInt("IMAGE", "LINE_SAMPLES");
            this.Height = ReadAsInt("IMAGE", "LINES");
            // If a number of band's isn't specified, assume this is a single band image
            if(HasKey("IMAGE", "BANDS"))
            {
                this.Bands = ReadAsInt("IMAGE", "BANDS");
            }
            else
            {
                this.Bands = 1;
            }            
            this.BitDepth = ReadAsInt("IMAGE", "SAMPLE_BITS");
            
            this.RecordBytes = ReadAsLong("RECORD_BYTES");
            int carrot = (int)ReadAsInt("^IMAGE");
            this.DataOffset = (carrot - 1) * this.RecordBytes;
            try
            {
                this.CameraModel = new PDSCameraModeParser(this).Parse();
            }
            catch (PDSMetadataNullValueException)
            {
                this.CameraModel = null;
            }
            string sampleType = ReadAsString("IMAGE", "SAMPLE_TYPE");
            if ((sampleType == "MSB_INTEGER" || sampleType == "MSB_UNSIGNED_INTEGER") && BitDepth == 16)
            {
                this.SampleType = typeof(ushort);
                this.BitMask = ushort.MaxValue;
            }
            else if (sampleType == "IEEE_REAL" && BitDepth == 32)
            {
                this.SampleType = typeof(float);
            }
            else if ((sampleType == "UNSIGNED_INTEGER" || sampleType == "MSB_UNSIGNED_INTEGER") && BitDepth == 8)
            {
                this.SampleType = typeof(byte);
                this.BitMask = byte.MaxValue;
            }

            // If a bit mask is specified, use it to override the per-type default masks assigned above
            if (HasKey("IMAGE", "SAMPLE_BIT_MASK"))
            {
                this.BitMask = ReadAsBitMask("IMAGE", "SAMPLE_BIT_MASK");
            }
        }

        void InitVIC(Stream stream)
        {
            this.rawHeader = ReadVICHeader(stream);
            this.Bands = ReadAsInt("NB");
            this.Height = ReadAsInt("NL");
            this.Width = ReadAsInt("NS");
            this.RecordBytes = ReadAsInt("RECSIZE");
            this.DataOffset = ReadAsInt("LBLSIZE");

            if (ReadAsString("ORG") != "BSQ")
            {
                throw new VICMetadataException("Only Band Sequential VIC files are supported");
            }
            if (ReadAsInt("NBB") != 0 || ReadAsInt("NLB") != 0)
            {
                throw new VICMetadataException("Binary record headers not supported");
            }
            if(ReadAsString("INTFMT") == "LOW")
            {
                this.BigEndian = false;
            }
            this.BitDepth = BitDepthLookup[ReadAsString("FORMAT")];
            this.SampleType = TypeLookup[ReadAsString("FORMAT")];
            this.BitMask = BitMaskLookup[ReadAsString("FORMAT")];
            if (HasKey("IMAGE_DATA", "SAMPLE_BIT_MASK"))
            {
                this.BitMask = ReadAsBitMask("IMAGE_DATA", "SAMPLE_BIT_MASK");
            }
            try
            {
                this.CameraModel = new PDSCameraModeParser(this).Parse();
            }
            catch (PDSMetadataNullValueException)
            {
                this.CameraModel = null;
            }

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
            if (!rawHeader.ContainsKey(group))
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
        public Dictionary<string, Dictionary<string, string>>.KeyCollection Groups()
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
                if (!HasKey(group, key))
                {
                    return null;
                }
                return rawHeader[group][key];
            }
        }

        public string ReadAsString(string key)
        {
            return ReadAsString(NULL_GROUP, key);
        }

        public string ReadAsString(string group, string key)
        {
            return ParseString(this[group, key]);
        }

        public string[] ReadAsStringArray(string key)
        {
            return ReadAsStringArray(NULL_GROUP, key);
        }

        public string[] ReadAsStringArray(string group, string key)
        {
            return ParseStringArray(this[group, key]);
        }

        public double ReadAsDouble(string key)
        {
            return ReadAsDouble(NULL_GROUP, key);
        }

        public double ReadAsDouble(string group, string key)
        {
            return ParseDouble(this[group, key]);
        }

        public double[] ReadAsDoubleArray(string key)
        {
            return ReadAsDoubleArray(NULL_GROUP, key);
        }

        public double[] ReadAsDoubleArray(string group, string key)
        {
            return ParseDoubleArray(this[group, key]);
        }

        public int ReadAsInt(string key)
        {
            return ReadAsInt(NULL_GROUP, key);
        }

        public int ReadAsInt(string group, string key)
        {
            return ParseInt(this[group, key]);
        }

        public long ReadAsLong(string key)
        {
            return ReadAsLong(NULL_GROUP, key);
        }

        public long ReadAsLong(string group, string key)
        {
            return ParseLong(this[group, key]);
        }

        public int[] ReadAsIntArray(string key)
        {
            return ReadAsIntArray(NULL_GROUP, key);
        }

        public int[] ReadAsIntArray(string group, string key)
        {
            return ParseIntArray(this[group, key]);
        }

        public DateTime ReadAsDateTime(string key)
        {
            return ReadAsDateTime(NULL_GROUP, key);
        }

        public DateTime ReadAsDateTime(string group, string key)
        {
            return DateTime.Parse(this[group, key]);
        }

        public uint ReadAsBitMask(string key)
        {
            return ReadAsBitMask(NULL_GROUP, key);
        }

        public uint ReadAsBitMask(string group, string key)
        {
            string[] tokens = ParseString(this[group, key]).Split('#');
            return Convert.ToUInt32(tokens[1], int.Parse(tokens[0]));
        }

        string ParseString(string s)
        {
            s = s.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            if (s.StartsWith("\'") && s.EndsWith("\'"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s;
        }

        string[] ParseStringArray(string s)
        {
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseString(x)).ToArray();
        }

        void CheckForNull(string s)
        {
            if (s.Equals("NULL") || s.Equals("null"))
            {
                throw new PDSMetadataNullValueException();
            }
        }

        int ParseInt(string s)
        {
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            return int.Parse(s);
        }

        int[] ParseIntArray(string s)
        {
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseInt(x)).ToArray();
        }

        double ParseDouble(string s)
        {
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            return double.Parse(s);
        }

        double[] ParseDoubleArray(string s)
        {
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseDouble(x)).ToArray();
        }

        long ParseLong(string s)
        {
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            return long.Parse(s);
        }

        string StripUnits(string s)
        {
            int start = s.IndexOf("<");
            if (start >= 0)
            {
                return s.Substring(0, start - 1);
            }
            return s;
        }

        Dictionary<string, Dictionary<string, string>> ReadPDSHeader(Stream stream)
        {
            var header = new Dictionary<string, Dictionary<string,string>>();
            using (StreamReader file = new StreamReader(stream))
            {
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

        private Dictionary<string, Dictionary<string, string>> ReadVICHeader(Stream stream)
        {
            Dictionary<string, Dictionary<string, string>> header = new Dictionary<string, Dictionary<string, string>>();
            StreamReader sr = new StreamReader(stream);
            char[] buffer = new char[1000];
            sr.Read(buffer, 0, 1000);
            string sizeLabelText = new string(buffer);
            string[] tokens = sizeLabelText.Split(' ');

            var parts = tokens[0].Split('=');
            string name = parts[0].Trim();
            string value = parts[1];
            if (name != "LBLSIZE")
            {
                throw new VICMetadataException("LBLSIZE not found");
            }
            int headerLength = int.Parse(value);
            stream.Position = 0;
            sr = new StreamReader(stream);
            buffer = new char[headerLength];
            sr.Read(buffer, 0, headerLength);
            string headerText = new string(buffer); //allText.Substring(0, headerLength);

            header.Add(NULL_GROUP, new Dictionary<string, string>());

            tokens = headerText.Split(' ');
            string group = NULL_GROUP;
            foreach (var tok in tokens)
            {
                // Note it is totally possible to have a valid VIC file with spaces around the = sign, we don't handle it here
                // because we expect its rare and complicates parsing.  
                if (tok.Contains("="))
                {
                    int splitIndex = tok.IndexOf('=');
                    if (splitIndex == 0)
                    {
                        throw new VICMetadataException("Unexpected '=' sign.  Reader does not currently support spaces between '=' and name/value");
                    }
                    name = tok.Substring(0, splitIndex).Trim();
                    value = tok.Substring(splitIndex + 1, tok.Length - splitIndex - 1).Trim();
                    if (name.ToUpper() == "PROPERTY")
                    {
                        group = ParseString(value);
                        if (!header.ContainsKey(value))
                        {
                            header.Add(group, new Dictionary<string, string>());
                        }
                        continue;
                    }
                    if (name.ToUpper() == "TASK")
                    {
                        break;
                    }
                    header[group].Add(name, value);
                }
            }
            return header;
        }
    }
}
