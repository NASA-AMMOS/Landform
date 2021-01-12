using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Xna.Framework;
using RestSharp;
using RestSharp.Authenticators;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class PlacesConfig : SingletonConfig<PlacesConfig>
    {
        public const string CONFIG_FILENAME = "places"; //config file will be ~/.landform/places.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        //PLACES instance URL
        //default is null which disables PlacesDB
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_URL")]
        public string Url { get; set; }

        //PLACES solution view
        //default is null which disables PlacesDB
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_VIEW")]
        public string View { get; set; }

        //username for http basic auth
        //default is null whcih means disable basic auth
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_USERNAME")]
        public string Username { get; set; }

        //password for http basic auth
        //default is null whcih means disable basic auth
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_API_KEY")]
        public string APIKey { get; set; }

        //name of auth cookie
        //null means no auth cookie
        //default is "ssosession"
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_NAME")]
        public string AuthCookieName { get; set; } = "ssosession";
 
        //auth cookie
        //default is null which means read from file, if any
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_VALUE")]
        public string AuthCookieValue { get; set; }

        //read auth cookie from file
        //default is null which disables auth cookie file
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_FILE")]
        public string AuthCookieFile { get; set; }

        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        //application/xml or application/json (experimental)
        [ConfigEnvironmentVariable("LANDFORM_PLACES_RESPONSE_TYPE")]
        public string ResponseType { get; set; } = "application/xml";

        //max response time including all retries
        //unlimited if non-positive
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/1154
        [ConfigEnvironmentVariable("LANDFORM_PLACES_TIMEOUT_SECONDS")]
        public int TimeoutSeconds { get; set; } = 600;

        //max number of request retries
        //non-positive same as 1
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/1154
        [ConfigEnvironmentVariable("LANDFORM_PLACES_MAX_RETRIES")]
        public int MaxRetries { get; set; } = 20;
    }

    /// <summary>
    /// PLACES is a service that JPL runs for storing and reporting position estimates of spacecraft such as rovers.
    /// This class interfaces with PLACES to compute relative rover positions between site drives.
    /// </summary>
    public class PlacesDB
    {
        public string FALLBACK_VIEW = "telemetry";

        private ILogger logger;

        private bool debug;

        private PlacesConfig config;

        private string view;
        private string cookieValue;

        //avoid hitting the upstream service too hard
        //important: this is explicitly *not* a ConcurrentDictionary
        //we lock on it to serialize requests
        //that handles the case of launching multiple initial requests for the same query in parallel
        //query => response
        Dictionary<string, string> cache = new Dictionary<string, string>();

        private ConcurrentDictionary<SiteDrive, Vector3> cachedOffsetFromStart =
            new ConcurrentDictionary<SiteDrive, Vector3>();

        public PlacesDB(ILogger logger = null, bool debug = false)
        {
            this.logger = logger;
            this.debug = debug;

            config = PlacesConfig.Instance;

            if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.View))
            {
                throw new Exception("no PLACES database for mission");
            }

            if (!string.IsNullOrEmpty(config.AuthCookieValue))
            {
                cookieValue = config.AuthCookieValue;
            }
            else if (!string.IsNullOrEmpty(config.AuthCookieFile))
            {
                string path = config.AuthCookieFile;
                if (path.StartsWith("~"))
                {
                    path = Path.Combine(PathHelper.GetHomeDir(), path.Substring(2));
                }
                if (!File.Exists(path))
                {
                    throw new Exception($"cannot read PlacesDB auth cookie from \"{path}\": file not found");
                }
                try
                {
                    if (logger != null)
                    {
                        logger.LogInfo("reading PlacesDB auth cookie from file \"{0}\"", path);
                    }
                    cookieValue = File.ReadAllText(path);
                    if (string.IsNullOrEmpty(cookieValue))
                    {
                        throw new Exception("empty file");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"error reading PlacesDB auth cookie from \"{path}\": {ex.Message}");
                }
            }

            try
            {
                view = config.View;
                GetOffsetToStart(new SiteDrive(1, 0)); //test query
            }
            catch
            {
                if (logger != null)
                {
                    logger.LogWarn("PlacesDB test query for sitedrive (1, 0) failed" +
                                   ", check list at {0}/view/{1}/rmcs", config.Url, view);
                }
                view = FALLBACK_VIEW;
                logger.LogWarn("trying fallback view {0}", view);
                try
                {
                    GetOffsetToStart(new SiteDrive(1, 0));
                }
                catch
                {
                    if (logger != null)
                    {
                        logger.LogError("PlacesDB test query for sitedrive (1, 0) failed" +
                                        ", check list at {0}/view/{1}/rmcs", config.Url, view);
                    }
                    throw;
                }
            }
        }

        private string Fetch(string query)
        {
            lock (cache)
            {
                if (cache.ContainsKey(query))
                {
                    var doc = cache[query];
                    if (doc == null)
                    {
                        throw new Exception(string.Format("PlacesDB: query {0} failed, not retrying", query));
                    }
                    return doc;
                }

                Uri uri = new Uri(config.Url);
                
                RestClient client = new RestClient();
                client.BaseUrl = uri;
                
                if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.APIKey))
                {
                    client.Authenticator = new HttpBasicAuthenticator(config.Username, config.APIKey);
                }
                
                if (!string.IsNullOrEmpty(config.AuthCookieName) && !string.IsNullOrEmpty(cookieValue))
                {
                    client.CookieContainer = new CookieContainer();
                    var cookie = new Cookie(config.AuthCookieName, cookieValue) { Domain = uri.Host };
                    client.CookieContainer.Add(cookie);
                }
                
                var request = new RestRequest();
                request.Resource = query;

                if (!string.IsNullOrEmpty(config.ResponseType))
                {
                    request.AddHeader("Accept", config.ResponseType);
                }

                int maxSec = config.TimeoutSeconds;
                if (maxSec > 0)
                {
                    request.Timeout = maxSec * 1000;
                }

                double startSec = UTCTime.Now();
                int maxRetries = Math.Max(config.MaxRetries, 1);
                string err = null;
                for (int i = 0; i < maxRetries; i++)
                {
                    IRestResponse response = client.Execute(request);
                
                    if (response.ResponseStatus == ResponseStatus.Completed &&
                        response.StatusCode == HttpStatusCode.OK)
                    {
                        string content = response.Content;
                        cache[query] = content;
                        Debug("request: {0}, response:\n{1}", config.Url + "/" + query, content);
                        return content;
                    }
                    else
                    {
                        err = string.Format("got status code {0} for {1} on try {2}: {3}", response.StatusCode,
                                            config.Url + "/" + query, i, response.ErrorMessage);
                        Debug(err);
                        if (response.StatusCode != HttpStatusCode.BadGateway && //proxies can impose their own timeout
                            response.ResponseStatus != ResponseStatus.TimedOut)
                        {
                            throw new Exception(err);
                        }
                    }
                    if (maxSec > 0 && ((UTCTime.Now() - startSec) > maxSec))
                    {
                        err = string.Format("exceeded max time {0} for {1} on try {2}", Fmt.HMS(maxSec * 1000),
                                            config.Url + "/" + query, i);
                        Debug(err);
                        throw new Exception(err);
                    }
                }
                throw new Exception(err);
            }
        }

        private void Debug(string msg, params Object[] args)
        {
            if (debug)
            {
                if (logger != null)
                {
                    logger.LogInfo("PlacesDB " + msg, args);
                }
                else
                {
                    Console.WriteLine("PlacesDB " + msg, args);
                }
            }
        }

        private XmlDocument ParseXml(string query, string response)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(response);
                return doc;
            }
            catch (System.Xml.XmlException ex)
            {
                throw new Exception(string.Format("PlacesDB: error parsing response for request {0}: {1}",
                                                  query, ex.Message));
            }
        }

        private class JsonTranslation
        {
            public double[] offset = new double[0];
        }

        private class JsonDocument
        {
            public JsonTranslation[] translations = new JsonTranslation[0];
        }

        private JsonDocument ParseJson(string query, string response)
        {
            return JsonHelper.FromJson<JsonDocument>(response);
        }

        private Vector3 GetOffset(string query)
        {
            string response = Fetch(query);
            Vector3 offset = new Vector3();
            if (response.StartsWith("{"))
            {
                JsonDocument doc = ParseJson(query, response);
                var translations = doc.translations;
                if (translations.Length != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(translations[0].offset[0], translations[0].offset[1], translations[0].offset[2]);
            }
            else
            {
                XmlDocument doc = ParseXml(query, response);
                XmlNodeList nodes = doc.GetElementsByTagName("offset");
                if (nodes.Count != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(double.Parse(nodes[0].Attributes["x"].Value),
                                     double.Parse(nodes[0].Attributes["y"].Value),
                                     double.Parse(nodes[0].Attributes["z"].Value));
            }

            Debug("request: {0}, offset {1}", query, offset);

            return offset;
        }

        private interface IExpectedValue
        {
            bool Equals(string str);
            string ToString();
        }

        private class ExpectedString : IExpectedValue
        {
            private string value;

            public ExpectedString(string str)
            {
                value = str;
            }

            public bool Equals(string str)
            {
                return string.Equals(value, str, StringComparison.OrdinalIgnoreCase);
            }

            public override string ToString()
            {
                return value;
            }
        }

        private class ExpectedNumber : IExpectedValue
        {
            private double value, tol;

            public ExpectedNumber(double num, double eps = 1e-3)
            {
                value = num;
                tol = eps;
            }

            public bool Equals(string str)
            {
                return double.TryParse(str, out double d) && Math.Abs(d - value) <= tol;
            }

            public override string ToString()
            {
                return value.ToString();
            }
        }

        private string[] CheckXMLDocument(XmlDocument doc, Dictionary<string, IExpectedValue> expected)
        {
            var missing = new HashSet<string>();
            missing.UnionWith(expected.Keys);
            var variances = new List<string>();
            foreach (XmlElement item in doc.GetElementsByTagName("item"))
            {
                XmlNodeList keyElts = item.GetElementsByTagName("key");
                if (keyElts.Count == 1)
                {
                    string key = keyElts[0].InnerText.Trim().ToLower();
                    if (expected.ContainsKey(key))
                    {
                        XmlNodeList valElts = item.GetElementsByTagName("value");
                        if (valElts.Count == 1)
                        {
                            missing.Remove(key);
                            string val = valElts[0].InnerText.Trim();
                            if (!expected[key].Equals(val))
                            {
                                variances.Add($"expected {key} = {expected[key].ToString()}, got {val}");
                            }
                        }
                    }
                }
            }
            if (missing.Count > 0)
            {
                variances.Add("missing " + string.Join(", ", missing));
            }
            return variances.ToArray();
        }

        private Dictionary<string, double> GetOrbitalMetadata(int index, string[] keys)
        {
            string query = string.Format("rmc/orbital({0})/metadata", index);
            string response = Fetch(query);

            if (response.StartsWith("{"))
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/752
                throw new Exception("PlacesDB: orbital metadata Json TODO");
            }

            var values = new Dictionary<string, double>();

            var doc = ParseXml(query, response);
            foreach (XmlElement item in doc.GetElementsByTagName("item"))
            {
                XmlNodeList keyElts = item.GetElementsByTagName("key");
                if (keyElts.Count == 1)
                {
                    string key = keyElts[0].InnerText.Trim().ToLower();
                    if (keys.Contains(key))
                    {
                        XmlNodeList valElts = item.GetElementsByTagName("value");
                        if (valElts.Count == 1)
                        {
                            string val = valElts[0].InnerText.Trim();
                            if (double.TryParse(val, out double d))
                            {
                                values[key] = d;
                            }
                        }
                    }
                }
            }

            return values;
        }

        private string[] CheckOrbitalMetadata(int index, double xyScale = -1, Vector2? ulcEastingNorthing = null,
                                              string filename = null,
                                              Dictionary<string, IExpectedValue> expected = null)
        {
            var cfg = OrbitalConfig.Instance;

            expected = expected ?? new Dictionary<string, IExpectedValue>();

            if (xyScale > 0)
            {
                expected["x_scale"] = expected["y_scale"] = new ExpectedNumber(xyScale);
            }

            if (ulcEastingNorthing.HasValue)
            {
                expected["upper_left_easting_m"] = new ExpectedNumber(ulcEastingNorthing.Value.X);
                expected["upper_left_northing_m"] = new ExpectedNumber(ulcEastingNorthing.Value.Y);
            }

            expected["projection"] = new ExpectedString("Equirectangular");

            expected["ellipsoid_radius"] = new ExpectedNumber(PlanetaryBody.GetByName(cfg.BodyName).Radius);

            expected["coord_sys_definition"] = new ExpectedString("+X is North, +Y is East, +Z is Down");

            if (!string.IsNullOrEmpty(filename))
            {
                expected["filename"] = new ExpectedString(filename);
            }

            string query = string.Format("rmc/orbital({0})/metadata", index);
            string response = Fetch(query);

            if (response.StartsWith("{"))
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/752
                throw new Exception("PlacesDB: orbital metadata Json TODO");
            }

            return CheckXMLDocument(ParseXml(query, response), expected);
        }

        public string[] CheckOrbitalDEMMetadata(int index, double xyScale = -1, Vector2? ulcEastingNorthing = null,
                                                string filename = null)
        {
            var cfg = OrbitalConfig.Instance;

            if (string.IsNullOrEmpty(filename))
            {
                filename = StringHelper.GetLastUrlPathSegment(cfg.DEMURL); //null/empty ok
            }

            var expected = new Dictionary<string, IExpectedValue>();

            if (cfg.DEMElevationScale > 0)
            {
                expected["z_scale"] = new ExpectedNumber(cfg.DEMElevationScale);
            }

            return CheckOrbitalMetadata(index, xyScale, ulcEastingNorthing, filename, expected);
        }

        public string[] CheckOrbitalImageMetadata(int index, double xyScale = -1, Vector2? ulcEastingNorthing = null,
                                                  string filename = null)
        {
            
            var cfg = OrbitalConfig.Instance;

            if (string.IsNullOrEmpty(filename))
            {
                filename = StringHelper.GetLastUrlPathSegment(cfg.ImageURL); //null/empty ok
            }

            return CheckOrbitalMetadata(index, xyScale, ulcEastingNorthing, filename);
        }

        /// <summary>
        /// returns X = easting meters, Y = northing meters
        /// easting is distance along equator east from prime meridian
        /// northing is distance above equator along a meridian
        /// requires both upper_left_{easting,northing}_m to be present in the metadata for orbitalIndex
        /// </summary>
        public Vector2? GetULCEastingNorthing(int orbitalIndex)
        {
            var keys = new string[] { "upper_left_easting_m", "upper_left_northing_m" };
            var md = GetOrbitalMetadata(orbitalIndex, keys);
            return md.Count == 2 ? new Vector2(md[keys[0]], md[keys[1]]) : (Vector2?)null;
        }

        /// <summary>
        /// returns X = easting meters per pixel, Y = northing meters per pixel, both positive
        /// requires both x_scale and y_scale to be present in the metadata for orbitalIndex
        /// </summary>
        public Vector2? GetOrbitalMetersPerPixel(int orbitalIndex)
        {
            var keys = new string[] { "x_scale", "y_scale" };
            var md = GetOrbitalMetadata(orbitalIndex, keys);
            return md.Count == 2 ? new Vector2(md[keys[0]], md[keys[1]]) : (Vector2?)null;
        }

        /// <summary>
        /// returns X = col, Y = row pixel for sitedrive sd in orbitalIndex
        /// </summary>
        public Vector2 GetOrbitalPixel(SiteDrive sd, int orbitalIndex, double defMetersPerPixel = 0,
                                       Vector2? defULCEastingNorthing = null)
        {
            var eastingNorthingElevation = GetEastingNorthingElevation(sd, orbitalIndex, false, defULCEastingNorthing);
            var mpp = GetOrbitalMetersPerPixel(orbitalIndex);
            if (!mpp.HasValue)
            {
                if (defMetersPerPixel > 0)
                {
                    mpp = defMetersPerPixel * Vector2.One;
                }
                else
                {
                    throw new Exception($"cannot get orbital pixel for site drive {sd}: " +
                                        "missing PlacesDB meters per pixel metadata {x,y}_scale " +
                                        $"for index {orbitalIndex} and default meters per pixel not specified");
                }
            }
            double col = eastingNorthingElevation.X / mpp.Value.X;
            double row = -1 * eastingNorthingElevation.Y / mpp.Value.Y;
            return new Vector2(col, row);
        }

        /// <summary>
        /// Formulate a PlacesDB query reference for the given sitedrive (S,D).
        /// If D=0 then the query will be of the form site(S), because queries like rover(S,0) generally don't work.
        /// Otherwise the query will be of the form rover(S,D,^), meaning the frame of the latest available pose (^)
        /// in that site and drive.  Note that in some venues queries like rover(S,D) work but in others they don't,
        /// but adding the carat should work in all cases (per Kevin Grimes).
        /// </summary>
        private static string SDRef(SiteDrive sd)
        {
            return sd.Drive > 0 ? $"rover({sd.Site},{sd.Drive},^)" : $"site({sd.Site})";
        }

        /// <summary>
        /// returns X = easting meters, Y = northing meters, Z = elevation meters
        ///
        /// easting is distance along equator east from prime meridian if absolute, else east from ULC for orbitalIndex
        /// northing is distance along a meridian above equator if absolute, else north from ULC for orbitalIndex
        ///
        /// proper behavior with absolute=false requires both upper_left_{easting,northing}_m to be present
        /// in the metadata for orbitalIndex or defULCEastingNorthing to be specified
        /// </summary>
        public Vector3 GetEastingNorthingElevation(SiteDrive sd, int orbitalIndex, bool absolute = true,
                                                   Vector2? defULCEastingNorthing = null)
        {
            string query = string.Format("query/primary/{0}?from={1}&to=orbital({2})", view, SDRef(sd), orbitalIndex);

            //offset is in standard mission local level frame: +X north, +Y east, +Z down
            var v = GetOffset(query);
            double easting = v.Y; // distance along surface on equator east of prime meridian
            double northing = v.X; // distance along surface on prime meridian north of equator
            double elevation = -v.Z;

            var ulc = GetULCEastingNorthing(orbitalIndex);
            if (absolute)
            {
                if (ulc.HasValue)
                {
                    easting += ulc.Value.X;
                    northing += ulc.Value.Y;
                }
                //ulc = null means either/both upper_left_{easting,northing}_m were missing
                //in the metadata for orbitalIndex
                //but in that case it appears that the PlacesDB easting/northing offset is already absolute
            }
            else if (!ulc.HasValue)
            {
                //upper_left_{easting,northing}_m were absent, but absolute=false: need to subtract off ULC
                if (defULCEastingNorthing.HasValue)
                {
                    easting -= defULCEastingNorthing.Value.X;
                    northing -= defULCEastingNorthing.Value.Y;
                }
                else
                {
                    throw new Exception($"cannot get relative easting/northing for site drive {sd}: " +
                                        "missing PlacesDB ULC easting/northing metadata " +
                                        $"upper_left_{{easting,northing}}_m for index {orbitalIndex} and " +
                                        "default ULC easting/northing not specified");
                }
            }
            return new Vector3(easting, northing, elevation);
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from fromSD to toSite.
        /// </summary>
        public Vector3 GetOffsetToSite(SiteDrive fromSD, int toSite)
        {
            return GetOffset(string.Format("query/primary/{0}?from={1}&to=site({2})", view, SDRef(fromSD), toSite));
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from sd to site 1, drive 0 (landing).
        /// </summary>
        public Vector3 GetOffsetToStart(SiteDrive sd)
        {
            return cachedOffsetFromStart.GetOrAdd(sd, _ => GetOffsetToSite(sd, 1));
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from fromSD to toSD.
        /// </summary>
        public Vector3 GetOffset(SiteDrive fromSD, SiteDrive toSD)
        {
            return GetOffset(string.Format("query/primary/{0}?from={1}&to={2}", view, SDRef(fromSD), SDRef(toSD)));
        }
    }
}
