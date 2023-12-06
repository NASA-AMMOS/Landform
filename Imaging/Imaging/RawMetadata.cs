using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Imaging
{

    public class MetadataException : Exception
    {
        public MetadataException() { }
        public MetadataException(string message) : base(message) { }
        public MetadataException(string message, Exception inner) : base(message, inner) { }
    }

    public class MetadataNullValueException : MetadataException
    {
        public MetadataNullValueException() { }
        public MetadataNullValueException(string message) : base(message) { }
        public MetadataNullValueException(string message, Exception inner) : base(message, inner) { }
    }

    public class MetadataKeyNotFoundException : MetadataException
    {
        public MetadataKeyNotFoundException() { }
        public MetadataKeyNotFoundException(string message) : base(message) { }
        public MetadataKeyNotFoundException(string message, Exception inner) : base(message, inner) { }
    }

    public class MetadataFormatException : MetadataException
    {
        public MetadataFormatException() { }
        public MetadataFormatException(string message) : base(message) { }
        public MetadataFormatException(string message, Exception inner) : base(message, inner) { }
        public MetadataFormatException(string group, string key, string kind, string val)
            : base(string.Format("error parsing {0} {1}=\"{2}\"", kind,
                                 !string.IsNullOrEmpty(group) ? (group + "/" + key) : key,
                                 val)) { }
    }

    /// <summary>
    /// Represents metadata in the form of key value string pairs and provides methods
    /// to convert the string values into various types when reading.
    /// </summary>
    public class RawMetadata : ImageMetadata
    {
        protected Dictionary<string, Dictionary<string, string>> rawHeader;
        public const string NULL_GROUP = "";

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
            CheckKey(key);
            return ReadAsString(NULL_GROUP, key);
        }

        public string ReadAsString(string group, string key)
        {
            CheckKey(group, key);
            return ParseString(this[group, key]);
        }

        public string[] ReadAsStringArray(string key)
        {
            CheckKey(key);
            return ReadAsStringArray(NULL_GROUP, key);
        }

        public string[] ReadAsStringArray(string group, string key)
        {
            CheckKey(group, key);
            return ParseStringArray(this[group, key]);
        }

        public double ReadAsDouble(string key)
        {
            CheckKey(key);
            return ReadAsDouble(NULL_GROUP, key);
        }

        public double ReadAsDouble(string group, string key)
        {
            CheckKey(group, key);
            return ParseDouble(group, key);
        }

        public double[] ReadAsDoubleArray(string key)
        {
            CheckKey(key);
            return ReadAsDoubleArray(NULL_GROUP, key);
        }

        public double[] ReadAsDoubleArray(string group, string key)
        {
            CheckKey(group, key);
            return ParseDoubleArray(group, key);
        }

        public float[] ReadAsFloatArray(string key)
        {
            CheckKey(key);
            return ReadAsFloatArray(NULL_GROUP, key);
        }

        public float[] ReadAsFloatArray(string group, string key)
        {
            CheckKey(group, key);
            return ParseFloatArray(group, key);
        }

        public int ReadAsInt(string key)
        {
            CheckKey(key);
            return ReadAsInt(NULL_GROUP, key);
        }

        public int ReadAsInt(string group, string key)
        {
            CheckKey(group, key);
            return ParseInt(group, key);
        }

        public long ReadAsLong(string key)
        {
            CheckKey(key);
            return ReadAsLong(NULL_GROUP, key);
        }

        public long ReadAsLong(string group, string key)
        {
            CheckKey(group, key);
            return ParseLong(group, key);
        }

        public int[] ReadAsIntArray(string key)
        {
            CheckKey(key);
            return ReadAsIntArray(NULL_GROUP, key);
        }

        public int[] ReadAsIntArray(string group, string key)
        {
            CheckKey(group, key);
            return ParseIntArray(group, key);
        }

        public DateTime ReadAsDateTime(string key)
        {
            CheckKey(key);
            return ReadAsDateTime(NULL_GROUP, key);
        }

        public DateTime ReadAsDateTime(string group, string key)
        {
            CheckKey(group, key);
            var dt = this[group, key];
            try
            {
                return DateTime.Parse(dt);
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "date", dt);
            }
        }

        public uint ReadAsBitMask(string key)
        {
            CheckKey(key);
            return ReadAsBitMask(NULL_GROUP, key);
        }

        public uint ReadAsBitMask(string group, string key)
        {
            CheckKey(group, key);
            string[] tokens = ParseString(this[group, key]).Split('#');
            try
            {
                return Convert.ToUInt32(tokens[1], int.Parse(tokens[0]));
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "bitmask", tokens[1] + " (base " + tokens[0] + ")");
            }
        }

        protected void CheckKey(string key)
        {
            if (!HasKey(key))
            {
                throw new MetadataKeyNotFoundException("key not found: " + key);
            }
        }
            
        protected void CheckKey(string group, string key)
        {
            if (!HasKey(group, key))
            {
                throw new MetadataKeyNotFoundException("key not found: " + group + "/" + key);
            }
        }
            
        protected void CheckForNull(string s)
        {
            if (s.Equals("NULL") || s.Equals("null"))
            {
                throw new MetadataNullValueException();
            }
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

        protected string[] ParseStringArray(string s)
        {
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s.Split(',').Select(x => ParseString(x)).ToArray();
        }

        protected int ParseInt(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            try
            {
                return int.Parse(s);
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "int", s);
            }
        }

        protected int[] ParseIntArray(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            try
            {
                return s.Split(',').Select(x => int.Parse(StripUnits(ParseString(x)))).ToArray();
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "int array", s);
            }
        }

        protected double ParseDouble(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            try
            {
                return double.Parse(s);
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "double", s);
            }
        }

        protected float[] ParseFloatArray(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            try
            {
                return s.Split(',').Select(x => float.Parse(StripUnits(ParseString(x)))).ToArray();
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "float array", s);
            }
        }

        protected double[] ParseDoubleArray(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            if (s.StartsWith("(") && s.EndsWith(")"))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            try
            {
                return s.Split(',').Select(x => double.Parse(StripUnits(ParseString(x)))).ToArray();
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "double array", s);
            }
        }

        protected long ParseLong(string group, string key)
        {
            var s = this[group, key];
            s = s.Trim();
            s = StripUnits(ParseString(s));
            CheckForNull(s);
            try
            {
                return long.Parse(s);
            }
            catch (FormatException)
            {
                throw new MetadataFormatException(group, key, "long", s);
            }
        }

        protected string StripUnits(string s)
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
