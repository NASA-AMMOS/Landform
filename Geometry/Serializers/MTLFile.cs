using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JPLOPS.Geometry
{
    public class MTLFile
    {
        private Dictionary<string, string> textureFiles = new Dictionary<string, string>();

        private List<string> comments = new List<string>();

        private Dictionary<string, string> commentValues = new Dictionary<string, string>();

        public MTLFile(string filename)
        {
            using (StreamReader sr = new StreamReader(filename))
            {
                string materialName = null;
                for (string line = sr.ReadLine(); line != null; line = sr.ReadLine())
                {
                    string[] parts = line.Split().Where(s => s.Length != 0).ToArray();
                    if (parts.Length == 0)
                    {
                        continue;
                    }
                    if (parts[0].StartsWith("#"))
                    {
                        ParseComment(parts);
                    }
                    else if (parts[0] == "newmtl")
                    {
                        materialName = parts[1];
                    }
                    else if (parts[0] == "map_Kd" && materialName != null)
                    {
                        textureFiles[materialName] = parts[1];
                    }
                }
            }
        }

        public string GetTextureFile(string material)
        {
            return textureFiles.ContainsKey(material) ? textureFiles[material] : null;
        }

        public string GetCommentValue(string key)
        {
            return commentValues.ContainsKey(key) ? commentValues[key] : null;
        }

        public IEnumerable<string> GetCommentKeys()
        {
            return commentValues.Keys;
        }

        public IEnumerable<string> GetComments()
        {
            return comments;
        }

        private void ParseComment(string[] parts)
        {
            //#lbl NLF_0393R0393114157_000RASLN0180000PIXX00002_0M0290J01.lbl
            //#obj NLF_0393R0393114157_000RASLN0180000PIXX00002_0M0290J01.obj
            //#xyz NLF_0393R0393114157_000XYZLN0180000PIXX00002_0M0290J01.VIC
            //#mtl NLF_0393R0393114157_000RASLN0180000PIXX00002_0M0290J01.mtl
            //
            //#REFERENCE_COORD_SYSTEM_NAME SITE_FRAME
            //#REFERENCE_COORD_SYSTEM_INDEX_NAME ( site )
            //#REFERENCE_COORD_SYSTEM_INDEX ( 18 )
        
            string key = parts[0];
            if (key.StartsWith("#"))
            {
                key = key.Substring(1);
            }

            comments.Add(key);

            if (parts.Length > 1)
            {
                string val = parts[1];
                if (val == "(" && parts.Length > 2)
                {
                    val = parts[2];
                }
                if (val.StartsWith("(") && val.EndsWith(")"))
                {
                    val = val.Substring(1, val.Length - 2);
                }
                commentValues[key] = val;
            }
        }
    }
}
