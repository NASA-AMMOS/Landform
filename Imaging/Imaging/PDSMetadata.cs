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

    public class PDSMetadata : ImageMetadata
    {
        // Essential Metadata
        public long RecordBytes;
        public int Carrot;
        public Type SampleType;
        public int BitDepth;
        public uint BitMask;
        // Optional Metadata
        public CameraModel CameraModel;
        
        protected Dictionary<string, Dictionary<string, string>> rawHeader;
        const string NULL_GROUP = "";

        public PDSMetadata(string filename)
        {
            using (FileStream fs = File.OpenRead(filename))
            {
                this.rawHeader = ReadHeader(fs);
            }
            this.Width = ReadAsInt("IMAGE", "LINE_SAMPLES");
            this.Height = ReadAsInt("IMAGE", "LINES");
            this.Bands = ReadAsInt("IMAGE", "BANDS");
            this.BitDepth = ReadAsInt("IMAGE", "SAMPLE_BITS");
            string[] tokens = ParseString(this["IMAGE", "SAMPLE_BIT_MASK"]).Split('#');
            this.BitMask = Convert.ToUInt32(tokens[1], int.Parse(tokens[0]));
            this.RecordBytes = ReadAsLong("RECORD_BYTES");
            this.Carrot = (int)ReadAsInt("^IMAGE");
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
            }
            else if (sampleType == "IEEE_REAL" && BitDepth == 32)
            {
                this.SampleType = typeof(float);
            }
            else if ((sampleType == "UNSIGNED_INTEGER" || sampleType == "MSB_UNSIGNED_INTEGER") && BitDepth == 8)
            {
                this.SampleType = typeof(byte);
            }
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
            this.Width = that.Width;
            this.Height = that.Height;
            this.Bands = that.Bands;
            this.BitDepth = that.BitDepth;
            this.BitMask = that.BitMask;
            this.RecordBytes = that.RecordBytes;
            this.Carrot = that.Carrot;
            this.SampleType = that.SampleType;
            if (that.CameraModel != null)
            {
                this.CameraModel = (CameraModel)that.CameraModel.Clone();
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

        string ParseString(string s)
        {
            s = s.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
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
            if(s.Equals("NULL") || s.Equals("null"))
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
