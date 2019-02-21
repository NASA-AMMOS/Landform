using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace OPS.Pipeline
{
    /// <summary>
    /// Represents an MSL location as read from locations XML file
    /// </summary>
    public class MSLLocation
    {
        public Vector3 Position;
        public Vector2 LatLon;
        public SiteDrive SiteDrive;
        public int StartSol;
        public int EndSol;

        public MSLLocation(Vector3 position, Vector2 latLon, SiteDrive siteDrive, int startSol, int endSol)
        {
            this.Position = position;
            this.LatLon = latLon;
            this.SiteDrive = siteDrive;
            this.StartSol = startSol;
            this.EndSol = endSol;
        }
    }

    /// <summary>
    /// Reads MSL location priors from locations xml.  Locations are relative to an orbital mosaic basemap.
    /// Once constructed, the object can be shared between multiple threads. 
    /// </summary>
    public class MSLLocations
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MSLLocations));

        const string DEFAULT_URL = "http://mars.jpl.nasa.gov/msl-raw-images/locations.xml";

        private ConcurrentDictionary<SiteDrive, MSLLocation> locations; 
      
        public static MSLLocations LoadFromUrl(string url = DEFAULT_URL)
        {
            logger.InfoFormat("fetching MSL locations from {0}", url);
            WebRequest req = WebRequest.Create(url);
            WebResponse resp = req.GetResponse();
            XmlDocument doc = new XmlDocument();
            doc.Load(resp.GetResponseStream());
            return new MSLLocations(doc);
        }

        public static MSLLocations LoadFromFile(string file)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(file);
            return new MSLLocations(doc);
        }

        private MSLLocations(XmlDocument doc)
        {
            this.locations = new ConcurrentDictionary<SiteDrive, MSLLocation>();

            XmlNodeList nodes = doc.SelectNodes("msl/location");
            foreach (XmlNode location in nodes)
            {
                double x = double.Parse(location["x"].InnerText),
                       y = double.Parse(location["y"].InnerText),
                       z = double.Parse(location["z"].InnerText);
                double lat = double.Parse(location["lat"].InnerText),
                       lon = double.Parse(location["lon"].InnerText);
                int site = int.Parse(location["site"].InnerText);
                int drive = int.Parse(location["drive"].InnerText);
                int startSol = int.Parse(location["startSol"].InnerText.Trim());
                int endSol = int.Parse(location["endSol"].InnerText.Trim());
                SiteDrive sd = new SiteDrive(site, drive);
                MSLLocation loc = new MSLLocation(new Vector3(x, y, z), new Vector2(lat, lon), sd, startSol, endSol);
                if (!locations.TryAdd(sd, loc)) throw new Exception("MSLLocations creation found duplicate item"); 
            }
        }

        public int MaxSite
        {
            get { return locations.Keys.Select(x => x.Site).Max();  }
        }

        public int MaxDrive(int site)
        {
            return locations.Keys.Where(x => x.Site == site).Select(x => x.Drive).Max(); 
        }

        public int MinDrive(int site)
        {
            return locations.Keys.Where(x => x.Site == site).Select(x => x.Drive).Min();
        }

        /// <summary>
        /// Look up a location for this site drive.  Return null if it doesn't exist
        /// </summary>
        /// <param name="sd"></param>
        /// <returns></returns>
        public MSLLocation Location(SiteDrive sd)
        {
            MSLLocation loc = null;
            if(this.locations.TryGetValue(sd, out loc))
            {
                return loc;
            }
            return null;
        }
    }
}
