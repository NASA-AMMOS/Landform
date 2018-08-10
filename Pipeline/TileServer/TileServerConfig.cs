using Newtonsoft.Json;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{

    public class TileServerConfig : SingletonConfig<TileServerConfig>
    {
        [ConfigEnvironmentVariable("TILE_SERVER_REGION")]
        public string Region { get; set; }


        [JsonIgnore]
        private string profile;

        [ConfigEnvironmentVariable("TILE_SERVER_PROFILE")]
        public string Profile
        {
            get
            {
                if(profile != null && profile.ToLower() == "null")
                {
                    return null;
                }
                return profile;
            }
            set
            {
                if(value != null && value.ToLower() == "null")
                {
                    profile = null;
                }
                else
                {
                    profile = value;
                }
            }
        }

        [ConfigEnvironmentVariable("TILE_SERVER_VENUE_NAME")]
        public string VenueName { get; set; }

        [ConfigEnvironmentVariable("TILE_SERVER_S3_URL")]
        public string S3Url { get; set; }

        protected override string ConfigFilename()
        {
            return "tileserver";
        }

        public override void Validate()
        {
            if(Region == null)
            {
                throw new Exception("Undefined AWS region in TileServerConfig");
            }
            if (VenueName == null)
            {
                throw new Exception("Undefined venue name in TileServerConfig");
            }
            if (S3Url == null)
            {
                throw new Exception("Undefined S3 url in TileServerConfig");
            }
        }

        public string InputUrl(string projectName, string filename="")
        {
            return GetUrl("input", projectName, filename);
        }
        public string WWWUrl(string projectName, string filename = "")
        {
            return GetUrl("www", projectName, filename);
        }
        public string ChunkUrl(string projectName, string filename = "")
        {
            return GetUrl("chunk", projectName, filename);
        }
        public string TileUrl(string projectName, string filename = "")
        {
            return GetUrl("tile", projectName, filename);
        }

        string GetUrl(string folder, string projectName, string filename)
        {
            return new Uri(Path.Combine(S3Url, VenueName, folder, projectName, filename).Replace('\\','/')).ToString();
        }

    }
}
