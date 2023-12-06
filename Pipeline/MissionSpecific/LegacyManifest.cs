using log4net;
using System;
using System.Linq;
using System.Xml;
using System.Net;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
namespace JPLOPS.Pipeline
{
    public class MSLLegacyManifest
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MSLLegacyManifest));

        private ConcurrentDictionary<SiteDrive, Matrix> primarySiteDriveToSiteDrive;
        public readonly SiteDrive PrimarySiteDrive;

        public SiteDrive[] KnownSiteDrives
        {
            get { return primarySiteDriveToSiteDrive.Keys.ToArray(); }
        }

        //relative transforms between primary site drive and arbitrary sitedrive
        // may need to convert them to a global frame to use with landform by multiplying with
        // primary site drive to root, eg. sitedriveToRoot = inv(primarytoSiteDrive) * primaryToRoot
        public Matrix? GetRelativeTransformPrimaryToSiteDrive(SiteDrive sd)
        {
            if(primarySiteDriveToSiteDrive.TryGetValue(sd, out Matrix matrix))
            {
                return matrix;
            }

            return null;
        }

        public static MSLLegacyManifest LoadFromUrl(string url)
        {
            logger.InfoFormat("fetching MSL legacy manifest from {0}", url);
            WebRequest req = WebRequest.Create(url);
            WebResponse resp = req.GetResponse();
            XmlDocument doc = new XmlDocument();
            doc.Load(resp.GetResponseStream());
            return new MSLLegacyManifest(doc);
        }
        public static MSLLegacyManifest LoadFromFile(string file)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(file);
            return new MSLLegacyManifest(doc);
        }

        public static MSLLegacyManifest Load(string fileOrUrl)
        {
            return fileOrUrl.ToLower().StartsWith("http") ? LoadFromUrl(fileOrUrl) : LoadFromFile(fileOrUrl);
        }

        private MSLLegacyManifest(XmlDocument doc)
        {
            this.primarySiteDriveToSiteDrive = new ConcurrentDictionary<SiteDrive, Matrix>();

            //add the keys
            XmlNodeList sdNodes = doc.SelectNodes("scene/sitedrives/sitedrive");
            foreach (XmlNode sdNode in sdNodes)
            {
                SiteDrive sd = new SiteDrive(sdNode.Attributes["id"].Value);
                bool isPrimary = (sdNode.Attributes.Count > 3) && bool.Parse(sdNode.Attributes["primary"].Value);
                if (isPrimary)
                {
                    PrimarySiteDrive = sd;
                }

                if (!primarySiteDriveToSiteDrive.TryAdd(sd, Matrix.Identity))
                {
                    throw new Exception("LegacyManifest sitedrive creation found duplicate item");
                }
            }

            //add the values
            XmlNodeList transformNodes = doc.SelectNodes("scene/projections/transforms/transform");
            foreach (XmlNode transformNode in transformNodes)
            {
                SiteDrive sd = new SiteDrive(transformNode["sitedrive"].InnerText);
                char[] spaceSep = { ' ' };
                double[] m = transformNode["primary_to_local_level_matrix"].InnerText.Split(spaceSep, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x)).ToArray();

                //column major, transpose on creation
                Matrix primarySiteDriveToSiteDriveLocalLevel = new Matrix(m[0],m[4],m[8],m[12],
                                                                          m[1],m[5],m[9],m[13],
                                                                          m[2],m[6],m[10],m[14],
                                                                          m[3],m[7],m[11],m[15]);
                if (!primarySiteDriveToSiteDrive.TryUpdate(sd, primarySiteDriveToSiteDriveLocalLevel, Matrix.Identity))
                {
                    throw new Exception("LegacyManifest transform update found duplicate item");
                }
            }
        }


    }
}
