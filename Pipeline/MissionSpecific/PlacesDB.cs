//#define CACHE_FAILED_REQUESTS
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Net;
using System.Xml;
using Microsoft.Xna.Framework;
using RestSharp;
using RestSharp.Authenticators;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
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
        //default is null
        //null or empty disables PlacesDB
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        //can be one or more comma separated views, in order of preference from worst to best (best last)
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
        [ConfigEnvironmentVariable("LANDFORM_PLACES_TIMEOUT_SECONDS")]
        public int TimeoutSeconds { get; set; } = 600;

        //max number of request retries
        //non-positive same as 1
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_MAX_RETRIES")]
        public int MaxRetries { get; set; } = 20;

        //always do a query like view/VIEW/rmc/REF to verify availability of REF in view
        //otherwise a query like query/primary/VIEW?from=REF0&to=REF1 will be used for pairs of refs
        [ConfigEnvironmentVariable("LANDFORM_PLACES_ALWAYS_CHECK_RMC")]
        public bool AlwaysCheckRMC { get; set; }
    }

    /// <summary>
    /// PLACES is a service that JPL runs for storing and reporting position estimates of spacecraft such as rovers.
    /// This class interfaces with PLACES to compute relative rover positions between site drives.
    /// </summary>
    public class PlacesDB
    {
        //public const string POSE_IDENTIFIER = "-1"; //ask for earliest available pose in sitedrive
        public const string POSE_IDENTIFIER = "^"; //ask for latest available pose in sitedrive

        private ILogger logger;

        private bool debug;

        private PlacesConfig config;

        private string[] views; //best last

        private string cookieValue;

        //avoid hitting the upstream service too hard
        //important: this is explicitly *not* a ConcurrentDictionary
        //we lock on it to serialize requests
        //that handles the case of launching multiple initial requests for the same query in parallel
        //query => response
        private Dictionary<string, string> cache = new Dictionary<string, string>();

        private Dictionary<string, string> bestViewCache = new Dictionary<string, string>();

        private ConcurrentDictionary<SiteDrive, Vector3> cachedOffsetFromStart =
            new ConcurrentDictionary<SiteDrive, Vector3>();

        public PlacesDB(ILogger logger = null, bool debug = false)
        {
            this.logger = logger;
            this.debug = debug;

            config = PlacesConfig.Instance;

            if (string.IsNullOrEmpty(config.Url))
            {
                throw new Exception("no PlacesDB URL");
            }

            views = StringHelper.ParseList(config.View);
            if (views.Length == 0)
            {
                throw new Exception("no PlacesDB views");
            }
            Debug("PlacesDB url: {0}; views {1}", config.Url, String.Join(",", views));

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
                    Debug("reading PlacesDB auth cookie from file \"{0}\"", path);
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
                GetOffsetToStart(new SiteDrive(1, 0)); //test query
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("PlacesDB test query for sitedrive (1, 0) failed: {0}" +
                                                  "; check list at {1}/view/VIEW/rmcs for VIEW in {2}",
                                                  ex.Message, config.Url, String.Join(",", views)),
                                    ex);
            }
        }

        private string Fetch(string query, bool throwOnError = true)
        {
            lock (cache)
            {
                if (cache.ContainsKey(query))
                {
                    var doc = cache[query];
                    if (doc == null && throwOnError)
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
                        if (content.StartsWith("{") || IsValidXml(content)) {
                            cache[query] = content;
                            Debug("request: {0}/{1}, response:\n{2}", config.Url, query, content);
                            return content;
                        } else {
                            Debug("invalid response content for query {0}/{1}: not valid JSON or XML:\n{2}",
                                  config.Url, query, content);
                            break;
                        }
                    }
                    else
                    {
                        err = string.Format("got status code {0} for {1} on try {2}: {3}", response.StatusCode,
                                            config.Url + "/" + query, i, response.ErrorMessage);
                        Debug(err);
                        if (response.StatusCode != HttpStatusCode.BadGateway && //proxies can impose their own timeout
                            response.ResponseStatus != ResponseStatus.TimedOut)
                        {
                            break;
                        }
                    }
                    if (maxSec > 0 && ((UTCTime.Now() - startSec) > maxSec))
                    {
                        err = string.Format("exceeded max time {0} for {1} on try {2}", Fmt.HMS(maxSec * 1000),
                                            config.Url + "/" + query, i);
                        Debug(err);
                        break;
                    }
                }
                if (err == null)
                {
                    err = $"no response after {maxRetries} tries";
                }
#if CACHE_FAILED_REQUESTS
                cache[query] = null;
#endif
                if (throwOnError)
                {
                    throw new Exception(err);
                }
                return null;
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

        private bool IsValidXml(string response)
        {
            try
            {
                (new XmlDocument()).LoadXml(response);
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
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

        private Vector3? FetchOffset(string query, bool throwOnError = true)
        {
            string response = Fetch(query, throwOnError);
            if (response == null)
            {
                if (throwOnError)
                {
                    throw new Exception("fetch failed for query " + query);
                }
                return null;
            }
            Vector3 offset = new Vector3();
            if (response.StartsWith("{"))
            {
                JsonDocument doc = ParseJson(query, response);
                var translations = doc.translations;
                if (translations.Length != 1)
                {
                    if (throwOnError)
                    {
                        throw new Exception("PlacesDB: unexpected number of offsets in response");
                    }
                    return null;
                }
                offset = new Vector3(translations[0].offset[0], translations[0].offset[1], translations[0].offset[2]);
            }
            else
            {
                XmlDocument doc = ParseXml(query, response);
                XmlNodeList nodes = doc.GetElementsByTagName("offset");
                if (nodes.Count != 1)
                {
                    if (throwOnError)
                    {
                        throw new Exception("PlacesDB: unexpected number of offsets in response");
                    }
                    return null;
                }
                offset = new Vector3(double.Parse(nodes[0].Attributes["x"].Value),
                                     double.Parse(nodes[0].Attributes["y"].Value),
                                     double.Parse(nodes[0].Attributes["z"].Value));
            }

            Debug("got offset {0} for {1}", offset, query);

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
                throw new Exception("PlacesDB: orbital metadata Json TODO");
            }

            return CheckXMLDocument(ParseXml(query, response), expected);
        }

        public Dictionary<string, string> GetCache()
        {
            return cache;
        }

        public void SetCache(Dictionary<string, string> cache)
        {
            this.cache = cache;
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
        ///
        /// standard parallel is equator by default but missions not landing close to the equator may use
        /// equirectangular projections with a different standard parallel to get approximately square pixels in the DEM
        ///
        /// easting is distance along standard parallel from longitude=0 if absolute, else from ULC for orbitalIndex
        /// northing is distance along a meridian above equator if absolute, else from ULC for orbitalIndex
        ///
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
                                       Vector2? defULCEastingNorthing = null, Action<string> view = null)
        {
            var ene = GetEastingNorthingElevation(sd, orbitalIndex, false, defULCEastingNorthing, view);
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
            double col = ene.X / mpp.Value.X;
            double row = -1 * ene.Y / mpp.Value.Y;
            var ret = new Vector2(col, row);
            Debug("got orbital pixel (col, row) {0} for {1}", ret, sd);
            return ret;
        }

        /// <summary>
        /// returns X = easting meters, Y = northing meters, Z = elevation meters
        ////
        /// standard parallel is equator by default but missions not landing close to the equator may use
        /// equirectangular projections with a different standard parallel to get approximately square pixels in the DEM
        ///
        /// easting is distance along standard parallel from longitude=0 if absolute, else from ULC for orbitalIndex
        /// northing is distance along a meridian above equator if absolute, else north from ULC for orbitalIndex
        ///
        /// proper behavior with absolute=false requires both upper_left_{easting,northing}_m to be present
        /// in the metadata for orbitalIndex or defULCEastingNorthing to be specified
        /// </summary>
        public Vector3 GetEastingNorthingElevation(SiteDrive sd, int orbitalIndex, bool absolute = true,
                                                   Vector2? defULCEastingNorthing = null, Action<string> view = null)
        {
            string sdRef = GetSDRef(sd);
            string oRef = $"orbital({orbitalIndex})";
            string bestView = GetBestView(sdRef, oRef);
            string query = $"query/primary/{bestView}?from={sdRef}&to={oRef}";

            //offset is in standard mission local level frame: +X north, +Y east, +Z down
            var v = FetchOffset(query).Value;
            double easting = v.Y;
            double northing = v.X;
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
            
            var ret = new Vector3(easting, northing, elevation);
            Debug("got (easting, northing, elevation) {0} from view {1} for {2}", ret, bestView, query);
            if (view != null)
            {
                view(bestView);
            }
            return ret;
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from fromSD to toSite.
        /// </summary>
        public Vector3 GetOffsetToSite(SiteDrive fromSD, int toSite, Action<string> view = null)
        {
            string sdRef = GetSDRef(fromSD);
            string siteRef = GetSDRef(toSite);
            string bestView = GetBestView(sdRef, siteRef);
            string query = $"query/primary/{bestView}?from={sdRef}&to={siteRef}";
            var ret = FetchOffset(query).Value;
            Debug("got offset {0} from view {1} for {2}", ret, bestView, query);
            if (view != null)
            {
                view(bestView);
            }
            return ret;
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from sd to site 1, drive 0 (landing).
        /// </summary>
        public Vector3 GetOffsetToStart(SiteDrive sd, Action<string> view = null)
        {
            return cachedOffsetFromStart.GetOrAdd(sd, _ => GetOffsetToSite(sd, 1, view));
        }

        /// <summary>
        /// Returns the LOCAL_LEVEL frame offset from fromSD to toSD.
        /// </summary>
        public Vector3 GetOffset(SiteDrive fromSD, SiteDrive toSD, Action<string> view = null)
        {
            string fromRef = GetSDRef(fromSD);
            string toRef = GetSDRef(toSD);
            string bestView = GetBestView(fromRef, toRef);
            string query = $"query/primary/{bestView}?from={fromRef}&to={toRef}";
            var ret = FetchOffset(query).Value;
            Debug("got offset {0} from view {1} for {2}", ret, bestView, query);
            if (view != null)
            {
                view(bestView);
            }
            return ret;
        }

        /// <summary>
        /// Formulate a PlacesDB query reference for the given sitedrive (S,D).
        /// If D=0 then the query will be of the form site(S), because queries like rover(S,0) generally don't work.
        /// Otherwise the query will be of the form rover(S,D,POSE_IDENTIFIER)
        /// POSE_IDENTIFIER=^ means the frame of the latest available pose in the sitedrive
        /// POSE_IDENTIFIER=-1 means the frame of the earliest available pose (I think??)
        /// in that site and drive.  Note that in some venues queries like rover(S,D) work but in others they don't,
        /// but adding the carat should work in all cases (per Kevin Grimes).
        /// </summary>
        private static string GetSDRef(SiteDrive sd)
        {
            return GetSDRef(sd.Site, sd.Drive);
        }

        private static string GetSDRef(int site, int drive = 0)
        {
            return drive > 0 ? $"rover({site},{drive},{POSE_IDENTIFIER})" : $"site({site})";
        }

        /// <summary>
        /// Generally one single view is sufficient for a whole mission.  Typically an "interp" view which contains
        /// interpolated poses between drive endpoints.  If the best view doesn't contain a solution for a particular
        /// from/to query pair, then typically the query will then default to "parent" views up to the telemetry
        /// view. (As per Kevin Grimes "deep=true" is implied for from/to queries.)
        /// However, we have seen Things.
        /// Such as the M20 SOPS PlacesDB hanging on from/to queries in the "best_tactical" view.
        /// This is a bit of logic that attempts to find a view which should contain solutions for all the refs.
        /// It is a bit flawed because even if the view/VIEW/rmc/REF query fails, the from/to query could still succeed.
        /// Due to the implied deep=true on the from/to query (there is no deep option for the RMC query at the time of
        /// this writing).
        /// Basically the common cases are still
        /// * only one view available, so that is always simply returned
        /// * two refs and !config.AlwaysCheckRMC, so a (cached) from/to query is attempted for each view in order
        /// </summary>
        private string GetBestView(params string[] refs)
        {
            string all = String.Join(",", refs);
            string[] sorted = (string[])refs.Clone();
            string key = String.Join(",", sorted);

            if (views.Length == 0)
            {
                throw new Exception("no PlacesDB views configured");
            }

            if (views.Length == 1)
            {
                Debug("using only available view {0} for {1}", views[0], all);
                return views[0];
            }

            Exception makeException()
            {
                return new Exception($"no PlacesDB view available for {all}; tried " + String.Join(",", views));
            }

            lock (bestViewCache)
            {
                if (bestViewCache.ContainsKey(key))
                {
                    string ret = bestViewCache[key];
                    if (ret == null)
                    {
                        throw makeException();
                    }
                    Debug("using cached best view {0} for {1}", ret, all);
                    return ret;
                }
                string best = null;
                if (!config.AlwaysCheckRMC && refs.Length == 2)
                {
                    for (int v = views.Length - 1; v >= 0 && best == null; v--)
                    {
                        if (FetchOffset($"query/primary/{views[v]}?from={refs[0]}&to={refs[1]}",
                                        throwOnError: false).HasValue)
                        {
                            best = views[v];
                        }
                    }
                }
                else
                {
                    for (int v = views.Length - 1; v >= 0 && best == null; v--)
                    {
                        for (int r = 0; r < refs.Length; r++)
                        {
                            if (Fetch($"view/{views[v]}/rmc/{refs[r]}", throwOnError: false) == null)
                            {
                                break;
                            }
                            if (r == 0)
                            {
                                best = views[v];
                            }
                        }
                    }
                }
                if (best != null)
                {
                    Debug("found best view {0} for {1}", best, all);
                    bestViewCache[key] = best;
                    return best;
                }
                Debug("no view for {0}", all);
#if CACHE_FAILED_REQUESTS
                bestViewCache[key] = null;
#endif
                throw makeException();
            }
        }

        public SiteDrive GetPreviousEndOfDrive(SiteDrive sd, string view)
        {
            //modeled after gov/nasa/jpl/ammos/ids/places/client/Plinth.java in https://github.jpl.nasa.gov/MIPL/PLACES

            int landingSite = 0;

            // Go back 5 sites (to avoid taking too long)
            string searchStart = $"SITE({Math.Max(landingSite, sd.Site - 5)})";

            //always use SITE(x) instead of ROVER(x) or ROVER(x,0)
            string searchEnd = null;
            if (sd.Drive > 1) {
                searchEnd = $"ROVER({sd.Site},{sd.Drive - 1},^)";
            } else if (sd.Drive == 1) {
                searchEnd = $"SITE({sd.Site})";
            } else if (sd.Site > landingSite) {
                searchEnd = $"ROVER({sd.Site - 1},^,^)";
            } else {
                throw new Exception("no end-of-drive RMC prior to " + sd);
            }

            string query = $"view/{view}/rmcs?from={searchStart}&to={searchEnd}";
            string response = Fetch(query);

            XmlDocument doc = ParseXml(query, response);

            XmlNodeList nodes = doc.GetElementsByTagName("rmc");
            if (nodes.Count == 0)
            {
                throw new Exception($"error getting previous end-of-drive for {sd}: query {query} returned no results");
            }

            //the RMC are returned in descending order - the first returned RMC is the newest
            //(and the list may be truncated due to pagination, but it's the *older* entries that are truncated) 
            var elt = nodes[0];
            string site = elt.Attributes["site"]?.Value;
            string drive = elt.Attributes["drive"]?.Value; //null ok
            if (site != null && int.TryParse(site, out int s))
            {
                int d = 0;
                if (drive == null || int.TryParse(drive, out d))
                {
                    return new SiteDrive(s, d);
                }
            }

            throw new Exception($"error getting previous end-of-drive for {sd}: " +
                                $"failed to parse site={site}, drive={drive}");
        }
    }
}
