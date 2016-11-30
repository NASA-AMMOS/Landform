using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public class PDSMetadata : ImageMetadata
    {

        public string Filename;

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

        
        public CameraModel CameraModel {  get { return fieldReader.CameraModel; } }
        
    }
}
