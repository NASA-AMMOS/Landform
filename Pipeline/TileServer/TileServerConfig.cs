using Newtonsoft.Json;
using log4net;
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

        [JsonIgnore]
        private string msliceProfile;

        [ConfigEnvironmentVariable("TILE_SERVER_MSLICE_PROFILE")]
        public string MSLICEProfile
        {
            get
            {
                if (msliceProfile != null && msliceProfile.ToLower() == "null")
                {
                    return null;
                }
                return msliceProfile;
            }
            set
            {
                if (value != null && value.ToLower() == "null")
                {
                    msliceProfile = null;
                }
                else
                {
                    msliceProfile = value;
                }
            }
        }

        [ConfigEnvironmentVariable("TILE_SERVER_MSLICE_S3_URL")]
        public string MSLICES3Url { get; set; }

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

        public string InputUrl(string projectName, string filename="", bool https=false)
        {
            return GetUrl("input", projectName, filename, https);
        }
        public string WWWUrl(string projectName, string filename = "", bool https=false)
        {
            return GetUrl("www", projectName, filename, https);
        }
        public string ChunkUrl(string projectName, string filename = "", bool https=false)
        {
            return GetUrl("chunk", projectName, filename, https);
        }
        public string TileUrl(string projectName, string filename = "", bool https=false)
        {
            return GetUrl("tile", projectName, filename, https);
        }

        string GetUrl(string folder, string projectName, string filename, bool https=false)
        {
            string baseUrl = S3Url;
            if (https)
            {
                string bucketName = new Uri(S3Url).Host;
                baseUrl = "https://" + bucketName + ".s3.amazonaws.com";
            }
            return new Uri(Path.Combine(baseUrl, VenueName, folder, projectName, filename).Replace('\\','/')).ToString();
        }

        public static string ConvertUrlToHttps(string url)
        {
            var uri = new Uri(url);
            switch (uri.Scheme)
            {
                case "https": return url;
                case "s3": return (new Uri("https://" + uri.Host + ".s3.amazonaws.com/" + uri.AbsolutePath)).ToString();
                default: throw new Exception("unrecognized protocol: \"" + url + "\"");
            }
        }

        public void Dump(ILog logger)
        {
            logger.Info("Landform venue: " + VenueName);
            logger.Info("S3URL: " + S3Url);
            logger.Info("AWS Region: " + Region);
            logger.Info("AWS Profile: " + Profile);
            logger.Info("MSLICE AWS Profile: " + MSLICEProfile);
            logger.Info("MSLICE S3 URL: " + MSLICES3Url);
        }
    }
}
