using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Xml;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Represents an MSL location as read from locations XML file
    /// </summary>
    public class MSLLocation
    {
        public Vector3 Position;
        public Vector2 LatLon; //X=latitude, Y=longitude
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

        public const string DEFAULT_FILENAME = "locations.xml";
        public const string DEFAULT_URL = "http://mars.jpl.nasa.gov/msl-raw-images/" + DEFAULT_FILENAME;

        public const string BASEMAP_FILENAME = "out_deltaradii_smg_1m.tif";
        public const string BASEMAP_URL = "s3://bucket/TerrainSourceAssets/basemaps/" + BASEMAP_FILENAME;

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

        public static MSLLocations Load(string fileOrUrl)
        {
            return fileOrUrl.ToLower().StartsWith("http") ? LoadFromUrl(fileOrUrl) : LoadFromFile(fileOrUrl);
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
        public MSLLocation GetLocation(SiteDrive sd)
        {
            MSLLocation loc = null;
            if(this.locations.TryGetValue(sd, out loc))
            {
                return loc;
            }
            return null;
        }

        private GISElevationMap basemapDEM = null;
        private double? basemapDEMZ0 = null;

        public bool HasBasemapDEM { get { return basemapDEM != null; } }

        public void LoadBasemapDEM(string file)
        {
            basemapDEM = new GISElevationMap(file, "Mars");
        }

        public double GetZFromBasemap(double lat, double lon)
        {
            return basemapDEM.InterpolateElevationAtLonLat(new Vector2(lon, lat));
        }

        /// <summary>
        /// locations.xml Z values are in site frame
        /// if you instead want Z to be relative to the landing site, like the Places database
        /// this API will do that by estimating the difference in elevations using the orbital DEM
        /// </summary>
        public MSLLocation SetZFromBasemap(MSLLocation loc, int radius = 2)
        {
            if (basemapDEM == null)
            {
                throw new InvalidOperationException("basemap DEM not loaded");
            }
            if (!basemapDEMZ0.HasValue)
            {
                var loc0 = GetLocation(new SiteDrive(1, 0));
                basemapDEMZ0 = basemapDEM.InterpolateElevationAtLonLat(new Vector2(loc0.LatLon.Y, loc0.LatLon.X), 2);
            }
            var z = basemapDEM.InterpolateElevationAtLonLat(new Vector2(loc.LatLon.Y, loc.LatLon.X), radius);
            loc.Position.Z = -(z - basemapDEMZ0.Value); 
            return loc;
        }
    }
}
