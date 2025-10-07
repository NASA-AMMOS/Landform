using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace JPLOPS.Imaging
{
    public class VICMetadataException : Exception
    {
        public VICMetadataException() { }
        public VICMetadataException(string message) : base(message) { }
        public VICMetadataException(string message, Exception inner) : base(message, inner) { }
    }


    public class PDSMetadata : RawMetadata
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
        public string DataPath;
        public long DataOffset;
        public uint BitMask;
        public bool BigEndian = true;

        // Optional Metadata
        public CameraModel CameraModel;


        public PDSMetadata(string filename) : base()
        {
            using (FileStream fs = File.OpenRead(filename))
            {
                if (IsVIC(fs, filename))
                {
                    InitVIC(fs);
                }
                else
                {
                    InitPDS(fs);
                }
            }
        }

        private bool IsVIC(FileStream fs, string filename)
        {
            try
            {
                string magic = "LBLSIZE";
                var sr = new StreamReader(fs);
                char[] buffer = new char[magic.Length];
                int n = sr.Read(buffer, 0, magic.Length);
                fs.Seek(0, SeekOrigin.Begin);
                if (n == magic.Length)
                {
                    return new string(buffer) == "LBLSIZE";
                }
            }
            catch (Exception) { } //ignore
            return Path.GetExtension(filename).ToUpper() == ".VIC"; //error or didn't read n bytes
        }

        public PDSMetadata(PDSMetadata that) : base(that)
        {
            this.BitDepth = that.BitDepth;
            this.BitMask = that.BitMask;
            this.BigEndian = that.BigEndian;
            this.RecordBytes = that.RecordBytes;
            this.DataOffset = that.DataOffset;
            this.DataPath = that.DataPath;
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

            if (this[NULL_GROUP, "^IMAGE"].Contains("\""))
            {
                //external file
                string[] data = ReadAsStringArray("^IMAGE");
                if (data.Length == 1)
                {
                    //external file (PDS standard reference 3, 14.1.1, case 3
                    this.DataOffset = 0;
                    this.DataPath = data[0];
                }
                else
                {
                    //external file (PDS standard reference 3, 14.1.1, case 4 and 5
                    throw new NotImplementedException("add support for external image file with offset");
                }
            }
            else if (this[NULL_GROUP, "^IMAGE"].Contains("<BYTES>"))
            {
                //byte offset (PDS standard reference 3, 14.1.1, case 2
                throw new NotImplementedException("add support for byte offsets");
            }
            else
            {
                //records offset (PDS standard reference 3, 14.1.1, case 1
                int carrot = (int)ReadAsInt("^IMAGE");
                this.DataOffset = (carrot - 1) * this.RecordBytes;
                this.DataPath = null;
            }

            try
            {
                this.CameraModel = new PDSCameraModelParser(this).Parse();
            }
            catch (MetadataException)
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
            if (ReadAsString("INTFMT") == "LOW")
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
                this.CameraModel = new PDSCameraModelParser(this).Parse();
            }
            catch (MetadataException)
            {
                this.CameraModel = null;
            }

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
                bool firstline = true;
                while ((line = file.ReadLine()) != null)
                {
                    line = line.Trim();

                    if (line.Length == 0 || line.StartsWith("/*"))
                    {
                        continue;
                    }

                    if (firstline)
                    {
                        if (!line.StartsWith("PDS") && !line.StartsWith("ODL"))
                        {
                            throw new InvalidDataException("no metadata in pds file");
                        }
                        firstline = false;
                    }
                    
                    if (line == "END")
                    {
                        break;
                    }

                    if (line.Split('=').Length == 2)
                    {
                        lines.Add(line);
                    }
                    else // continuation of the last line
                    {
                        lines[lines.Count - 1] += " " + line;
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
            var header = new Dictionary<string, Dictionary<string, string>>();
            header.Add(NULL_GROUP, new Dictionary<string, string>());

            StreamReader sr = new StreamReader(stream);
            char[] buffer = new char[100];
            sr.Read(buffer, 0, buffer.Length);
            stream.Position = 0;
            sr.DiscardBufferedData();

            var sizeMatch = Regex.Match(new string(buffer), @"LBLSIZE\s*=\s*(\d+)");
            if (!sizeMatch.Success)
            {
                throw new VICMetadataException($"VIC LBLSIZE not found");
            }
            int headerLength = int.Parse(sizeMatch.Groups[1].Value);
            
            buffer = new char[headerLength];
            sr.Read(buffer, 0, headerLength);

            //https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf
            //"[VICAR] Keywords are strings, up to 32 characters in length,
            //and consist of uppercase characters, underscores (_), and numbers (but should start with a letter)"

            //NAME='VAL', NAME='FOO ''BAR'' BAZ', NAME=VAL
            //also allows optional space before and after equals sign
            var regex = new Regex(@"\s*([A-Z][A-Z_0-9]*)\s*=\s*((?:'(?:[^']*(?:'')?)*')|\([^)]+\)|\S+)");

            string group = NULL_GROUP;
            foreach (Match match in regex.Matches(new string(buffer)))
            {
                string key = match.Groups[1].Value;
                string val = ParseString(match.Groups[2].Value);
                if (key == "PROPERTY" || key == "TASK")
                {
                    group = val;
                    if (!header.ContainsKey(group))
                    {
                        header.Add(group, new Dictionary<string, string>());
                    }
                }
                else
                {
                    //if (header[group].ContainsKey(key))
                    //{
                    //    Console.WriteLine("overwriting group={0} key={1} val={2} with {3}",
                    //                      group, key, header[group][key], val);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("group={0} key={1} val={2}", group, key, val);
                    //}
                    header[group][key] = val;
                }
            }

            return header;
        }
    }
}
