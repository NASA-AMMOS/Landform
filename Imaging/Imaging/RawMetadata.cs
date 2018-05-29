using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Represents metadata in the form of key value string pairs and provides methods
    /// to convert the string values into various types when reading.
    /// </summary>
    public class RawMetadata : ImageMetadata
    {
        protected Dictionary<string, Dictionary<string, string>> rawHeader;
        protected const string NULL_GROUP = "";

        public RawMetadata() : base()
        {
            this.rawHeader = new Dictionary<string, Dictionary<string, string>>();
        }

        public RawMetadata(RawMetadata that) : base(that)
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
        }

        public override object Clone()
        {
            return new RawMetadata(this);
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

        protected string ParseString(string s)
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
    }
}
